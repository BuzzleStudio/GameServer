using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

/// <summary>
/// Grants a mail attachment to the calling player.
/// Per §5.4:
///   1. Check idempotency cache.
///   2. Read state with writeLock.
///   3. Validate not-already-claimed, not-expired, has-attachments.
///   4. Call IRewardGrantService BEFORE writing claimed=true.
///   5. Write claimed=true under writeLock.
///   6. Store idempotency entry (best-effort).
/// </summary>
public class ClaimAttachmentModule
{
    private readonly IExecutionContext _context;
    private readonly IGameApiClient _gameApiClient;
    private readonly ILogger<ClaimAttachmentModule> _logger;

    public ClaimAttachmentModule(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        ILogger<ClaimAttachmentModule> logger)
    {
        _context = context;
        _gameApiClient = gameApiClient;
        _logger = logger;
    }

    [CloudCodeFunction("ClaimAttachment")]
    public async Task<ClaimAttachmentResponse> ClaimAttachmentAsync(ClaimAttachmentRequest request)
    {
        var playerId = _context.PlayerId ?? string.Empty;
        _logger.LogInformation("ClaimAttachment called for {PlayerId}, mailId={MailId}, type={MailType}",
            playerId, request.MailId, request.MailType);

        if (string.IsNullOrWhiteSpace(request.MailId))
            throw new ArgumentException(MailboxError.InvalidInput);

        var requestId = string.IsNullOrEmpty(request.RequestId) ? null : request.RequestId;

        // Step 1: Check idempotency cache
        if (requestId != null)
        {
            var cached = await IdempotencyService.TryGetCachedResponseAsync(
                _gameApiClient, _context, playerId, requestId, "ClaimAttachment", request.MailId);
            if (cached != null)
            {
                _logger.LogInformation("ClaimAttachment: idempotent replay for requestId={RequestId}", requestId);
                return new ClaimAttachmentResponse
                {
                    Success        = true,
                    MailId         = request.MailId,
                    AlreadyClaimed = false
                };
            }
        }

        var isGlobal = string.Equals(request.MailType, "global", StringComparison.OrdinalIgnoreCase);

        ClaimAttachmentResponse result;
        if (isGlobal)
            result = await ClaimGlobalAttachmentAsync(playerId, request.MailId, requestId);
        else
            result = await ClaimUserAttachmentAsync(playerId, request.MailId, requestId);

        // Step 6: Store idempotency entry (best-effort)
        if (requestId != null && !result.AlreadyClaimed)
        {
            try
            {
                await IdempotencyService.StoreResponseAsync(
                    _gameApiClient, _context, playerId, requestId, "ClaimAttachment", request.MailId,
                    new { success = true, alreadyClaimed = false });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ClaimAttachment: failed to store idempotency entry (non-fatal)");
            }
        }

        return result;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task<ClaimAttachmentResponse> ClaimGlobalAttachmentAsync(
        string playerId, string mailId, string? requestId)
    {
        // Load the payload from v2 key
        var payload = await CloudSaveHelper.GetCustomDataAsync<GlobalMailPayload>(
            _gameApiClient, _context,
            string.Format(MailboxConstants.KeyGlobalMailPayloadFmt, mailId));

        if (payload == null)
        {
            // Fallback: check v1 legacy index for back-compat
            var v1Index = await CloudSaveHelper.GetCustomDataAsync<GlobalMailIndex>(
                _gameApiClient, _context, MailboxConstants.KeyGlobalMailIndex);
            var v1Mail = v1Index?.Mails.Find(m => m.GlobalMailId == mailId);
            if (v1Mail == null) throw new InvalidOperationException(MailboxError.MailNotFound);
            return await ClaimGlobalAttachmentFromV1Async(playerId, v1Mail, requestId);
        }

        // Step 2: Read player global state with writeLock
        var (state, writeLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerGlobalMailState>(
            _gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState);
        state ??= new PlayerGlobalMailState();

        // Already claimed — return immediately without Economy call
        if (state.ClaimedIds.Contains(mailId))
            return new ClaimAttachmentResponse { Success = true, MailId = mailId, AlreadyClaimed = true };

        // Step 3: Validate
        if (payload.IsExpired())
            throw new InvalidOperationException(MailboxError.MailExpired);

        if (payload.Attachments == null || payload.Attachments.Count == 0)
            throw new InvalidOperationException(MailboxError.NoAttachment);

        // Step 4: Grant BEFORE writing claimed=true
        await GrantRewardsAsync(playerId, mailId, payload.Attachments, requestId);

        // Step 5: Write claimed=true under writeLock
        state.ClaimedIds.Add(mailId);
        if (!state.ReadIds.Contains(mailId)) state.ReadIds.Add(mailId);

        try
        {
            await CloudSaveHelper.SetPlayerDataAsync(
                _gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState, state, writeLock);
        }
        catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex))
        {
            // §5.4 step 9: grant issued but state write failed — at-most-once tradeoff
            _logger.LogWarning(
                "ClaimAttachment (global): write conflict after grant for mailId={MailId} — returning AlreadyClaimed (at-most-once tradeoff)",
                mailId);
            return new ClaimAttachmentResponse { Success = true, MailId = mailId, AlreadyClaimed = true };
        }

        _logger.LogInformation("Global attachment claimed for mailId={MailId} by {PlayerId}", mailId, playerId);
        return new ClaimAttachmentResponse
        {
            Success            = true,
            MailId             = mailId,
            AlreadyClaimed     = false,
            GrantedAttachments = payload.Attachments
        };
    }

