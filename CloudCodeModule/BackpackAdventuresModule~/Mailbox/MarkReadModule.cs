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
    public async Task<ApiResponse<MarkMailReadResponse>> MarkMailReadAsync(MarkMailReadRequest request)
    {
        var playerId = _context.PlayerId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(request.MailId))
            throw new ArgumentException(MailboxError.InvalidInput);

        if (!string.IsNullOrEmpty(request.RequestId))
        {
            var cached = await IdempotencyService.TryGetCachedResponseAsync(_gameApiClient, _context, playerId, request.RequestId, "MarkMailRead", request.MailId);
            if (cached != null)
                return ApiResponse<MarkMailReadResponse>.Ok(new MarkMailReadResponse { MailId = request.MailId, IsRead = true });
        }

        if (IsGlobalMail(request.MailId, request.MailType))
            await MarkGlobalReadAsync(playerId, request.MailId);
        else
            await MarkUserReadAsync(playerId, request.MailId);

        if (!string.IsNullOrEmpty(request.RequestId))
            PendingIdemStore = StoreIdemSafeAsync(playerId, request.RequestId, "MarkMailRead", request.MailId, new { isRead = true });

        return ApiResponse<MarkMailReadResponse>.Ok(new MarkMailReadResponse { MailId = request.MailId, IsRead = true });
    }

    [CloudCodeFunction("MarkAllRead")]
    public async Task<ApiResponse<MarkAllReadResponse>> MarkAllReadAsync()
    {
        var playerId = _context.PlayerId ?? string.Empty;
        var now = DateTime.UtcNow.ToString("o");
        // Independent keys (user items vs meta) → run concurrently to cut MarkAllRead latency ~in half.
        await Task.WhenAll(
            MarkAllUserMailsReadWithRetryAsync(playerId),
            UpdateMetaLastReadAtWithRetryAsync(playerId, now));
        return ApiResponse<MarkAllReadResponse>.Ok(new MarkAllReadResponse { LastReadAt = now });
    }

    private async Task MarkGlobalReadAsync(string playerId, string mailId)
    {
        var collection = await CloudSaveHelper.GetCustomDataAsync<GlobalMailCollection>(_gameApiClient, _context, MailboxConstants.KeyMailsAll);
        var payload = GlobalMailStore.FindById(collection?.Mails, mailId);
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
            var mail = mailbox.FindById(mailId);
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

    // Best-effort idempotency store, OFF the response critical path (not awaited) — see the matching
    // note in ClaimAttachmentModule. Correctness holds via the per-mail IsRead guard on re-run.
    // Exposed via PendingIdemStore for deterministic test completion.
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

    private async Task PruneDeadGlobalStateIdsAsync(PlayerGlobalMailState state)
    {
        var collection = await CloudSaveHelper.GetCustomDataAsync<GlobalMailCollection>(_gameApiClient, _context, MailboxConstants.KeyMailsAll);
        var liveIds = GlobalMailStore.LiveIds(collection?.Mails);
        MailSchemaHelper.MigrateLegacyMetadata(state);
        state.Mails.RemoveAll(m => !liveIds.Contains(m.MessageId));
    }

    private static bool IsGlobalMail(string mailId, string mailType)
    {
        return string.Equals(mailType, "global", StringComparison.OrdinalIgnoreCase)
               || (!string.IsNullOrEmpty(mailId) && mailId.StartsWith("gm_", StringComparison.OrdinalIgnoreCase));
    }
}
