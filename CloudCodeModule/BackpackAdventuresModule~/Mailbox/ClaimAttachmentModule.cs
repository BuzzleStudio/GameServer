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
        if (string.IsNullOrWhiteSpace(request.MailId))
            throw new ArgumentException(MailboxError.InvalidInput);

        var requestId = string.IsNullOrEmpty(request.RequestId) ? null : request.RequestId;
        if (requestId != null)
        {
            var cached = await IdempotencyService.TryGetCachedResponseAsync(_gameApiClient, _context, playerId, requestId, "ClaimAttachment", request.MailId);
            if (cached != null)
                return new ClaimAttachmentResponse { MailId = request.MailId, AlreadyClaimed = false };
        }

        ClaimAttachmentResponse result = string.Equals(request.MailType, "global", StringComparison.OrdinalIgnoreCase)
            ? await ClaimGlobalAttachmentAsync(playerId, request.MailId, requestId)
            : await ClaimUserAttachmentAsync(playerId, request.MailId, requestId);

        if (requestId != null && !result.AlreadyClaimed)
            await IdempotencyService.StoreResponseAsync(_gameApiClient, _context, playerId, requestId, "ClaimAttachment", request.MailId, new { alreadyClaimed = false });

        return result;
    }

    private async Task<ClaimAttachmentResponse> ClaimGlobalAttachmentAsync(string playerId, string mailId, string? requestId)
    {
        var payload = await CloudSaveHelper.GetCustomDataAsync<GlobalMailPayload>(_gameApiClient, _context, string.Format(MailboxConstants.KeyGlobalMailPayloadFmt, mailId));
        if (payload?.Mail == null)
        {
            var v1Index = await CloudSaveHelper.GetCustomDataAsync<GlobalMailIndex>(_gameApiClient, _context, MailboxConstants.KeyGlobalMailIndex);
            var v1Mail = v1Index?.Mails.Find(m => m.GlobalMailId == mailId);
            if (v1Mail == null) throw new InvalidOperationException(MailboxError.MailNotFound);
            return await ClaimLegacyGlobalAttachmentAsync(playerId, v1Mail, requestId);
        }

        var (state, writeLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerGlobalMailState>(_gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState);
        state ??= new PlayerGlobalMailState();
        state.DeletedIds ??= new List<string>();
        state.ClaimedIds ??= new List<string>();
        state.ReadIds ??= new List<string>();
        if (state.DeletedIds.Contains(mailId))
            throw new InvalidOperationException(MailboxError.MailNotFound);
        if (state.ClaimedIds.Contains(mailId))
            return new ClaimAttachmentResponse { MailId = mailId, AlreadyClaimed = true };
        if (payload.Mail.IsExpired())
            throw new InvalidOperationException(MailboxError.MailExpired);
        if (!MailSchemaHelper.HasAttachments(payload.Mail))
            throw new InvalidOperationException(MailboxError.NoAttachment);

        var attachments = MailSchemaHelper.ToAttachments(payload.Mail.MailInfo.Attachment);
        await GrantRewardsAsync(playerId, mailId, attachments ?? new List<MailAttachment>(), requestId);
        state.ClaimedIds.Add(mailId);
        if (!state.ReadIds.Contains(mailId)) state.ReadIds.Add(mailId);

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
        state.DeletedIds ??= new List<string>();
        state.ClaimedIds ??= new List<string>();
        state.ReadIds ??= new List<string>();
        if (state.DeletedIds.Contains(mail.GlobalMailId))
            throw new InvalidOperationException(MailboxError.MailNotFound);
        if (state.ClaimedIds.Contains(mail.GlobalMailId))
            return new ClaimAttachmentResponse { MailId = mail.GlobalMailId, AlreadyClaimed = true };
        if (mail.IsExpired())
            throw new InvalidOperationException(MailboxError.MailExpired);
        if (mail.Attachments == null || mail.Attachments.Count == 0)
            throw new InvalidOperationException(MailboxError.NoAttachment);

        await GrantRewardsAsync(playerId, mail.GlobalMailId, mail.Attachments, requestId);
        state.ClaimedIds.Add(mail.GlobalMailId);
        if (!state.ReadIds.Contains(mail.GlobalMailId)) state.ReadIds.Add(mail.GlobalMailId);
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
}