    private async Task<ClaimAttachmentResponse> ClaimGlobalAttachmentFromV1Async(
        string playerId, GlobalMailItem v1Mail, string? requestId)
    {
        var (state, writeLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerGlobalMailState>(
            _gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState);
        state ??= new PlayerGlobalMailState();

        if (state.ClaimedIds.Contains(v1Mail.GlobalMailId))
            return new ClaimAttachmentResponse { Success = true, MailId = v1Mail.GlobalMailId, AlreadyClaimed = true };

        if (v1Mail.IsExpired())
            throw new InvalidOperationException(MailboxError.MailExpired);

        if (v1Mail.Attachments == null || v1Mail.Attachments.Count == 0)
            throw new InvalidOperationException(MailboxError.NoAttachment);

        await GrantRewardsAsync(playerId, v1Mail.GlobalMailId, v1Mail.Attachments, requestId);

        state.ClaimedIds.Add(v1Mail.GlobalMailId);
        if (!state.ReadIds.Contains(v1Mail.GlobalMailId)) state.ReadIds.Add(v1Mail.GlobalMailId);

        try
        {
            await CloudSaveHelper.SetPlayerDataAsync(
                _gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState, state, writeLock);
        }
        catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex))
        {
            _logger.LogWarning("ClaimAttachment (v1 global): write conflict after grant — returning AlreadyClaimed");
            return new ClaimAttachmentResponse { Success = true, MailId = v1Mail.GlobalMailId, AlreadyClaimed = true };
        }

        return new ClaimAttachmentResponse
        {
            Success            = true,
            MailId             = v1Mail.GlobalMailId,
            AlreadyClaimed     = false,
            GrantedAttachments = v1Mail.Attachments
        };
    }

    private async Task<ClaimAttachmentResponse> ClaimUserAttachmentAsync(
        string playerId, string mailId, string? requestId)
    {
        // Step 2: Read user mailbox with writeLock
        var (mailbox, writeLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerUserMailbox>(
            _gameApiClient, _context, playerId, MailboxConstants.KeyUserItems);
        mailbox ??= new PlayerUserMailbox();

        var mail = mailbox.Mails.Find(m => m.MailId == mailId);
        if (mail == null) throw new InvalidOperationException(MailboxError.MailNotFound);

        // Already claimed — return immediately without Economy call
        if (mail.AttachmentClaimed)
            return new ClaimAttachmentResponse { Success = true, MailId = mailId, AlreadyClaimed = true };

        // Step 3: Validate
        if (mail.IsExpired())
            throw new InvalidOperationException(MailboxError.MailExpired);

        if (mail.Attachments == null || mail.Attachments.Count == 0)
            throw new InvalidOperationException(MailboxError.NoAttachment);

        var attachments = new List<MailAttachment>(mail.Attachments);

        // Step 4: Grant BEFORE writing claimed=true
        await GrantRewardsAsync(playerId, mailId, attachments, requestId);

        // Step 5: Write claimed=true under writeLock
        mail.AttachmentClaimed = true;
        mail.IsRead = true;

        try
        {
            await CloudSaveHelper.SetPlayerDataAsync(
                _gameApiClient, _context, playerId, MailboxConstants.KeyUserItems, mailbox, writeLock);
        }
        catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex))
        {
            // §5.4 step 9: at-most-once tradeoff
            _logger.LogWarning(
                "ClaimAttachment (user): write conflict after grant for mailId={MailId} — returning AlreadyClaimed",
                mailId);
            return new ClaimAttachmentResponse { Success = true, MailId = mailId, AlreadyClaimed = true };
        }

        _logger.LogInformation("User attachment claimed for mailId={MailId} by {PlayerId}", mailId, playerId);
        return new ClaimAttachmentResponse
        {
            Success            = true,
            MailId             = mailId,
            AlreadyClaimed     = false,
            GrantedAttachments = attachments
        };
    }

    private async Task GrantRewardsAsync(
        string playerId, string mailId,
        IReadOnlyList<MailAttachment> attachments,
        string? requestId)
    {
        var idempotencyKey = string.IsNullOrEmpty(requestId)
            ? $"claim-{playerId}-{mailId}"
            : $"claim-{requestId}";

        try
        {
            await RewardGrant.GrantRewardsAsync(_gameApiClient, _context, playerId, attachments, idempotencyKey, _logger);
        }
        catch (RetryableGrantException ex)
        {
            _logger.LogWarning(ex, "ClaimAttachment: transient grant failure for mailId={MailId} — returning GrantUnavailable", mailId);
            throw new InvalidOperationException(MailboxError.GrantUnavailable);
        }
    }
}
