using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

public class MarkReadModule
{
    private readonly IExecutionContext _context;
    private readonly IGameApiClient _gameApiClient;
    private readonly ILogger<MarkReadModule> _logger;

    public MarkReadModule(IExecutionContext context, IGameApiClient gameApiClient, ILogger<MarkReadModule> logger)
    {
        _context = context;
        _gameApiClient = gameApiClient;
        _logger = logger;
    }

    [CloudCodeFunction("MarkMailRead")]
    public async Task<MarkMailReadResponse> MarkMailReadAsync(MarkMailReadRequest request)
    {
        var playerId = _context.PlayerId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(request.MailId))
            throw new ArgumentException(MailboxError.InvalidInput);

        if (!string.IsNullOrEmpty(request.RequestId))
        {
            var cached = await IdempotencyService.TryGetCachedResponseAsync(_gameApiClient, _context, playerId, request.RequestId, "MarkMailRead", request.MailId);
            if (cached != null)
                return new MarkMailReadResponse { MailId = request.MailId, IsRead = true };
        }

        if (IsGlobalMail(request.MailId, request.MailType))
            await MarkGlobalReadAsync(playerId, request.MailId);
        else
            await MarkUserReadAsync(playerId, request.MailId);

        if (!string.IsNullOrEmpty(request.RequestId))
            await IdempotencyService.StoreResponseAsync(_gameApiClient, _context, playerId, request.RequestId, "MarkMailRead", request.MailId, new { isRead = true });

        return new MarkMailReadResponse { MailId = request.MailId, IsRead = true };
    }

    [CloudCodeFunction("MarkAllRead")]
    public async Task<MarkAllReadResponse> MarkAllReadAsync()
    {
        var playerId = _context.PlayerId ?? string.Empty;
        var now = DateTime.UtcNow.ToString("o");
        await MarkAllUserMailsReadWithRetryAsync(playerId);
        await UpdateMetaLastReadAtWithRetryAsync(playerId, now);
        return new MarkAllReadResponse { LastReadAt = now };
    }

    private async Task MarkGlobalReadAsync(string playerId, string mailId)
    {
        var payload = await CloudSaveHelper.GetCustomDataAsync<GlobalMailPayload>(_gameApiClient, _context, string.Format(MailboxConstants.KeyGlobalMailPayloadFmt, mailId));
        if (payload?.Mail != null && !MailSchemaHelper.IsVisibleToPlayer(payload.Mail, playerId))
            throw new InvalidOperationException(MailboxError.MailNotFound);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var (state, writeLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerGlobalMailState>(_gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState);
            state ??= new PlayerGlobalMailState();
            MailSchemaHelper.MigrateLegacyMetadata(state);
            var metadata = MailSchemaHelper.GetOrCreateMetadata(state, mailId);
            if (metadata.IsDelete) throw new InvalidOperationException(MailboxError.MailNotFound);
            if (metadata.IsRead) return;
            await PruneDeadGlobalStateIdsAsync(state);
            metadata.IsRead = true;
            try
            {
                await CloudSaveHelper.SetPlayerDataAsync(_gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState, state, writeLock);
                return;
            }
            catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex) && attempt == 0) { }
        }
    }

    private async Task MarkUserReadAsync(string playerId, string mailId)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var (mailbox, writeLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerUserMailbox>(_gameApiClient, _context, playerId, MailboxConstants.KeyUserItems);
            mailbox ??= new PlayerUserMailbox();
            var mail = mailbox.Mails.Find(m => m.MessageId == mailId);
            if (mail == null) throw new InvalidOperationException(MailboxError.MailNotFound);
            if (mail.MailMetaData.IsRead) return;
            mail.MailMetaData.IsRead = true;
            try
            {
                await CloudSaveHelper.SetPlayerDataAsync(_gameApiClient, _context, playerId, MailboxConstants.KeyUserItems, mailbox, writeLock);
                return;
            }
            catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex) && attempt == 0) { }
        }
    }

    private async Task MarkAllUserMailsReadWithRetryAsync(string playerId)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var (mailbox, writeLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerUserMailbox>(_gameApiClient, _context, playerId, MailboxConstants.KeyUserItems);
            if (mailbox == null) return;
            foreach (var mail in mailbox.Mails) mail.MailMetaData.IsRead = true;
            try
            {
                await CloudSaveHelper.SetPlayerDataAsync(_gameApiClient, _context, playerId, MailboxConstants.KeyUserItems, mailbox, writeLock);
                return;
            }
            catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex) && attempt == 0) { }
        }
    }

    private async Task UpdateMetaLastReadAtWithRetryAsync(string playerId, string lastReadAt)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var (meta, writeLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerMailboxMeta>(_gameApiClient, _context, playerId, MailboxConstants.KeyMeta);
            meta ??= new PlayerMailboxMeta();
            meta.LastReadAt = lastReadAt;
            try
            {
                await CloudSaveHelper.SetPlayerDataAsync(_gameApiClient, _context, playerId, MailboxConstants.KeyMeta, meta, writeLock);
                return;
            }
            catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex) && attempt == 0) { }
        }
    }

    private async Task PruneDeadGlobalStateIdsAsync(PlayerGlobalMailState state)
    {
        var index = await CloudSaveHelper.GetCustomDataAsync<GlobalMailIndexV2>(_gameApiClient, _context, MailboxConstants.KeyGlobalMailIndexV2);
        if (index == null) return;
        var liveIds = new HashSet<string>();
        foreach (var reference in index.Refs) liveIds.Add(reference.MessageId);
        MailSchemaHelper.MigrateLegacyMetadata(state);
        state.Mails.RemoveAll(m => !liveIds.Contains(m.MessageId));
    }

    private static bool IsGlobalMail(string mailId, string mailType)
    {
        return string.Equals(mailType, "global", StringComparison.OrdinalIgnoreCase)
               || (!string.IsNullOrEmpty(mailId) && mailId.StartsWith("gm_", StringComparison.OrdinalIgnoreCase));
    }
}
