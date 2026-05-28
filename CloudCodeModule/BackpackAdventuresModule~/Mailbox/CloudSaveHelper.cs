using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudSave.Model;

namespace BackpackAdventures.CloudCode;

internal static class CloudSaveHelper
{
    // Must match the Cloud Save custom data collection name configured in the UGS dashboard.
    internal const string GlobalCustomId = "default";

    // ── Player data ──────────────────────────────────────────────────────────

    // Returns null when the key does not exist; rethrows on any API or network error
    // so callers are not silently handed an empty document that would overwrite real data.
    internal static async Task<T?> GetPlayerDataAsync<T>(
        IGameApiClient client, IExecutionContext ctx, string playerId, string key)
    {
        var response = await client.CloudSaveData.GetItemsAsync(
            ctx, ctx.AccessToken, ctx.ProjectId, playerId,
            new List<string> { key }, after: null);
        if (response.Data.Results.Count == 0) return default;
        var raw = response.Data.Results[0].Value?.ToString();
        return string.IsNullOrEmpty(raw) ? default : JsonSerializer.Deserialize<T>(raw);
    }

    // Returns (null, "") when the key does not exist; rethrows on API errors.
    internal static async Task<(T? data, string writeLock)> GetPlayerDataWithLockAsync<T>(
        IGameApiClient client, IExecutionContext ctx, string playerId, string key)
    {
        var response = await client.CloudSaveData.GetItemsAsync(
            ctx, ctx.AccessToken, ctx.ProjectId, playerId,
            new List<string> { key }, after: null);
        if (response.Data.Results.Count == 0) return (default, string.Empty);
        var item = response.Data.Results[0];
        var raw = item.Value?.ToString();
        var data = string.IsNullOrEmpty(raw) ? default : JsonSerializer.Deserialize<T>(raw);
        return (data, item.WriteLock ?? string.Empty);
    }

    internal static async Task SetPlayerDataAsync<T>(
        IGameApiClient client, IExecutionContext ctx, string playerId, string key, T value,
        string? writeLock = null)
    {
        var json = JsonSerializer.Serialize(value);
        var body = new SetItemBody(key, json, writeLock ?? string.Empty);
        await client.CloudSaveData.SetItemAsync(ctx, ctx.AccessToken, ctx.ProjectId, playerId, body);
    }

    // ── Custom data (project-wide) ───────────────────────────────────────────

    // Returns null when the key does not exist; rethrows on API errors.
    internal static async Task<T?> GetCustomDataAsync<T>(
        IGameApiClient client, IExecutionContext ctx, string key)
    {
        // Diagnostic null checks — surface a clear message instead of a bare NRE.
        if (client == null)              throw new InvalidOperationException("CloudSaveHelper: client is null");
        if (client.CloudSaveData == null) throw new InvalidOperationException("CloudSaveHelper: client.CloudSaveData is null (Cloud Save SDK surface unavailable)");
        if (ctx == null)                 throw new InvalidOperationException("CloudSaveHelper: ctx is null");
        if (ctx.AccessToken == null)     throw new InvalidOperationException("CloudSaveHelper: ctx.AccessToken is null");
        if (ctx.ProjectId == null)       throw new InvalidOperationException("CloudSaveHelper: ctx.ProjectId is null");

        var response = await client.CloudSaveData.GetCustomItemsAsync(
            ctx, ctx.AccessToken, ctx.ProjectId, GlobalCustomId,
            new List<string> { key }, after: null);
        if (response?.Data?.Results == null || response.Data.Results.Count == 0) return default;
        var raw = response.Data.Results[0].Value?.ToString();
        return string.IsNullOrEmpty(raw) ? default : JsonSerializer.Deserialize<T>(raw);
    }

    internal static async Task SetCustomDataAsync<T>(
        IGameApiClient client, IExecutionContext ctx, string key, T value)
    {
        var json = JsonSerializer.Serialize(value);
        var body = new SetItemBody(key, json, string.Empty);
        await client.CloudSaveData.SetCustomItemAsync(ctx, ctx.AccessToken, ctx.ProjectId, GlobalCustomId, body);
    }

    // Returns (null, "") when the key does not exist; rethrows on API errors.
    // Used for optimistic-concurrency writes on project-wide custom data keys (e.g. global_mail_index_v2).
    internal static async Task<(T? data, string writeLock)> GetCustomDataWithLockAsync<T>(
        IGameApiClient client, IExecutionContext ctx, string key)
    {
        var response = await client.CloudSaveData.GetCustomItemsAsync(
            ctx, ctx.AccessToken, ctx.ProjectId, GlobalCustomId,
            new List<string> { key }, after: null);
        if (response.Data.Results.Count == 0) return (default, string.Empty);
        var item = response.Data.Results[0];
        var raw = item.Value?.ToString();
        var data = string.IsNullOrEmpty(raw) ? default : JsonSerializer.Deserialize<T>(raw);
        return (data, item.WriteLock ?? string.Empty);
    }

    // Conditional write on custom data keys using the provided writeLock token.
    // Throws when the server returns a 409 write-lock conflict; callers use IsWriteLockConflict to detect.
    internal static async Task SetCustomDataWithLockAsync<T>(
        IGameApiClient client, IExecutionContext ctx, string key, T value, string writeLock)
    {
        var json = JsonSerializer.Serialize(value);
        var body = new SetItemBody(key, json, writeLock);
        await client.CloudSaveData.SetCustomItemAsync(ctx, ctx.AccessToken, ctx.ProjectId, GlobalCustomId, body);
    }

    // Deletes a custom data key (project-wide). No-ops silently if the key does not exist.
    internal static async Task DeleteCustomDataAsync(
        IGameApiClient client, IExecutionContext ctx, string key)
    {
        try
        {
            await client.CloudSaveData.DeleteCustomItemAsync(
                ctx, ctx.AccessToken, ctx.ProjectId, GlobalCustomId, key);
        }
        catch (Exception ex) when (ex.Message.Contains("404") || ex.Message.Contains("NotFound", StringComparison.OrdinalIgnoreCase))
        {
            // Key already absent — treat as success.
        }
    }

    // ── Conflict detection ───────────────────────────────────────────────────

    internal static bool IsWriteLockConflict(Exception ex) =>
        ex.Message.Contains("409") ||
        ex.Message.Contains("Conflict", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("WriteLock", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("write_lock", StringComparison.OrdinalIgnoreCase);
}
