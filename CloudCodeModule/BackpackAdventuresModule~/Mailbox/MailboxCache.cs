// Best-effort process-wide RAM cache for Cloud Save round-trip reduction.
// Warm Cloud Code instances share this store across invocations; cold starts begin empty.
// Correctness is still guarded by Cloud Save writeLock tokens — the cache is acceleration only.

using System;
using System.Collections.Concurrent;
using System.Text.Json;

namespace BackpackAdventures.CloudCode;

internal static class MailboxCache
{
    // Short TTL collapses repeated reads within one invocation while limiting stale-data window.
    internal const int CacheTtlSeconds = 5;

    // Set to false in unit tests to isolate REST behaviour without cache interference.
    internal static bool Enabled = true;

    private sealed record CacheEntry(string Json, DateTime ExpiresUtc);

    private static readonly ConcurrentDictionary<string, CacheEntry> _store = new();

    // Key format: {projectId}:{scope}:{qualifier}:{dataKey}
    //   player data  → "proj:player:playerId:mailbox_user_items"
    //   custom data  → "proj:custom:global_mail:mails_all"

    // Wallet keys bypass the cache entirely — stale balances must never be served.
    private static bool IsNoCacheKey(string cacheKey) =>
        cacheKey.Contains(MailboxConstants.KeyPlayerWallet);

    // Returns true on a live hit; false on miss or expired (expired entry is evicted).
    internal static bool TryGet<T>(string cacheKey, out T? value)
    {
        value = default;
        if (!Enabled || IsNoCacheKey(cacheKey)) return false;
        if (!_store.TryGetValue(cacheKey, out var entry)) return false;
        if (DateTime.UtcNow >= entry.ExpiresUtc)
        {
            _store.TryRemove(cacheKey, out _);
            return false;
        }
        try
        {
            value = JsonSerializer.Deserialize<T>(entry.Json);
            return true;
        }
        catch
        {
            _store.TryRemove(cacheKey, out _);
            return false;
        }
    }

    // Writes or refreshes a cache entry with a fresh TTL.
    internal static void Set<T>(string cacheKey, T value)
    {
        if (!Enabled || IsNoCacheKey(cacheKey)) return;
        _store[cacheKey] = new CacheEntry(
            JsonSerializer.Serialize(value),
            DateTime.UtcNow.AddSeconds(CacheTtlSeconds));
    }

    // Removes one entry. Called on 409 write-lock conflicts so the next read fetches fresh data.
    internal static void Evict(string cacheKey)
    {
        _store.TryRemove(cacheKey, out _);
    }
}
