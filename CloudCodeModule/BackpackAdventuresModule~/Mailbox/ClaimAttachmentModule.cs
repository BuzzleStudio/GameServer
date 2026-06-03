using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

public class ClaimAttachmentModule
{
    private const int ClaimWriteRetries = 3;

    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IExecutionContext _context;
    private readonly IGameApiClient _gameApiClient;
    private readonly ILogger<ClaimAttachmentModule> _logger;

    public ClaimAttachmentModule(IExecutionContext context, IGameApiClient gameApiClient, ILogger<ClaimAttachmentModule> logger)
    {
        _context = context;
        _gameApiClient = gameApiClient;
        _logger = logger;
    }

    [CloudCodeFunction("ClaimAttachment")]
    public async Task<ApiResponse<ClaimAttachmentResponse>> ClaimAttachmentAsync(ClaimAttachmentRequest request)
    {
        var sw = Stopwatch.StartNew();
        var claimRequest = request ?? new ClaimAttachmentRequest();
        var playerId = _context.PlayerId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(claimRequest.MailId))
            throw new ArgumentException(MailboxError.InvalidInput);

        var requestId = string.IsNullOrEmpty(claimRequest.RequestId) ? null : claimRequest.RequestId;
        if (requestId != null)
        {
            var cached = await IdempotencyService.TryGetCachedResponseAsync(_gameApiClient, _context, playerId, requestId, "ClaimAttachment", claimRequest.MailId);
            if (cached != null)
                return ApiResponse<ClaimAttachmentResponse>.Ok(new ClaimAttachmentResponse { MailId = claimRequest.MailId, AlreadyClaimed = false }, sw);
        }

        ClaimAttachmentResponse result = IsGlobalMail(claimRequest.MailId, claimRequest.MailType)
            ? await ClaimGlobalAttachmentAsync(playerId, claimRequest.MailId, requestId)
            : await ClaimUserAttachmentAsync(playerId, claimRequest.MailId, requestId);

        if (requestId != null && !result.AlreadyClaimed)
            PendingIdemStore = StoreIdemSafeAsync(playerId, requestId, "ClaimAttachment", claimRequest.MailId, new { alreadyClaimed = false });

