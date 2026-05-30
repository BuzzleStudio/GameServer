using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

public class ClaimAttachmentModule
{
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
    public async Task<ApiResponse<ClaimAttachmentResponse>> ClaimAttachmentAsync(object request)
    {
        var claimRequest = NormalizeClaimAttachmentRequest(request);
        var playerId = _context.PlayerId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(claimRequest.MailId))
            throw new ArgumentException(MailboxError.InvalidInput);

        var requestId = string.IsNullOrEmpty(claimRequest.RequestId) ? null : claimRequest.RequestId;
        if (requestId != null)
        {
            var cached = await IdempotencyService.TryGetCachedResponseAsync(_gameApiClient, _context, playerId, requestId, "ClaimAttachment", claimRequest.MailId);
            if (cached != null)
                return ApiResponse<ClaimAttachmentResponse>.Ok(new ClaimAttachmentResponse { MailId = claimRequest.MailId, AlreadyClaimed = false });
        }

        ClaimAttachmentResponse result = IsGlobalMail(claimRequest.MailId, claimRequest.MailType)
            ? await ClaimGlobalAttachmentAsync(playerId, claimRequest.MailId, requestId)
            : await ClaimUserAttachmentAsync(playerId, claimRequest.MailId, requestId);

        if (requestId != null && !result.AlreadyClaimed)
            await IdempotencyService.StoreResponseAsync(_gameApiClient, _context, playerId, requestId, "ClaimAttachment", claimRequest.MailId, new { alreadyClaimed = false });

        return ApiResponse<ClaimAttachmentResponse>.Ok(result);
    }

    [CloudCodeFunction("ClaimAllAttachments")]
    public async Task<ApiResponse<ClaimAllAttachmentsResponse>> ClaimAllAttachmentsAsync(ClaimAllAttachmentsRequest request)
    {
        var playerId = _context.PlayerId ?? string.Empty;
        var scope = NormalizeClaimAllScope(request?.MailType);
        var response = new ClaimAllAttachmentsResponse();

        if (scope == "global" || scope == "all")
            await ClaimAllGlobalAttachmentsAsync(playerId, request?.RequestId, response);

        if (scope == "user" || scope == "all")
            await ClaimAllUserAttachmentsAsync(playerId, request?.RequestId, response);

        return ApiResponse<ClaimAllAttachmentsResponse>.Ok(response);
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

    private async Task ClaimAllGlobalAttachmentsAsync(string playerId, string? requestId, ClaimAllAttachmentsResponse response)
    {
        var collection = await CloudSaveHelper.GetCustomDataAsync<GlobalMailCollection>(_gameApiClient, _context, MailboxConstants.KeyMailsAll);
        if (collection?.Mails == null) return;

        var candidateIds = new List<string>();
        foreach (var payload in collection.Mails)
        {
            var mail = payload?.Mail;
            if (mail == null) continue;
            if (string.IsNullOrEmpty(mail.MessageId)) continue;
            if (!mail.IsAvailable) continue;
            if (!MailSchemaHelper.IsVisibleToPlayer(mail, playerId)) continue;
            if (!MailSchemaHelper.HasAttachments(mail)) continue;
            candidateIds.Add(mail.MessageId);
        }

        foreach (var mailId in candidateIds)
            await ClaimOneForClaimAllAsync(playerId, mailId, "global", BuildPerMailRequestId(requestId, mailId), response);
    }

    private async Task ClaimAllUserAttachmentsAsync(string playerId, string? requestId, ClaimAllAttachmentsResponse response)
    {
        var mailbox = await CloudSaveHelper.GetPlayerDataAsync<PlayerUserMailbox>(
            _gameApiClient, _context, playerId, MailboxConstants.KeyUserItems);
        if (mailbox?.Mails == null) return;

        var candidateIds = new List<string>();
        foreach (var mail in mailbox.Mails)
        {
            if (mail == null) continue;
            if (string.IsNullOrEmpty(mail.MessageId)) continue;
            if (mail.IsExpired()) continue;
            if (!MailSchemaHelper.HasAttachments(mail)) continue;
            candidateIds.Add(mail.MessageId);
        }

        foreach (var mailId in candidateIds)
            await ClaimOneForClaimAllAsync(playerId, mailId, "user", BuildPerMailRequestId(requestId, mailId), response);
    }

    private async Task ClaimOneForClaimAllAsync(
        string playerId,
        string mailId,
        string mailType,
        string? requestId,
        ClaimAllAttachmentsResponse response)
    {
        try
        {
            var result = mailType == "global"
                ? await ClaimGlobalAttachmentAsync(playerId, mailId, requestId)
                : await ClaimUserAttachmentAsync(playerId, mailId, requestId);
            AddClaimAllResult(response, mailType, result);
        }
        catch (InvalidOperationException ex) when (IsClaimAllSkippable(ex))
        {
            response.SkippedCount++;
            response.Results.Add(new ClaimAllAttachmentResult
            {
                MailId = mailId,
                MailType = mailType,
                SkippedReason = ResolveMailboxError(ex.Message)
            });
        }
    }

    private async Task<ClaimAttachmentResponse> ClaimGlobalAttachmentAsync(string playerId, string mailId, string? requestId)
    {
        var collection = await CloudSaveHelper.GetCustomDataAsync<GlobalMailCollection>(_gameApiClient, _context, MailboxConstants.KeyMailsAll);
        var payload = GlobalMailStore.FindById(collection?.Mails, mailId);
        if (payload?.Mail == null)
        {
            var v1Index = await CloudSaveHelper.GetCustomDataAsync<GlobalMailIndex>(_gameApiClient, _context, MailboxConstants.KeyGlobalMailIndex);
            var v1Mail = v1Index?.Mails.Find(m => m.GlobalMailId == mailId);
            if (v1Mail == null) throw new InvalidOperationException(MailboxError.MailNotFound);
            return await ClaimLegacyGlobalAttachmentAsync(playerId, v1Mail, requestId);
        }

        var (state, writeLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerGlobalMailState>(_gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState);
        state ??= new PlayerGlobalMailState();
        MailSchemaHelper.MigrateLegacyMetadata(state);
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

        try
        {
            await CloudSaveHelper.SetPlayerDataAsync(_gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState, state, writeLock);
        }
        catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex))
        {
            return new ClaimAttachmentResponse { MailId = mailId, AlreadyClaimed = true };
        }

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
        var mail = mailbox.Mails.Find(m => m.MessageId == mailId);
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

        try
        {
            await CloudSaveHelper.SetPlayerDataAsync(_gameApiClient, _context, playerId, MailboxConstants.KeyUserItems, mailbox, writeLock);
        }
        catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex))
        {
            return new ClaimAttachmentResponse { MailId = mailId, AlreadyClaimed = true };
        }

        return new ClaimAttachmentResponse { MailId = mailId, AlreadyClaimed = false, GrantedAttachments = attachments };
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
