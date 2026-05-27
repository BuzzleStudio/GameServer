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
    private const string GlobalCustomId = "default";

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

    // Returns null when the key does not exist; rethrows on API errors.
    internal static async Task<T?> GetCustomDataAsync<T>(
        IGameApiClient client, IExecutionContext ctx, string key)
    {
        var response = await client.CloudSaveData.GetCustomItemsAsync(
            ctx, ctx.AccessToken, ctx.ProjectId, GlobalCustomId,
            new List<string> { key }, after: null);
        if (response.Data.Results.Count == 0) return default;
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
}
