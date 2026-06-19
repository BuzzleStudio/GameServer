// CloudSaveHelper rewritten to use raw HTTP against the UGS Cloud Save REST API.
//
// Why: Cloud Code SDK 1.0.2-alpha's IGameApiClient is injected as null into modules
// (no registration helper exists, no setup extension exposes it). Every Cloud Save
// call through `client.CloudSaveData.*` therefore threw NullReferenceException.
// Replacing the broken SDK path with direct REST calls bypasses the DI issue while
// preserving the public method signatures so every module call site stays unchanged.
//
// Auth: prefers ctx.ServiceToken (s2s, broader perms — required for cross-player
// writes from admin endpoints + project-scoped custom data writes). Falls back to
// ctx.AccessToken (player JWT) when ServiceToken is empty.
//
// Endpoints (Unity Cloud Save v1):
//   Player data:  /v1/data/projects/{p}/players/{playerId}/items[?keys=…]
//                 /v1/data/projects/{p}/players/{playerId}/item        (POST set)
//   Custom data:  /v1/data/projects/{p}/custom/{accessClass}/items[?keys=…]
//                 /v1/data/projects/{p}/custom/{accessClass}/item      (POST set)
//                 /v1/data/projects/{p}/custom/{accessClass}/items/{key} (DELETE)

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

internal static class CloudSaveHelper
{
    // Must match the Cloud Save custom data ID configured in the UGS dashboard.
    // Stores mailbox project-wide custom data such as mails_all.
    internal const string GlobalCustomId = "global_mail";

    // Cloud Save uses a dedicated subdomain (matches the SDK's
    // Configuration default: "https://cloud-save.services.api.unity.com").
    private const string BaseHost = "https://cloud-save.services.api.unity.com";

    // One static HttpClient per module lifetime — required to avoid socket exhaustion.
    private static readonly HttpClient _http = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    // ── Cache key helpers ────────────────────────────────────────────────────

    private static string PlayerCacheKey(IExecutionContext ctx, string playerId, string key)
        => $"{ctx.ProjectId}:player:{playerId}:{key}";

    private static string CustomCacheKey(IExecutionContext ctx, string key)
        => $"{ctx.ProjectId}:custom:{GlobalCustomId}:{key}";

    // ── Player data ──────────────────────────────────────────────────────────

    // Read-through: returns cached value when live; falls through to REST on miss/expiry.
    internal static async Task<T?> GetPlayerDataAsync<T>(
        IGameApiClient _, IExecutionContext ctx, string playerId, string key)
    {
        var cacheKey = PlayerCacheKey(ctx, playerId, key);
        if (MailboxCache.TryGet<T>(cacheKey, out var cached)) return cached;

        var url = $"{BaseHost}/v1/data/projects/{ctx.ProjectId}/players/{Uri.EscapeDataString(playerId)}/items?keys={Uri.EscapeDataString(key)}";
        var (json, _, _) = await SendAsync(HttpMethod.Get, ctx, url, body: null);
        var result = ExtractFirstValue<T>(json);
        MailboxCache.Set(cacheKey, result);
        return result;
    }

    // Always REST — writeLock token must be current for safe CAS writes.
    // Write-through of data portion into the plain cache as a side-effect.
    internal static async Task<(T? data, string writeLock)> GetPlayerDataWithLockAsync<T>(
        IGameApiClient _, IExecutionContext ctx, string playerId, string key)
    {
        var url = $"{BaseHost}/v1/data/projects/{ctx.ProjectId}/players/{Uri.EscapeDataString(playerId)}/items?keys={Uri.EscapeDataString(key)}";
        var (json, _, _) = await SendAsync(HttpMethod.Get, ctx, url, body: null);
        var result = ExtractFirstWithLock<T>(json);
        MailboxCache.Set(PlayerCacheKey(ctx, playerId, key), result.data);
        return result;
    }

    // Write-through: cache is updated with the new value on success.
    internal static async Task SetPlayerDataAsync<T>(
        IGameApiClient _, IExecutionContext ctx, string playerId, string key, T value,
        string? writeLock = null)
    {
        var cacheKey = PlayerCacheKey(ctx, playerId, key);
        var url = $"{BaseHost}/v1/data/projects/{ctx.ProjectId}/players/{Uri.EscapeDataString(playerId)}/items";
        await PostItemAsync(ctx, url, key, value, writeLock, cacheKey);
        MailboxCache.Set(cacheKey, value);
    }

