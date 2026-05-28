using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;


namespace BackpackAdventures.CloudCode;

public class ClaimAttachmentModule
{
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
    public async Task<ClaimAttachmentResponse> ClaimAttachmentAsync(ClaimAttachmentRequest request)
    {
        var playerId = _context.PlayerId ?? string.Empty;
        _logger.LogInformation("ClaimAttachment called for {PlayerId}, mailId={MailId}", playerId, request.MailId);
        try
        {
            if (string.IsNullOrWhiteSpace(request.MailId))
                throw new ArgumentException(MailboxError.InvalidInput);

            // Auto-detect mail type: check global index first, then user mailbox.
            var globalIndex = await CloudSaveHelper.GetCustomDataAsync<GlobalMailIndex>(
                _gameApiClient, _context, MailboxConstants.KeyGlobalMailIndex) ?? new GlobalMailIndex();

            var globalMail = globalIndex.Mails.Find(m => m.GlobalMailId == request.MailId);
            if (globalMail != null)
                return await ClaimGlobalAttachmentAsync(playerId, globalMail);

            var mailbox = await CloudSaveHelper.GetPlayerDataAsync<PlayerUserMailbox>(
                _gameApiClient, _context, playerId, MailboxConstants.KeyUserItems)
                ?? new PlayerUserMailbox();

            var userMail = mailbox.Mails.Find(m => m.MailId == request.MailId);
            if (userMail != null)
                return await ClaimUserAttachmentAsync(playerId, userMail, mailbox);

            throw new InvalidOperationException(MailboxError.MailNotFound);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClaimAttachment failed for {PlayerId}, mailId={MailId}", playerId, request.MailId);
            throw;
        }
    }

    private async Task<ClaimAttachmentResponse> ClaimGlobalAttachmentAsync(string playerId, GlobalMailItem mail)
    {
        // Read with writeLock for optimistic concurrency — prevents double-grant if two
        // requests race past the claimed check before either write completes.
        var (state, writeLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerGlobalMailState>(
            _gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState);
        state ??= new PlayerGlobalMailState();

        if (state.ClaimedIds.Contains(mail.GlobalMailId))
            return new ClaimAttachmentResponse { Success = true, MailId = mail.GlobalMailId, AlreadyClaimed = true };

        if (mail.IsExpired())
            throw new InvalidOperationException(MailboxError.MailExpired);

        if (mail.Attachments == null || mail.Attachments.Count == 0)
            throw new InvalidOperationException(MailboxError.NoAttachment);

        state.ClaimedIds.Add(mail.GlobalMailId);
        if (!state.ReadIds.Contains(mail.GlobalMailId))
            state.ReadIds.Add(mail.GlobalMailId);

        try
        {
            await CloudSaveHelper.SetPlayerDataAsync(
                _gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState, state, writeLock);
        }
        catch (Exception ex) when (IsWriteLockConflict(ex))
        {
            // A concurrent claim won the race and already updated the state.
            _logger.LogWarning("Write conflict on global claim for mailId={MailId} by {PlayerId}", mail.GlobalMailId, playerId);
            return new ClaimAttachmentResponse { Success = true, MailId = mail.GlobalMailId, AlreadyClaimed = true };
        }

        _logger.LogInformation("Global attachment claimed for mailId={MailId} by {PlayerId}", mail.GlobalMailId, playerId);
        return new ClaimAttachmentResponse
        {
            Success = true,
            MailId = mail.GlobalMailId,
            AlreadyClaimed = false,
            GrantedAttachments = mail.Attachments
        };
    }

    private async Task<ClaimAttachmentResponse> ClaimUserAttachmentAsync(
        string playerId, UserMailItem mail, PlayerUserMailbox mailbox)
    {
        if (mail.AttachmentClaimed)
            return new ClaimAttachmentResponse { Success = true, MailId = mail.MailId, AlreadyClaimed = true };

        if (mail.IsExpired())
            throw new InvalidOperationException(MailboxError.MailExpired);

        if (mail.Attachments == null || mail.Attachments.Count == 0)
            throw new InvalidOperationException(MailboxError.NoAttachment);

        // Re-read with writeLock for optimistic concurrency on user mailbox.
        var (freshMailbox, writeLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerUserMailbox>(
            _gameApiClient, _context, playerId, MailboxConstants.KeyUserItems);
        freshMailbox ??= new PlayerUserMailbox();

        var freshMail = freshMailbox.Mails.Find(m => m.MailId == mail.MailId);
        if (freshMail == null)
            throw new InvalidOperationException(MailboxError.MailNotFound);

        if (freshMail.AttachmentClaimed)
            return new ClaimAttachmentResponse { Success = true, MailId = mail.MailId, AlreadyClaimed = true };

        var claimed = new List<MailAttachment>(freshMail.Attachments ?? mail.Attachments);
        freshMail.AttachmentClaimed = true;
        freshMail.IsRead = true;

        try
        {
            await CloudSaveHelper.SetPlayerDataAsync(
                _gameApiClient, _context, playerId, MailboxConstants.KeyUserItems, freshMailbox, writeLock);
        }
        catch (Exception ex) when (IsWriteLockConflict(ex))
        {
            _logger.LogWarning("Write conflict on user claim for mailId={MailId} by {PlayerId}", mail.MailId, playerId);
            return new ClaimAttachmentResponse { Success = true, MailId = mail.MailId, AlreadyClaimed = true };
        }

        _logger.LogInformation("User attachment claimed for mailId={MailId} by {PlayerId}", mail.MailId, playerId);
        return new ClaimAttachmentResponse
        {
            Success = true,
            MailId = mail.MailId,
            AlreadyClaimed = false,
            GrantedAttachments = claimed
        };
    }

    private static bool IsWriteLockConflict(Exception ex) =>
        ex.Message.Contains("409") ||
        ex.Message.Contains("Conflict", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("WriteLock", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("write_lock", StringComparison.OrdinalIgnoreCase);
}
