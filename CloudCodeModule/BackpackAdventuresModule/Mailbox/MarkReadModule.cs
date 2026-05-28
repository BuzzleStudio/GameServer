using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

/// <summary>
/// Mark a single mail as read (with writeLock + idempotency) or mark all mails as read.
/// MarkMailRead: writeLock on mailbox_global_state or mailbox_user_items; retry once on 409.
/// MarkAllRead: writeLock on mailbox_meta; retry once on 409.
/// </summary>
public class MarkReadModule
{
    private readonly IExecutionContext _context;
    private readonly IGameApiClient _gameApiClient;
    private readonly ILogger<MarkReadModule> _logger;

    public MarkReadModule(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        ILogger<MarkReadModule> logger)
    {
        _context = context;
        _gameApiClient = gameApiClient;
        _logger = logger;
    }

    [CloudCodeFunction("MarkMailRead")]
    public async Task<MarkMailReadResponse> MarkMailReadAsync(MarkMailReadRequest request)
    {
        var playerId = _context.PlayerId ?? string.Empty;
        _logger.LogInformation("MarkMailRead called for {PlayerId}, mailId={MailId}, type={MailType}",
            playerId, request.MailId, request.MailType);

        if (string.IsNullOrWhiteSpace(request.MailId))
            throw new ArgumentException(MailboxError.InvalidInput);

        // Idempotency check
        if (!string.IsNullOrEmpty(request.RequestId))
        {
            var cached = await IdempotencyService.TryGetCachedResponseAsync(
                _gameApiClient, _context, playerId, request.RequestId, "MarkMailRead", request.MailId);
            if (cached != null)
            {
                _logger.LogInformation("MarkMailRead: idempotent replay for requestId={RequestId}", request.RequestId);
                return new MarkMailReadResponse { Success = true, MailId = request.MailId, IsRead = true };
            }
        }

        var isGlobal = string.Equals(request.MailType, "global", StringComparison.OrdinalIgnoreCase);

        if (isGlobal)
            await MarkGlobalReadAsync(playerId, request.MailId);
        else
            await MarkUserReadAsync(playerId, request.MailId);

        // Store idempotency entry (best-effort)
        if (!string.IsNullOrEmpty(request.RequestId))
        {
            try
            {
                await IdempotencyService.StoreResponseAsync(
                    _gameApiClient, _context, playerId, request.RequestId, "MarkMailRead", request.MailId,
                    new { success = true, isRead = true });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MarkMailRead: failed to store idempotency entry (non-fatal)");
            }
        }

        _logger.LogInformation("MarkMailRead success for mailId={MailId}", request.MailId);
        return new MarkMailReadResponse { Success = true, MailId = request.MailId, IsRead = true };
    }

    [CloudCodeFunction("MarkAllRead")]
    public async Task<MarkAllReadResponse> MarkAllReadAsync()
    {
        var playerId = _context.PlayerId ?? string.Empty;
        _logger.LogInformation("MarkAllRead called for {PlayerId}", playerId);

        var now = DateTime.UtcNow.ToString("o");

        // Mark all user mails read (writeLock, retry once)
        await MarkAllUserMailsReadWithRetryAsync(playerId);

        // Update meta.lastReadAt (writeLock on meta, retry once)
        await UpdateMetaLastReadAtWithRetryAsync(playerId, now);

        _logger.LogInformation("MarkAllRead complete for {PlayerId}", playerId);
        return new MarkAllReadResponse { Success = true, LastReadAt = now };
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task MarkGlobalReadAsync(string playerId, string mailId)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var (state, writeLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerGlobalMailState>(
                _gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState);
            state ??= new PlayerGlobalMailState();

            if (state.ReadIds.Contains(mailId)) return; // already read, idempotent

            // Prune dead IDs: remove any ID not present in current global index refs
            await PruneDeadGlobalStateIdsAsync(state);

            state.ReadIds.Add(mailId);

            try
            {
                await CloudSaveHelper.SetPlayerDataAsync(
                    _gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState, state, writeLock);
                return;
            }
            catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex) && attempt == 0)
            {
                _logger.LogWarning("MarkMailRead (global): write conflict — retrying");
                // loop
            }
        }
        // Second conflict: read is idempotent, succeed silently
        _logger.LogWarning("MarkMailRead (global): second write conflict — succeeding silently (read is idempotent)");
    }

    private async Task MarkUserReadAsync(string playerId, string mailId)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var (mailbox, writeLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerUserMailbox>(
                _gameApiClient, _context, playerId, MailboxConstants.KeyUserItems);
            mailbox ??= new PlayerUserMailbox();

            var mail = mailbox.Mails.Find(m => m.MailId == mailId);
            if (mail == null) throw new InvalidOperationException(MailboxError.MailNotFound);
            if (mail.IsRead) return; // already read

            mail.IsRead = true;

            try
            {
                await CloudSaveHelper.SetPlayerDataAsync(
                    _gameApiClient, _context, playerId, MailboxConstants.KeyUserItems, mailbox, writeLock);
                return;
            }
            catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex) && attempt == 0)
            {
                _logger.LogWarning("MarkMailRead (user): write conflict — retrying");
                // loop
            }
        }
        _logger.LogWarning("MarkMailRead (user): second write conflict — succeeding silently");
    }

    private async Task MarkAllUserMailsReadWithRetryAsync(string playerId)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var (mailbox, writeLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerUserMailbox>(
                _gameApiClient, _context, playerId, MailboxConstants.KeyUserItems);
            if (mailbox == null) return;

            foreach (var m in mailbox.Mails) m.IsRead = true;

            try
            {
                await CloudSaveHelper.SetPlayerDataAsync(
                    _gameApiClient, _context, playerId, MailboxConstants.KeyUserItems, mailbox, writeLock);
                return;
            }
            catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex) && attempt == 0)
            {
                _logger.LogWarning("MarkAllRead (user mails): write conflict — retrying");
            }
        }
        _logger.LogWarning("MarkAllRead (user mails): second write conflict — succeeding silently");
    }

    private async Task UpdateMetaLastReadAtWithRetryAsync(string playerId, string lastReadAt)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var (meta, writeLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerMailboxMeta>(
                _gameApiClient, _context, playerId, MailboxConstants.KeyMeta);
            meta ??= new PlayerMailboxMeta();
            meta.LastReadAt = lastReadAt;

            try
            {
                await CloudSaveHelper.SetPlayerDataAsync(
                    _gameApiClient, _context, playerId, MailboxConstants.KeyMeta, meta, writeLock);
                return;
            }
            catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex) && attempt == 0)
            {
                _logger.LogWarning("MarkAllRead (meta): write conflict — retrying");
            }
        }
        _logger.LogWarning("MarkAllRead (meta): second write conflict — succeeding silently");
    }

    private async Task PruneDeadGlobalStateIdsAsync(PlayerGlobalMailState state)
    {
        // Remove any claimed/read IDs not present in the current v2 index (dead pruning per §5.1)
        var v2Index = await CloudSaveHelper.GetCustomDataAsync<GlobalMailIndexV2>(
            _gameApiClient, _context, MailboxConstants.KeyGlobalMailIndexV2);
        if (v2Index == null) return;

        var liveIds = new System.Collections.Generic.HashSet<string>();
        foreach (var r in v2Index.Refs) liveIds.Add(r.MailId);

        state.ClaimedIds.RemoveAll(id => !liveIds.Contains(id));
        state.ReadIds.RemoveAll(id => !liveIds.Contains(id));
    }
}
