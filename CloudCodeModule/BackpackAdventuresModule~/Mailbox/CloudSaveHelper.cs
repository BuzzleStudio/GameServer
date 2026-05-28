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
using System.Threading.Tasks;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

internal static class CloudSaveHelper
{
    // Must match the Cloud Save custom data access-class name configured in the UGS dashboard.
    internal const string GlobalCustomId = "default";

    private const string BaseHost = "https://services.api.unity.com";

    // One static HttpClient per module lifetime — required to avoid socket exhaustion.
    private static readonly HttpClient _http = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    // ── Player data ──────────────────────────────────────────────────────────

    internal static async Task<T?> GetPlayerDataAsync<T>(
        IGameApiClient _, IExecutionContext ctx, string playerId, string key)
    {
        var url = $"{BaseHost}/cloud-save/v1/data/projects/{ctx.ProjectId}/players/{Uri.EscapeDataString(playerId)}/items?keys={Uri.EscapeDataString(key)}";
        var (json, _, _) = await SendAsync(HttpMethod.Get, ctx, url, body: null);
        return ExtractFirstValue<T>(json);
    }

    internal static async Task<(T? data, string writeLock)> GetPlayerDataWithLockAsync<T>(
        IGameApiClient _, IExecutionContext ctx, string playerId, string key)
    {
        var url = $"{BaseHost}/cloud-save/v1/data/projects/{ctx.ProjectId}/players/{Uri.EscapeDataString(playerId)}/items?keys={Uri.EscapeDataString(key)}";
        var (json, _, _) = await SendAsync(HttpMethod.Get, ctx, url, body: null);
        return ExtractFirstWithLock<T>(json);
    }

    internal static async Task SetPlayerDataAsync<T>(
        IGameApiClient _, IExecutionContext ctx, string playerId, string key, T value,
        string? writeLock = null)
    {
        var url = $"{BaseHost}/cloud-save/v1/data/projects/{ctx.ProjectId}/players/{Uri.EscapeDataString(playerId)}/item";
        await PostItemAsync(ctx, url, key, value, writeLock);
    }

    // ── Custom data (project-wide) ───────────────────────────────────────────

    internal static async Task<T?> GetCustomDataAsync<T>(
        IGameApiClient _, IExecutionContext ctx, string key)
    {
        var url = $"{BaseHost}/cloud-save/v1/data/projects/{ctx.ProjectId}/custom/{GlobalCustomId}/items?keys={Uri.EscapeDataString(key)}";
        var (json, _, _) = await SendAsync(HttpMethod.Get, ctx, url, body: null);
        return ExtractFirstValue<T>(json);
    }

    internal static async Task SetCustomDataAsync<T>(
        IGameApiClient _, IExecutionContext ctx, string key, T value)
    {
        var url = $"{BaseHost}/cloud-save/v1/data/projects/{ctx.ProjectId}/custom/{GlobalCustomId}/item";
        await PostItemAsync(ctx, url, key, value, writeLock: null);
    }

    internal static async Task<(T? data, string writeLock)> GetCustomDataWithLockAsync<T>(
        IGameApiClient _, IExecutionContext ctx, string key)
    {
        var url = $"{BaseHost}/cloud-save/v1/data/projects/{ctx.ProjectId}/custom/{GlobalCustomId}/items?keys={Uri.EscapeDataString(key)}";
        var (json, _, _) = await SendAsync(HttpMethod.Get, ctx, url, body: null);
        return ExtractFirstWithLock<T>(json);
    }

    internal static async Task SetCustomDataWithLockAsync<T>(
        IGameApiClient _, IExecutionContext ctx, string key, T value, string writeLock)
    {
        var url = $"{BaseHost}/cloud-save/v1/data/projects/{ctx.ProjectId}/custom/{GlobalCustomId}/item";
        await PostItemAsync(ctx, url, key, value, writeLock);
    }

    internal static async Task DeleteCustomDataAsync(
        IGameApiClient _, IExecutionContext ctx, string key)
    {
        var url = $"{BaseHost}/cloud-save/v1/data/projects/{ctx.ProjectId}/custom/{GlobalCustomId}/items/{Uri.EscapeDataString(key)}";
        var (_, status, _) = await SendAsync(HttpMethod.Delete, ctx, url, body: null, treat404AsSuccess: true);
        if ((int)status >= 400 && status != HttpStatusCode.NotFound)
            throw new InvalidOperationException($"DeleteCustomDataAsync failed: HTTP {(int)status} on {key}");
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
        IExecutionContext ctx, string url, string key, T value, string? writeLock)
    {
        // Cloud Save stores `value` as an arbitrary JSON value. Match the legacy behaviour:
        // serialize the payload as JSON, then send the resulting string verbatim. Reads
        // expect a string field and JSON-deserialize it again.
        var valueJson = JsonSerializer.Serialize(value);
        var payload = new Dictionary<string, object>
        {
            ["key"] = key,
            ["value"] = valueJson,
        };
        if (!string.IsNullOrEmpty(writeLock)) payload["writeLock"] = writeLock!;
        var bodyJson = JsonSerializer.Serialize(payload);

        var (respBody, status, _) = await SendAsync(
            HttpMethod.Post, ctx, url,
            body: new StringContent(bodyJson, Encoding.UTF8, "application/json"));

        if ((int)status == 409)
            throw new InvalidOperationException($"Cloud Save write-lock conflict (HTTP 409) on key={key}. Response: {respBody}");
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
        // value is typically returned as a JSON string containing escaped JSON
        // (matching the legacy SDK behaviour). Handle both cases defensively.
        if (val.ValueKind == JsonValueKind.String)
        {
            var s = val.GetString();
            return string.IsNullOrEmpty(s) ? default : JsonSerializer.Deserialize<T>(s);
        }
        if (val.ValueKind == JsonValueKind.Null || val.ValueKind == JsonValueKind.Undefined)
            return default;
        return JsonSerializer.Deserialize<T>(val.GetRawText());
    }
}
