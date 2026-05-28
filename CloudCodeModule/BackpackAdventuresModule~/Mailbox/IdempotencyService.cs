using System;
using System.Text.Json;
using System.Threading.Tasks;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

/// <summary>
/// Manages the per-player idempotency cache (Cloud Save key: mailbox_idem_cache).
/// Read/written WITHOUT writeLock — last-write-wins is acceptable; the claim itself is protected by writeLock.
/// </summary>
internal static class IdempotencyService
{
    /// <summary>
    /// Checks whether <paramref name="requestId"/> is cached and within TTL.
    /// Returns the cached response object if found; null otherwise.
    /// </summary>
    internal static async Task<object?> TryGetCachedResponseAsync(
        IGameApiClient client, IExecutionContext ctx, string playerId,
        string requestId, string operation, string mailId)
    {
        if (string.IsNullOrEmpty(requestId)) return null;

        var cache = await CloudSaveHelper.GetPlayerDataAsync<IdemCache>(
            client, ctx, playerId, MailboxConstants.KeyIdemCache);
        if (cache == null) return null;

        var now = DateTime.UtcNow;
        var entry = cache.Entries.Find(e =>
            e.RequestId == requestId &&
            e.Operation == operation &&
            e.MailId == mailId &&
            DateTime.TryParse(e.ResolvedAt, out var resolved) &&
            (now - resolved).TotalHours <= MailboxConstants.IdemCacheTtlHours);

        return entry?.ResponseSummary;
    }

    /// <summary>
    /// Stores a resolved response in the idempotency cache (best-effort, fire-and-forget pattern supported).
    /// Prunes entries older than TTL and caps at MaxIdemCacheEntries before writing.
    /// </summary>
    internal static async Task StoreResponseAsync(
        IGameApiClient client, IExecutionContext ctx, string playerId,
        string requestId, string operation, string mailId, object responseSummary)
    {
        if (string.IsNullOrEmpty(requestId)) return;

        var cache = await CloudSaveHelper.GetPlayerDataAsync<IdemCache>(
            client, ctx, playerId, MailboxConstants.KeyIdemCache) ?? new IdemCache();

        var now = DateTime.UtcNow;

        // Prune expired entries
        cache.Entries.RemoveAll(e =>
            !DateTime.TryParse(e.ResolvedAt, out var resolved) ||
            (now - resolved).TotalHours > MailboxConstants.IdemCacheTtlHours);

        // Cap at MaxIdemCacheEntries by removing oldest
        while (cache.Entries.Count >= MailboxConstants.MaxIdemCacheEntries)
        {
            // Remove oldest by ResolvedAt
            IdemCacheEntry? oldest = null;
            foreach (var e in cache.Entries)
            {
                if (oldest == null || string.Compare(e.ResolvedAt, oldest.ResolvedAt, StringComparison.Ordinal) < 0)
                    oldest = e;
            }
            if (oldest != null) cache.Entries.Remove(oldest);
            else break;
        }

        cache.Entries.Add(new IdemCacheEntry
        {
            RequestId       = requestId,
            Operation       = operation,
            MailId          = mailId,
            ResolvedAt      = now.ToString("o"),
            ResponseSummary = responseSummary
        });

        await CloudSaveHelper.SetPlayerDataAsync(client, ctx, playerId, MailboxConstants.KeyIdemCache, cache);
    }
}