    // ── Custom data (project-wide) ───────────────────────────────────────────

    // Read-through. The project-wide mails_all key is VERSION-AWARE (validated against
    // global_mail_change_log) instead of TTL-only, so a write on another worker instance
    // invalidates this instance's cache on the next read. All other keys are TTL read-through.
    internal static async Task<T?> GetCustomDataAsync<T>(
        IGameApiClient client, IExecutionContext ctx, string key)
    {
        var cacheKey = CustomCacheKey(ctx, key);

        if (key == MailboxConstants.KeyMailsAll)
            return await GetCustomDataWithVersionAwareCacheAsync<T>(client, ctx, key, cacheKey);

        if (MailboxCache.TryGet<T>(cacheKey, out var cached)) return cached;
        var result = await FetchCustomAsync<T>(ctx, key);
        MailboxCache.Set(cacheKey, result);
        return result;
    }

    // Write-through. mails_all writes bump the global change log so other instances invalidate.
    internal static async Task SetCustomDataAsync<T>(
        IGameApiClient client, IExecutionContext ctx, string key, T value, ILogger? logger = null)
    {
        var cacheKey = CustomCacheKey(ctx, key);
        var url = $"{BaseHost}/v1/data/projects/{ctx.ProjectId}/custom/{GlobalCustomId}/items";
        await PostItemAsync(ctx, url, key, value, null, cacheKey);
        await OnCustomWriteSucceededAsync(client, ctx, key, value, cacheKey, logger);
    }

    // Always REST — writeLock token must be current for safe CAS writes.
    // Write-through of data portion into the plain cache as a side-effect.
    internal static async Task<(T? data, string writeLock)> GetCustomDataWithLockAsync<T>(
        IGameApiClient _, IExecutionContext ctx, string key)
    {
        var url = $"{BaseHost}/v1/data/projects/{ctx.ProjectId}/custom/{GlobalCustomId}/items?keys={Uri.EscapeDataString(key)}";
        var (json, _, _) = await SendAsync(HttpMethod.Get, ctx, url, body: null);
        var result = ExtractFirstWithLock<T>(json);
        MailboxCache.Set(CustomCacheKey(ctx, key), result.data);
        return result;
    }

    // Write-through. mails_all writes bump the global change log so other instances invalidate.
    internal static async Task SetCustomDataWithLockAsync<T>(
        IGameApiClient client, IExecutionContext ctx, string key, T value, string writeLock, ILogger? logger = null)
    {
        var cacheKey = CustomCacheKey(ctx, key);
        var url = $"{BaseHost}/v1/data/projects/{ctx.ProjectId}/custom/{GlobalCustomId}/items";
        await PostItemAsync(ctx, url, key, value, writeLock, cacheKey);
        await OnCustomWriteSucceededAsync(client, ctx, key, value, cacheKey, logger);
    }

    // Evicts cache entry on successful delete so stale data cannot be served afterward.
    internal static async Task DeleteCustomDataAsync(
        IGameApiClient _, IExecutionContext ctx, string key)
    {
        var url = $"{BaseHost}/v1/data/projects/{ctx.ProjectId}/custom/{GlobalCustomId}/items/{Uri.EscapeDataString(key)}";
        var (_, status, _) = await SendAsync(HttpMethod.Delete, ctx, url, body: null, treat404AsSuccess: true);
        if ((int)status >= 400 && status != HttpStatusCode.NotFound)
            throw new InvalidOperationException($"DeleteCustomDataAsync failed: HTTP {(int)status} on {key}");
        MailboxCache.Evict(CustomCacheKey(ctx, key));
    }

    // ── Global mail change log (version-aware mails_all cache) ─────────────────

    // Reads the change log fresh. The key is in MailboxCache.IsNoCacheKey, so this never serves
    // a cached version — required for version validation to be meaningful.
    internal static async Task<GlobalMailChangeLog?> GetGlobalMailChangeLogAsync(
        IGameApiClient client, IExecutionContext ctx)
        => await GetCustomDataAsync<GlobalMailChangeLog>(client, ctx, MailboxConstants.KeyGlobalMailChangeLog);

