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
        var state = await CloudSaveHelper.GetPlayerDataAsync<PlayerGlobalMailState>(
            _gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState)
            ?? new PlayerGlobalMailState();

        if (state.ClaimedIds.Contains(mail.GlobalMailId))
            return new ClaimAttachmentResponse { Success = true, MailId = mail.GlobalMailId, AlreadyClaimed = true };

        if (mail.IsExpired())
            throw new InvalidOperationException(MailboxError.MailExpired);

        if (mail.Attachments == null || mail.Attachments.Count == 0)
            throw new InvalidOperationException(MailboxError.NoAttachment);

        state.ClaimedIds.Add(mail.GlobalMailId);
        if (!state.ReadIds.Contains(mail.GlobalMailId))
            state.ReadIds.Add(mail.GlobalMailId);

        await CloudSaveHelper.SetPlayerDataAsync(
            _gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState, state);

        _logger.LogInformation("Global attachment claimed for mailId={MailId} by {PlayerId}", mail.GlobalMailId, playerId);
        return new ClaimAttachmentResponse
        {
            Success = true,
            MailId = mail.GlobalMailId,
            AlreadyClaimed = false,
            ClaimedAttachments = mail.Attachments
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

        var claimed = new List<MailAttachment>(mail.Attachments);
        mail.AttachmentClaimed = true;
        mail.IsRead = true;

        await CloudSaveHelper.SetPlayerDataAsync(
            _gameApiClient, _context, playerId, MailboxConstants.KeyUserItems, mailbox);

        _logger.LogInformation("User attachment claimed for mailId={MailId} by {PlayerId}", mail.MailId, playerId);
        return new ClaimAttachmentResponse
        {
            Success = true,
            MailId = mail.MailId,
            AlreadyClaimed = false,
            ClaimedAttachments = claimed
        };
    }
}
