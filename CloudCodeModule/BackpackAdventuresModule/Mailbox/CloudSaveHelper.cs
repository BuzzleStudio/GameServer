using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudSave.Model;

namespace BackpackAdventures.CloudCode;

/// <summary>Thin wrapper around ICloudSaveDataApi for typed JSON round-trips.</summary>
internal static class CloudSaveHelper
{
    internal static async Task<T?> GetPlayerDataAsync<T>(
        IGameApiClient client,
        IExecutionContext ctx,
        string playerId,
        string key)
    {
        var response = await client.CloudSaveData.GetItemsAsync(
            ctx, ctx.AccessToken, ctx.ProjectId, playerId,
            new List<string> { key });

        if (response.Data.Results.Count == 0) return default;
        var raw = response.Data.Results[0].Value?.ToString();
        if (string.IsNullOrEmpty(raw)) return default;
        return JsonSerializer.Deserialize<T>(raw);
    }

    internal static async Task SetPlayerDataAsync<T>(
        IGameApiClient client,
        IExecutionContext ctx,
        string playerId,
        string key,
        T value)
    {
        var json = JsonSerializer.Serialize(value);
        var body = new SetItemBody { Key = key, Value = json };
        await client.CloudSaveData.SetItemAsync(
            ctx, ctx.AccessToken, ctx.ProjectId, playerId, body);
    }

    internal static async Task<T?> GetCustomDataAsync<T>(
        IGameApiClient client,
        IExecutionContext ctx,
        string key)
    {
        var response = await client.CloudSaveData.GetCustomItemsAsync(
            ctx, ctx.AccessToken, ctx.ProjectId,
            new List<string> { key });

        if (response.Data.Results.Count == 0) return default;
        var raw = response.Data.Results[0].Value?.ToString();
        if (string.IsNullOrEmpty(raw)) return default;
        return JsonSerializer.Deserialize<T>(raw);
    }

    internal static async Task SetCustomDataAsync<T>(
        IGameApiClient client,
        IExecutionContext ctx,
        string key,
        T value)
    {
        var json = JsonSerializer.Serialize(value);
        var body = new SetItemBody { Key = key, Value = json };
        await client.CloudSaveData.SetCustomItemAsync(
            ctx, ctx.AccessToken, ctx.ProjectId, body);
    }
}