    // Current global mail version (0 when the change log has never been written).
    internal static async Task<long> GetCurrentGlobalMailVersionAsync(
        IGameApiClient client, IExecutionContext ctx)
        => (await GetGlobalMailChangeLogAsync(client, ctx))?.Version ?? 0;

    // Retry budget for the change-log bump under writeLock (409) contention.
    private const int ChangeLogBumpAttempts = 3;

    // Increments the global mail version. Reads with writeLock, retries on 409 by re-reading the
    // latest log and incrementing again. THROWS on failure (exhausted 409 retries, or any non-409
    // error) — the caller (OnCustomWriteSucceededAsync) treats a bump failure as best-effort AFTER
    // the mails_all source-of-truth write has already committed.
    internal static async Task<long> BumpGlobalMailChangeLogAsync(
        IGameApiClient client, IExecutionContext ctx)
    {
        for (var attempt = 0; attempt < ChangeLogBumpAttempts; attempt++)
        {
            var (log, writeLock) = await GetCustomDataWithLockAsync<GlobalMailChangeLog>(
                client, ctx, MailboxConstants.KeyGlobalMailChangeLog);
            log ??= new GlobalMailChangeLog();      // missing → first bump produces Version = 1
            log.Version += 1;
            log.LastChangedAt = DateTime.UtcNow.ToString("o");
            try
            {
                await SetCustomDataWithLockAsync(client, ctx, MailboxConstants.KeyGlobalMailChangeLog, log, writeLock);
                return log.Version;
            }
            // Retry on 409 EXCEPT on the final attempt, where it propagates as a bump failure.
            catch (Exception ex) when (IsWriteLockConflict(ex) && attempt < ChangeLogBumpAttempts - 1) { }
        }
        throw new InvalidOperationException("global_mail_change_log bump exhausted writeLock retries");
    }

    // Version-aware read for mails_all: validate cache against a fresh change-log version.
    private static async Task<T?> GetCustomDataWithVersionAwareCacheAsync<T>(
        IGameApiClient client, IExecutionContext ctx, string key, string cacheKey)
    {
        var version = await GetCurrentGlobalMailVersionAsync(client, ctx);
        if (MailboxCache.TryGetVersioned<T>(cacheKey, version, out var cached)) return cached;
        var result = await FetchCustomAsync<T>(ctx, key);
        MailboxCache.SetVersioned(cacheKey, result, version);
        return result;
    }

    // Post-write cache coherence. Bumping is keyed on the mails_all WRITE, so it covers every
    // mutation path (SendGlobalMail, SendUserMail, DeleteGlobalMail, ExpireMail, SetMailEndTime,
    // PurgeExpired) and never fires for no-ops, which return before calling Set*.
    //
    // mails_all is the SOURCE OF TRUTH and is already committed by the time we get here. The change
    // log is only a cross-instance cache-invalidation signal, so its bump is BEST-EFFORT: a bump
    // failure (exhausted 409 retries or any transient error) must NOT fail the committed mutation.
    // On failure we evict this instance's mails_all cache (so it never serves an entry stamped with
    // a wrong/old version) and let the TTL window cover other instances until the next bump.
    private static async Task OnCustomWriteSucceededAsync<T>(
        IGameApiClient client, IExecutionContext ctx, string key, T value, string cacheKey, ILogger? logger)
    {
        if (key != MailboxConstants.KeyMailsAll)
        {
            MailboxCache.Set(cacheKey, value);
            return;
        }

        try
        {
            var newVersion = await BumpGlobalMailChangeLogAsync(client, ctx);
            MailboxCache.SetVersioned(cacheKey, value, newVersion); // stamp with the new version
        }
        catch (Exception ex)
        {
            // Source-of-truth write already succeeded — do NOT rethrow. Evict so the next read
            // refetches at the current version; other workers fall back to the TTL window.
            MailboxCache.Evict(cacheKey);
            logger?.LogWarning(ex,
                "mails_all committed but global_mail_change_log bump failed; evicted local mails_all cache, " +
                "TTL fallback protects other workers until the next successful bump.");
        }
    }

