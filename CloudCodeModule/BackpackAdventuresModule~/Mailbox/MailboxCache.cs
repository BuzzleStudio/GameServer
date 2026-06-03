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
    // For versioned keys (mails_all) this is a defense-in-depth backstop only — a missed version
    // bump still self-heals within the TTL; correctness comes from the version check.
    internal const int CacheTtlSeconds = 5;

    // Sentinel for entries written without a version (plain, non-versioned keys).
    private const long Unversioned = -1;

    // Set to false in unit tests to isolate REST behaviour without cache interference.
    internal static bool Enabled = true;

    private sealed record CacheEntry(string Json, DateTime ExpiresUtc, long Version);

    private static readonly ConcurrentDictionary<string, CacheEntry> _store = new();

    // Key format: {projectId}:{scope}:{qualifier}:{dataKey}
    //   player data  → "proj:player:playerId:mailbox_user_items"
    //   custom data  → "proj:custom:global_mail:mails_all"

    // Keys that must NEVER be cached:
    //   player_wallet          — stale balances must never be served (money path).
    //   global_mail_change_log — the version oracle; caching it defeats version-aware invalidation.
    private static bool IsNoCacheKey(string cacheKey) =>
        cacheKey.Contains(MailboxConstants.KeyPlayerWallet) ||
        cacheKey.Contains(MailboxConstants.KeyGlobalMailChangeLog);

    // Keys cached with a version stamp instead of TTL-only. Must use the *Versioned APIs.
    private static bool IsVersionedKey(string cacheKey) =>
        cacheKey.Contains(MailboxConstants.KeyMailsAll);

    // ── Plain (TTL-only) cache — player data + non-versioned custom data ──────────

    // Returns true on a live hit; false on miss or expired (expired entry is evicted).
    internal static bool TryGet<T>(string cacheKey, out T? value)
    {
        value = default;
        if (!Enabled || IsNoCacheKey(cacheKey) || IsVersionedKey(cacheKey)) return false;
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

    // Writes or refreshes a non-versioned cache entry with a fresh TTL.
    internal static void Set<T>(string cacheKey, T value)
    {
        if (!Enabled || IsNoCacheKey(cacheKey) || IsVersionedKey(cacheKey)) return;
        _store[cacheKey] = new CacheEntry(
            JsonSerializer.Serialize(value),
            DateTime.UtcNow.AddSeconds(CacheTtlSeconds),
            Unversioned);
    }

    // ── Versioned cache — mails_all, validated against global_mail_change_log.Version ──

    // Hit only when the entry exists, is within TTL, AND its stored version == currentVersion.
    // A version mismatch (another instance bumped the change log) evicts and reports a miss.
    internal static bool TryGetVersioned<T>(string cacheKey, long currentVersion, out T? value)
    {
        value = default;
        if (!Enabled) return false;
        if (!_store.TryGetValue(cacheKey, out var entry)) return false;
        if (DateTime.UtcNow >= entry.ExpiresUtc || entry.Version != currentVersion)
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

    // Stores a versioned entry stamped with the version that was current when the data was fetched.
    internal static void SetVersioned<T>(string cacheKey, T value, long version)
    {
        if (!Enabled) return;
        _store[cacheKey] = new CacheEntry(
            JsonSerializer.Serialize(value),
            DateTime.UtcNow.AddSeconds(CacheTtlSeconds),
            version);
    }

    // Removes one entry. Called on 409 write-lock conflicts so the next read fetches fresh data.
    internal static void Evict(string cacheKey)
    {
        _store.TryRemove(cacheKey, out _);
    }
}
