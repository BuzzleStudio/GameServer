// Requires cc-cache merge AND InternalsVisibleTo="BackpackAdventuresModule.Tests" in the module csproj.
// BackpackAdventuresModule.csproj now includes that AssemblyAttribute — remove the
// <Compile Remove> in the test csproj after confirming cc-cache build is green.

using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace BackpackAdventures.CloudCode.Tests;

/// <summary>
/// Unit tests for MailboxCache (internal static class).
///
/// MailboxCache is process-wide static state. Each test uses a unique key prefix
/// (GUID) so no test pollutes another's keys. The Enabled flag is restored after
/// each test via IDisposable.
/// </summary>
public class MailboxCacheTests : IDisposable
{
    // Each test gets its own namespace so static state doesn't bleed between tests
    private readonly string _ns = Guid.NewGuid().ToString("N");

    private string Key(string name) => $"test-proj:custom:global_mail:{_ns}_{name}";

    public void Dispose()
    {
        MailboxCache.Enabled = true; // restore default after any test that disables it
    }

    // ── Hit / Miss ────────────────────────────────────────────────────────────

    [Fact]
    public void Miss_KeyNotSet_ReturnsFalse()
    {
        var hit = MailboxCache.TryGet<GlobalMailCollection>(Key("mails_all"), out var value);

        Assert.False(hit);
        Assert.Null(value);
    }

    [Fact]
    public void Hit_AfterSet_ReturnsDeserializedValue()
    {
        // Plain (non-versioned) cache mechanics — use a neutral key. mails_all is version-aware
        // and intentionally bypasses plain Set/TryGet.
        var key = Key("hit_after_set");
        var collection = new GlobalMailCollection();
        collection.Mails.Add(new GlobalMailPayload { Mail = new Mail { MessageId = "gm_001" } });

        MailboxCache.Set(key, collection);
        var hit = MailboxCache.TryGet<GlobalMailCollection>(key, out var value);

        Assert.True(hit);
        Assert.NotNull(value);
        Assert.Single(value!.Mails);
        Assert.Equal("gm_001", value.Mails[0].Mail.MessageId);
    }

    [Fact]
    public void Miss_DifferentKey_ReturnsFalse()
    {
        MailboxCache.Set(Key("key_a"), new GlobalMailCollection());

        var hit = MailboxCache.TryGet<GlobalMailCollection>(Key("key_b"), out _);

        Assert.False(hit);
    }

    // ── Write-through: Set updates value ─────────────────────────────────────

    [Fact]
    public void Set_ReplacesExistingValue()
    {
        var key = Key("replace_test");
        var first  = new GlobalMailCollection();
        first.Mails.Add(new GlobalMailPayload { Mail = new Mail { MessageId = "gm_first" } });

        var second = new GlobalMailCollection();
        second.Mails.Add(new GlobalMailPayload { Mail = new Mail { MessageId = "gm_second" } });

        MailboxCache.Set(key, first);
        MailboxCache.Set(key, second); // write-through replaces

        var hit = MailboxCache.TryGet<GlobalMailCollection>(key, out var value);

        Assert.True(hit);
        Assert.NotNull(value);
        Assert.Equal("gm_second", value!.Mails[0].Mail.MessageId);
    }

    // ── TTL expiry (uses reflection to insert a pre-expired entry) ────────────

    [Fact]
    public void TTL_WithinWindow_ReturnsHit()
    {
        var key = Key("ttl_live");
        MailboxCache.Set(key, new GlobalMailCollection());

        var hit = MailboxCache.TryGet<GlobalMailCollection>(key, out _);

        Assert.True(hit, "Entry set just now should still be within TTL window");
    }

    [Fact]
    public void TTL_ExpiredEntry_ReturnsMiss()
    {
        var key = Key("ttl_expired");
        InsertExpiredEntry(key, new GlobalMailCollection());

        var hit = MailboxCache.TryGet<GlobalMailCollection>(key, out _);

        Assert.False(hit, "Expired entry must be evicted on TryGet");
    }

    [Fact]
    public void TTL_Default_Is5Seconds()
    {
        Assert.Equal(5, MailboxCache.CacheTtlSeconds);
    }

    // ── Evict ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Evict_KnownKey_RemovesEntry()
    {
        var key = Key("evict_test");
        MailboxCache.Set(key, new GlobalMailCollection());
        MailboxCache.Evict(key);

        var hit = MailboxCache.TryGet<GlobalMailCollection>(key, out _);

        Assert.False(hit, "TryGet after Evict must be a miss");
    }

    [Fact]
    public void Evict_UnknownKey_DoesNotThrow()
    {
        var ex = Record.Exception(() => MailboxCache.Evict(Key("nonexistent")));
        Assert.Null(ex);
    }