    private static async Task<T?> FetchCustomAsync<T>(IExecutionContext ctx, string key)
    {
        var url = $"{BaseHost}/v1/data/projects/{ctx.ProjectId}/custom/{GlobalCustomId}/items?keys={Uri.EscapeDataString(key)}";
        var (json, _, _) = await SendAsync(HttpMethod.Get, ctx, url, body: null);
        return ExtractFirstValue<T>(json);
    }

    // ── Conflict detection ───────────────────────────────────────────────────

    internal static bool IsWriteLockConflict(Exception ex) =>
        ex.Message.Contains("409") ||
        ex.Message.Contains("Conflict", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("WriteLock", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("write_lock", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("write-lock", StringComparison.OrdinalIgnoreCase);

    // ── Private helpers ──────────────────────────────────────────────────────

    private static async Task PostItemAsync<T>(
        IExecutionContext ctx, string url, string key, T value, string? writeLock,
        string? cacheKey = null)
    {
        // Serialize the value as a compact JSON string so Cloud Save stores a flat
        // string instead of a nested object tree. This keeps the dashboard tidy and
        // avoids key-size surprises from pretty-printed nesting.
        // The read path (DeserializeValue<T>) already handles ValueKind.String by
        // double-parsing, so old nested-object data AND new inline-string data both
        // deserialize correctly — full backward compatibility.
        var compactValue = JsonSerializer.Serialize(value);

        // Build the POST body with `value` as a JSON string literal.
        // Using JsonSerializer on a Dictionary<string, object?> would double-escape
        // the string, so we build the JSON manually with a JsonObject.
        using var bodyDoc = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(bodyDoc))
        {
            writer.WriteStartObject();
            writer.WriteString("key", key);
            // Write the compact JSON as a raw string value — Cloud Save stores it as-is.
            writer.WriteString("value", compactValue);
            if (!string.IsNullOrEmpty(writeLock))
                writer.WriteString("writeLock", writeLock!);
            writer.WriteEndObject();
        }
        var bodyJson = Encoding.UTF8.GetString(bodyDoc.ToArray());

        var (respBody, status, _) = await SendAsync(
            HttpMethod.Post, ctx, url,
            body: new StringContent(bodyJson, Encoding.UTF8, "application/json"));

        if ((int)status == 409)
        {
            // Evict so the next read fetches a fresh writeLock token from REST.
            if (cacheKey != null) MailboxCache.Evict(cacheKey);
            throw new InvalidOperationException($"Cloud Save write-lock conflict (HTTP 409) on key={key}. Response: {respBody}");
        }
        if ((int)status >= 400)
            throw new InvalidOperationException($"Cloud Save SetItem failed: HTTP {(int)status} on key={key}. Response: {respBody}");
    }

    private static async Task<(string? body, HttpStatusCode status, HttpResponseHeaders headers)> SendAsync(
        HttpMethod method, IExecutionContext ctx, string url, HttpContent? body, bool treat404AsSuccess = false)
    {
        using var req = new HttpRequestMessage(method, url) { Content = body };
        AddAuth(ctx, req);
        using var resp = await _http.SendAsync(req).ConfigureAwait(false);
        var text = resp.Content == null ? null : await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound && treat404AsSuccess)
            return (text, resp.StatusCode, resp.Headers);
        // Don't throw here; callers inspect status + body to decide.
        return (text, resp.StatusCode, resp.Headers);
    }

    private static void AddAuth(IExecutionContext ctx, HttpRequestMessage req)
    {
        // ServiceToken first (s2s, allows cross-player + custom-data writes).
        // AccessToken fallback for environments where ServiceToken is not populated.
        var token = !string.IsNullOrEmpty(ctx.ServiceToken) ? ctx.ServiceToken : ctx.AccessToken;
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException("CloudSaveHelper: both ctx.ServiceToken and ctx.AccessToken are empty.");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // ProjectId header is required by some Cloud Save deployments.
        if (!string.IsNullOrEmpty(ctx.ProjectId))
            req.Headers.TryAddWithoutValidation("ProjectId", ctx.ProjectId);
    }

    // Parses {"results":[{"value":"<json-string>", ...}, ...]} → T
    private static T? ExtractFirstValue<T>(string? json)
    {
        if (string.IsNullOrEmpty(json)) return default;
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            return default;
        return DeserializeValue<T>(results[0]);
    }

    private static (T? data, string writeLock) ExtractFirstWithLock<T>(string? json)
    {
        if (string.IsNullOrEmpty(json)) return (default, string.Empty);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            return (default, string.Empty);
        var item = results[0];
        var data = DeserializeValue<T>(item);
        var wl = item.TryGetProperty("writeLock", out var wlProp) && wlProp.ValueKind == JsonValueKind.String
            ? wlProp.GetString()
            : null;
        return (data, wl ?? string.Empty);
    }

    private static T? DeserializeValue<T>(JsonElement item)
    {
        if (!item.TryGetProperty("value", out var val)) return default;

        // Extract key name for diagnostic logging (best-effort).
        var keyName = item.TryGetProperty("key", out var keyProp) && keyProp.ValueKind == JsonValueKind.String
            ? keyProp.GetString() ?? "?"
            : "?";

        if (val.ValueKind == JsonValueKind.Null || val.ValueKind == JsonValueKind.Undefined)
            return default;

        // New format: value stored as a compact JSON string (double-serialized).
        // Legacy format: value stored as a raw JSON object/array.
        // Both paths converge on DeserializeChecked<T>.
        if (val.ValueKind == JsonValueKind.String)
        {
            var s = val.GetString();
            if (string.IsNullOrEmpty(s)) return default;
            try
            {
                using var inner = JsonDocument.Parse(s);
                return DeserializeChecked<T>(inner.RootElement);
            }
            catch (JsonException ex)
            {
                Console.Error.WriteLine(
                    $"[CloudSaveHelper] DeserializeValue<{typeof(T).Name}> failed for key={keyName}, " +
                    $"valueKind=String, error={ex.Message}, rawLength={s.Length}");
                return default;
            }
        }

        // Legacy path: value is a raw JSON object/array (pre-compact-string migration).
        try
        {
            return DeserializeChecked<T>(val);
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine(
                $"[CloudSaveHelper] DeserializeValue<{typeof(T).Name}> failed for key={keyName}, " +
                $"valueKind={val.ValueKind}, error={ex.Message}");
            return default;
        }
    }

    // Deserializes `element` into T, but returns default (instead of throwing) when the
    // stored JSON's shape cannot map to T — e.g. an object {…} stored where a List<>
    // (array […]) is expected. A single malformed value on a shared, project-wide key
    // such as `mails_all` would otherwise throw a JsonException that Cloud Code reports
    // as HTTP 422, bricking every mailbox endpoint that reads the key. Returning default
    // lets callers treat it as "no data yet"; the next write overwrites (self-heals) the
    // key with the correct shape. Field-level data is preserved on a matching shape.
    private static T? DeserializeChecked<T>(JsonElement element)
    {
        var type = typeof(T);
        // Types with a custom JsonConverter (e.g. GlobalMailCollection) accept multiple
        // root shapes by design — array vs object — so the generic shape guard below
        // must not pre-reject them. Defer entirely to the converter, which still
        // returns a safe default for foreign/malformed values.
        if (!HasCustomConverter(type))
        {
            if (ExpectsJsonArray(type) && element.ValueKind != JsonValueKind.Array)
                return default;
            if (ExpectsJsonObject(type) && element.ValueKind != JsonValueKind.Object)
                return default;
        }
        try
        {
            return JsonSerializer.Deserialize<T>(element.GetRawText());
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static bool HasCustomConverter(Type type) =>
        Attribute.IsDefined(type, typeof(JsonConverterAttribute));

    private static bool ExpectsJsonArray(Type type)
    {
        if (type == typeof(string)) return false;
        if (type.IsArray) return true;
        return typeof(System.Collections.IEnumerable).IsAssignableFrom(type);
    }

    private static bool ExpectsJsonObject(Type type)
    {
        if (type == typeof(string)) return false;
        if (ExpectsJsonArray(type)) return false;
        return type.IsClass;
    }
}