        return ApiResponse<ClaimAttachmentResponse>.Ok(result, sw);
    }

    [CloudCodeFunction("ClaimAllAttachments")]
    public async Task<ApiResponse<ClaimAllAttachmentsResponse>> ClaimAllAttachmentsAsync(ClaimAllAttachmentsRequest request)
    {
        var sw = Stopwatch.StartNew();
        var playerId = _context.PlayerId ?? string.Empty;
        var scope = NormalizeClaimAllScope(request?.MailType);
        var response = new ClaimAllAttachmentsResponse();

        if (scope == "global" || scope == "all")
            await ClaimAllGlobalAttachmentsAsync(playerId, request?.RequestId, response);

        if (scope == "user" || scope == "all")
            await ClaimAllUserAttachmentsAsync(playerId, request?.RequestId, response);

        return ApiResponse<ClaimAllAttachmentsResponse>.Ok(response, sw);
    }

    private static ClaimAttachmentRequest NormalizeClaimAttachmentRequest(object? request)
    {
        if (request == null)
            return new ClaimAttachmentRequest();

        if (request is ClaimAttachmentRequest typedRequest)
            return typedRequest;

        if (request is string mailId)
            return new ClaimAttachmentRequest { MailId = mailId };

        if (request is JsonElement json)
            return NormalizeClaimAttachmentRequest(json);

        try
        {
            var serialized = JsonSerializer.Serialize(request, RequestJsonOptions);
            var parsed = JsonSerializer.Deserialize<ClaimAttachmentRequest>(serialized, RequestJsonOptions);
            if (parsed != null)
                return parsed;
        }
        catch (JsonException)
        {
        }
        catch (NotSupportedException)
        {
        }

        throw new ArgumentException(MailboxError.InvalidInput);
    }

    private static ClaimAttachmentRequest NormalizeClaimAttachmentRequest(JsonElement request)
    {
        if (request.ValueKind == JsonValueKind.String)
            return new ClaimAttachmentRequest { MailId = request.GetString() ?? string.Empty };

        if (request.ValueKind == JsonValueKind.Object)
        {
            var parsed = request.Deserialize<ClaimAttachmentRequest>(RequestJsonOptions);
            return parsed ?? new ClaimAttachmentRequest();
        }

        throw new ArgumentException(MailboxError.InvalidInput);
    }

    // Load collection ONCE + state-with-lock ONCE. Grant all candidates. Write state ONCE.
    // On 409: bounded retry re-reads fresh state and re-applies claim flags for IDs whose
    // rewards were already granted — prevents double-grant on next ClaimAll invocation.
    private async Task ClaimAllGlobalAttachmentsAsync(string playerId, string? requestId, ClaimAllAttachmentsResponse response)
    {
        var collection = await CloudSaveHelper.GetCustomDataAsync<GlobalMailCollection>(_gameApiClient, _context, MailboxConstants.KeyMailsAll);
        if (collection?.Mails == null) return;

        var candidateIds = new List<string>();
        foreach (var payload in collection.Mails)
        {
            var mail = payload?.Mail;
            if (mail == null || string.IsNullOrEmpty(mail.MessageId)) continue;
            if (!mail.IsAvailable) continue;
            if (!MailSchemaHelper.IsVisibleToPlayer(mail, playerId)) continue;
            if (!MailSchemaHelper.HasAttachments(mail)) continue;
            candidateIds.Add(mail.MessageId);
        }
        if (candidateIds.Count == 0) return;

        var (state, writeLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerGlobalMailState>(_gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState);
        state ??= new PlayerGlobalMailState();
        MailSchemaHelper.MigrateLegacyMetadata(state);

        var newlyClaimedIds = new List<string>();
        foreach (var mailId in candidateIds)
        {
            try
            {
                var result = await ClaimGlobalAttachmentCoreAsync(playerId, mailId, collection, state, BuildPerMailRequestId(requestId, mailId));
                AddClaimAllResult(response, "global", result);
                if (!result.AlreadyClaimed) newlyClaimedIds.Add(mailId);
            }
            catch (InvalidOperationException ex) when (IsClaimAllSkippable(ex))
            {
                response.SkippedCount++;
                response.Results.Add(new ClaimAllAttachmentResult
                {
                    MailId = mailId,
                    MailType = "global",
                    SkippedReason = ResolveMailboxError(ex.Message)
                });
            }
        }

        if (newlyClaimedIds.Count == 0) return;

        try
        {
            await CloudSaveHelper.SetPlayerDataAsync(_gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState, state, writeLock);
        }
        catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex))
        {
            var persisted = await PersistGlobalClaimFlagsWithRetryAsync(playerId, newlyClaimedIds);
            if (!persisted)
                _logger.LogError(
                    "ClaimAllGlobal: granted {Count} reward(s) but failed to persist claim flags after {Retries} retries. Risk: re-grant on next call. PlayerId={PlayerId} MailIds={MailIds}",
                    newlyClaimedIds.Count, ClaimWriteRetries, playerId, string.Join(",", newlyClaimedIds));
        }
    }

    // Load mailbox-with-lock ONCE. Grant all candidates. Write ONCE.
    // On 409: bounded retry re-reads fresh mailbox and re-applies IsClaimed flags.
    private async Task ClaimAllUserAttachmentsAsync(string playerId, string? requestId, ClaimAllAttachmentsResponse response)
    {
        var (mailbox, writeLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerUserMailbox>(_gameApiClient, _context, playerId, MailboxConstants.KeyUserItems);
        if (mailbox?.Mails == null) return;

        var candidateIds = new List<string>();
        foreach (var mail in mailbox.Mails)
        {
            if (mail == null || string.IsNullOrEmpty(mail.MessageId)) continue;
            if (mail.IsExpired()) continue;
            if (!MailSchemaHelper.HasAttachments(mail)) continue;
            candidateIds.Add(mail.MessageId);
        }
        if (candidateIds.Count == 0) return;

        var newlyClaimedIds = new List<string>();
        foreach (var mailId in candidateIds)
        {
            try
            {
                var result = await ClaimUserAttachmentCoreAsync(playerId, mailId, mailbox, BuildPerMailRequestId(requestId, mailId));
                AddClaimAllResult(response, "user", result);
                if (!result.AlreadyClaimed) newlyClaimedIds.Add(mailId);
            }
            catch (InvalidOperationException ex) when (IsClaimAllSkippable(ex))
            {
                response.SkippedCount++;
                response.Results.Add(new ClaimAllAttachmentResult
                {
                    MailId = mailId,
                    MailType = "user",
                    SkippedReason = ResolveMailboxError(ex.Message)
                });
            }
        }

        if (newlyClaimedIds.Count == 0) return;

        try
        {
            await CloudSaveHelper.SetPlayerDataAsync(_gameApiClient, _context, playerId, MailboxConstants.KeyUserItems, mailbox, writeLock);
        }
        catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex))
        {
            var persisted = await PersistUserClaimFlagsWithRetryAsync(playerId, newlyClaimedIds);
            if (!persisted)
                _logger.LogError(
                    "ClaimAllUser: granted {Count} reward(s) but failed to persist claim flags after {Retries} retries. Risk: re-grant on next call. PlayerId={PlayerId} MailIds={MailIds}",
                    newlyClaimedIds.Count, ClaimWriteRetries, playerId, string.Join(",", newlyClaimedIds));
        }
    }

    // Re-read fresh state with a new lock, re-apply claim flags for already-granted IDs, write.
    // Retries up to ClaimWriteRetries times on continued 409s.
    private async Task<bool> PersistGlobalClaimFlagsWithRetryAsync(string playerId, List<string> newlyClaimedIds)
    {
        for (int attempt = 0; attempt < ClaimWriteRetries; attempt++)
        {
            try
            {
                var (freshState, freshLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerGlobalMailState>(
                    _gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState);
                freshState ??= new PlayerGlobalMailState();
                MailSchemaHelper.MigrateLegacyMetadata(freshState);
                foreach (var id in newlyClaimedIds)
                {
                    var meta = MailSchemaHelper.GetOrCreateMetadata(freshState, id);
                    meta.IsClaim = true;
                    meta.IsRead = true;
                }
                await CloudSaveHelper.SetPlayerDataAsync(_gameApiClient, _context, playerId,
                    MailboxConstants.KeyGlobalState, freshState, freshLock);
                return true;
            }
            catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex))
            {
            }
        }
        return false;
    }

    // Re-read fresh mailbox with a new lock, re-apply IsClaimed flags for already-granted IDs, write.
    private async Task<bool> PersistUserClaimFlagsWithRetryAsync(string playerId, List<string> newlyClaimedIds)
    {
        for (int attempt = 0; attempt < ClaimWriteRetries; attempt++)
        {
            try
            {
                var (freshMailbox, freshLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerUserMailbox>(
                    _gameApiClient, _context, playerId, MailboxConstants.KeyUserItems);
                freshMailbox ??= new PlayerUserMailbox();
                foreach (var id in newlyClaimedIds)
                {
                    var mail = freshMailbox.FindById(id);
                    if (mail == null) continue;
                    mail.MailMetaData.IsClaimed = true;
                    mail.MailMetaData.IsRead = true;
                }
                await CloudSaveHelper.SetPlayerDataAsync(_gameApiClient, _context, playerId,
                    MailboxConstants.KeyUserItems, freshMailbox, freshLock);
                return true;
            }
            catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex))
            {
            }
        }
        return false;
    }

    private async Task<ClaimAttachmentResponse> ClaimGlobalAttachmentAsync(string playerId, string mailId, string? requestId)
    {
        var collection = await CloudSaveHelper.GetCustomDataAsync<GlobalMailCollection>(_gameApiClient, _context, MailboxConstants.KeyMailsAll);
        if (GlobalMailStore.FindById(collection, mailId)?.Mail == null)
        {
            var v1Index = await CloudSaveHelper.GetCustomDataAsync<GlobalMailIndex>(_gameApiClient, _context, MailboxConstants.KeyGlobalMailIndex);
            var v1Mail = v1Index?.Mails.Find(m => m.GlobalMailId == mailId);
            if (v1Mail == null) throw new InvalidOperationException(MailboxError.MailNotFound);
            return await ClaimLegacyGlobalAttachmentAsync(playerId, v1Mail, requestId);
        }

        var (state, writeLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerGlobalMailState>(_gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState);
        state ??= new PlayerGlobalMailState();
        MailSchemaHelper.MigrateLegacyMetadata(state);

        var result = await ClaimGlobalAttachmentCoreAsync(playerId, mailId, collection!, state, requestId);
        if (result.AlreadyClaimed) return result;

        try
        {
            await CloudSaveHelper.SetPlayerDataAsync(_gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState, state, writeLock);
        }
        catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex))
        {
            return new ClaimAttachmentResponse { MailId = mailId, AlreadyClaimed = true };
        }

        return result;
    }

    // Core: O(1) dict lookup. No reads/writes — caller owns state write.
    private async Task<ClaimAttachmentResponse> ClaimGlobalAttachmentCoreAsync(
        string playerId,
        string mailId,
        GlobalMailCollection collection,
        PlayerGlobalMailState state,
        string? requestId)
    {
        var payload = GlobalMailStore.FindById(collection, mailId);
        if (payload?.Mail == null)
            throw new InvalidOperationException(MailboxError.MailNotFound);

        var metadata = MailSchemaHelper.GetOrCreateMetadata(state, mailId);
        if (metadata.IsDelete)
            throw new InvalidOperationException(MailboxError.MailNotFound);
        if (metadata.IsClaim)
            return new ClaimAttachmentResponse { MailId = mailId, AlreadyClaimed = true };
        if (payload.Mail.IsExpired)
            throw new InvalidOperationException(MailboxError.MailExpired);
        if (!payload.Mail.IsAvailable || !MailSchemaHelper.IsVisibleToPlayer(payload.Mail, playerId))
            throw new InvalidOperationException(MailboxError.MailNotFound);
        if (!MailSchemaHelper.HasAttachments(payload.Mail))
            throw new InvalidOperationException(MailboxError.NoAttachment);

        var attachments = MailSchemaHelper.ToAttachments(payload.Mail.Attachments);
        await GrantRewardsAsync(playerId, mailId, attachments ?? new List<MailAttachment>(), requestId);
        metadata.IsClaim = true;
        metadata.IsRead = true;

        return new ClaimAttachmentResponse { MailId = mailId, AlreadyClaimed = false, GrantedAttachments = attachments };
    }

    private async Task<ClaimAttachmentResponse> ClaimLegacyGlobalAttachmentAsync(string playerId, GlobalMailItem mail, string? requestId)
    {
        var (state, writeLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerGlobalMailState>(_gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState);
        state ??= new PlayerGlobalMailState();
        MailSchemaHelper.MigrateLegacyMetadata(state);
        var metadata = MailSchemaHelper.GetOrCreateMetadata(state, mail.GlobalMailId);
        if (metadata.IsDelete)
            throw new InvalidOperationException(MailboxError.MailNotFound);
        if (metadata.IsClaim)
            return new ClaimAttachmentResponse { MailId = mail.GlobalMailId, AlreadyClaimed = true };
        if (mail.IsExpired())
            throw new InvalidOperationException(MailboxError.MailExpired);
        if (mail.Attachments == null || mail.Attachments.Count == 0)
            throw new InvalidOperationException(MailboxError.NoAttachment);

        await GrantRewardsAsync(playerId, mail.GlobalMailId, mail.Attachments, requestId);
        metadata.IsClaim = true;
        metadata.IsRead = true;
        await CloudSaveHelper.SetPlayerDataAsync(_gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState, state, writeLock);
        return new ClaimAttachmentResponse { MailId = mail.GlobalMailId, AlreadyClaimed = false, GrantedAttachments = mail.Attachments };
    }

    private async Task<ClaimAttachmentResponse> ClaimUserAttachmentAsync(string playerId, string mailId, string? requestId)
    {
        var (mailbox, writeLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerUserMailbox>(_gameApiClient, _context, playerId, MailboxConstants.KeyUserItems);
        mailbox ??= new PlayerUserMailbox();

        var result = await ClaimUserAttachmentCoreAsync(playerId, mailId, mailbox, requestId);
        if (result.AlreadyClaimed) return result;

        try
        {
            await CloudSaveHelper.SetPlayerDataAsync(_gameApiClient, _context, playerId, MailboxConstants.KeyUserItems, mailbox, writeLock);
        }
        catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex))
        {
            return new ClaimAttachmentResponse { MailId = mailId, AlreadyClaimed = true };
        }

        return result;
    }

    // Core: O(1) dict lookup. No reads/writes — caller owns mailbox write.
    private async Task<ClaimAttachmentResponse> ClaimUserAttachmentCoreAsync(
        string playerId,
        string mailId,
        PlayerUserMailbox mailbox,
        string? requestId)
    {
        var mail = mailbox.FindById(mailId);
        if (mail == null) throw new InvalidOperationException(MailboxError.MailNotFound);
        if (mail.MailMetaData.IsClaimed)
            return new ClaimAttachmentResponse { MailId = mailId, AlreadyClaimed = true };
        if (mail.IsExpired())
            throw new InvalidOperationException(MailboxError.MailExpired);
        if (!MailSchemaHelper.HasAttachments(mail))
            throw new InvalidOperationException(MailboxError.NoAttachment);

        var attachments = MailSchemaHelper.ToAttachments(mail.MailInfo.Attachment);
        await GrantRewardsAsync(playerId, mailId, attachments ?? new List<MailAttachment>(), requestId);
        mail.MailMetaData.IsClaimed = true;
        mail.MailMetaData.IsRead = true;

        return new ClaimAttachmentResponse { MailId = mailId, AlreadyClaimed = false, GrantedAttachments = attachments };
    }

    // Idempotency store is best-effort and OFF the response critical path: not awaited, so the client
    // gets its result one Cloud Save POST sooner. Correctness never depends on it — a duplicate
    // requestId that misses an unpersisted cache simply re-runs and is caught by the writeLock +
    // IsClaim guard (returns AlreadyClaimed, no double-grant). The cached idem GET returns
    // synchronously, so this method only backgrounds at the POST await. On warm instances it
    // completes before the next request; on cold recycle it may be dropped (acceptable).
    // Exposed via PendingIdemStore so tests can await completion deterministically.
    internal Task? PendingIdemStore;

    private async Task StoreIdemSafeAsync(string playerId, string requestId, string operation, string mailId, object summary)
    {
        try
        {
            await IdempotencyService.StoreResponseAsync(_gameApiClient, _context, playerId, requestId, operation, mailId, summary);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Idempotency store (fire-and-forget) failed for {Operation} {MailId}", operation, mailId);
        }
    }

    private async Task GrantRewardsAsync(string playerId, string mailId, IReadOnlyList<MailAttachment> attachments, string? requestId)
    {
        var idempotencyKey = string.IsNullOrEmpty(requestId) ? $"claim-{playerId}-{mailId}" : $"claim-{requestId}";
        try
        {
            await RewardGrant.GrantRewardsAsync(_gameApiClient, _context, playerId, attachments, idempotencyKey, _logger);
        }
        catch (RetryableGrantException)
        {
            throw new InvalidOperationException(MailboxError.GrantUnavailable);
        }
    }

    private static bool IsGlobalMail(string mailId, string mailType)
    {
        return string.Equals(mailType, "global", StringComparison.OrdinalIgnoreCase)
               || (!string.IsNullOrEmpty(mailId) && mailId.StartsWith("gm_", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeClaimAllScope(string? mailType)
    {
        if (string.IsNullOrWhiteSpace(mailType)) return "all";
        var value = mailType.Trim().ToLowerInvariant();
        if (value == "all" || value == "global" || value == "user") return value;
        throw new ArgumentException(MailboxError.InvalidInput);
    }

    private static string? BuildPerMailRequestId(string? requestId, string mailId)
    {
        return string.IsNullOrEmpty(requestId) ? null : $"{requestId}:{mailId}";
    }

    private static void AddClaimAllResult(
        ClaimAllAttachmentsResponse response,
        string mailType,
        ClaimAttachmentResponse result)
    {
        var attachments = result.GrantedAttachments ?? new List<MailAttachment>();
        response.Results.Add(new ClaimAllAttachmentResult
        {
            MailId = result.MailId,
            MailType = mailType,
            AlreadyClaimed = result.AlreadyClaimed,
            GrantedAttachments = attachments.Count == 0 ? null : attachments
        });

        if (result.AlreadyClaimed)
        {
            response.AlreadyClaimedCount++;
            return;
        }

        response.ClaimedCount++;
        response.GrantedAttachments.AddRange(attachments);
    }

    private static bool IsClaimAllSkippable(Exception ex)
    {
        var error = ResolveMailboxError(ex.Message);
        return error == MailboxError.MailNotFound ||
               error == MailboxError.MailExpired ||
               error == MailboxError.NoAttachment;
    }

    private static string ResolveMailboxError(string message)
    {
        string[] knownCodes =
        {
            MailboxError.InvalidInput,
            MailboxError.MailNotFound,
            MailboxError.MailExpired,
            MailboxError.AlreadyClaimed,
            MailboxError.NoAttachment,
            MailboxError.GrantUnavailable
        };

        foreach (var code in knownCodes)
        {
            if (message.IndexOf(code, StringComparison.OrdinalIgnoreCase) >= 0)
                return code;
        }

        return message;
    }
}