    [Fact]
    public void Evict_409ConflictPattern_ClearsEntry_SoNextReadIsFresh()
    {
        // Mirrors the "On writeLock conflict (409): evict the key" invariant.
        // After eviction the next TryGet must be a miss so CloudSaveHelper goes to REST.
        var key = Key("conflict_evict");
        MailboxCache.Set(key, new PlayerGlobalMailState());

        MailboxCache.Evict(key); // simulate 409 eviction

        var hit = MailboxCache.TryGet<PlayerGlobalMailState>(key, out _);
        Assert.False(hit, "After 409 eviction, TryGet must miss so the next read goes to Cloud Save.");
    }

    // ── Enabled flag ──────────────────────────────────────────────────────────

    [Fact]
    public void Disabled_TryGet_AlwaysReturnsFalse()
    {
        var key = Key("disabled_get");
        MailboxCache.Set(key, new GlobalMailCollection()); // set while enabled

        MailboxCache.Enabled = false;

        var hit = MailboxCache.TryGet<GlobalMailCollection>(key, out _);
        Assert.False(hit, "When Enabled=false, TryGet must always miss to force REST reads");
    }

    [Fact]
    public void Disabled_Set_DoesNotPopulateStore()
    {
        var key = Key("disabled_set");
        MailboxCache.Enabled = false;
        MailboxCache.Set(key, new GlobalMailCollection());
        MailboxCache.Enabled = true;

        var hit = MailboxCache.TryGet<GlobalMailCollection>(key, out _);
        Assert.False(hit, "Set while Enabled=false must not write to the store");
    }

    // ── Lock-read bypass contract ─────────────────────────────────────────────

    [Fact]
    public void LockReadBypass_CacheStoresDataOnly_NoWriteLock()
    {
        // GetPlayerDataWithLockAsync ALWAYS goes to REST (writeLock must be fresh).
        // The cache may store the DATA portion as a side-effect, but never the lock token.
        // This test verifies the type contract: MailboxCache stores plain T values.

        var key = Key("lock_bypass");
        var state = new PlayerGlobalMailState();
        MailboxCache.Set(key, state);

        var hit = MailboxCache.TryGet<PlayerGlobalMailState>(key, out var cached);

        Assert.True(hit);
        Assert.NotNull(cached);
        // Plain PlayerGlobalMailState returned — no lock token property exists on it
        Assert.IsType<PlayerGlobalMailState>(cached);
    }

    [Fact]
    public void WalletKey_BypassesCache_AlwaysReturnsMiss()
    {
        // Wallet keys must never be served stale. MailboxCache.IsNoCacheKey() skips them.
        var walletKey = $"test-proj:player:player1:{MailboxConstants.KeyPlayerWallet}";

        MailboxCache.Set(walletKey, new object()); // attempt to set
        var hit = MailboxCache.TryGet<object>(walletKey, out _);

        Assert.False(hit, "Wallet key must always bypass cache (IsNoCacheKey guard)");
    }

    // ── Multiple types coexist ────────────────────────────────────────────────

    [Fact]
    public void Set_DifferentTypes_Coexist()
    {
        // Neutral key for the collection — mails_all is version-aware and bypasses plain Set/TryGet.
        var kCol   = Key("coexist_collection");
        var kState = Key(MailboxConstants.KeyGlobalState);
        var kMb    = Key(MailboxConstants.KeyUserItems);

        MailboxCache.Set(kCol,   new GlobalMailCollection());
        MailboxCache.Set(kState, new PlayerGlobalMailState());
        MailboxCache.Set(kMb,    new PlayerUserMailbox());

        Assert.True(MailboxCache.TryGet<GlobalMailCollection>(kCol,   out _));
        Assert.True(MailboxCache.TryGet<PlayerGlobalMailState>(kState, out _));
        Assert.True(MailboxCache.TryGet<PlayerUserMailbox>(kMb,       out _));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Inserts an already-expired CacheEntry into the static _store via reflection.
    /// Uses the dictionary's own indexer (no readonly-field mutation — safe on .NET 9).
    /// </summary>
    private static void InsertExpiredEntry<T>(string cacheKey, T value)
    {
        var storeField = typeof(MailboxCache)
            .GetField("_store", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("MailboxCache._store not found");

        var entryType = typeof(MailboxCache)
            .GetNestedType("CacheEntry", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("MailboxCache.CacheEntry not found");

        var json = JsonSerializer.Serialize(value);
        var expiredTime = DateTime.UtcNow.AddSeconds(-1); // already expired

        // CacheEntry is (string Json, DateTime ExpiresUtc, long Version); -1 = unversioned.
        var entry = Activator.CreateInstance(entryType, json, expiredTime, -1L)
            ?? throw new InvalidOperationException("Could not construct CacheEntry");

        // _store is readonly but we DON'T reassign it — we call the dictionary's indexer.
        // GetValue reads the reference; the indexer mutates the existing dictionary object.
        var store = storeField.GetValue(null)
            ?? throw new InvalidOperationException("MailboxCache._store is null");

        var setItem = store.GetType().GetProperty("Item")?.SetMethod
            ?? throw new InvalidOperationException("ConcurrentDictionary indexer set not found");

        setItem.Invoke(store, new[] { cacheKey, entry });
    }
}
