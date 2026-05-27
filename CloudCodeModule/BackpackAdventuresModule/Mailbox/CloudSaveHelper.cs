using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudSave.Model;

namespace BackpackAdventures.CloudCode;

/// <summary>Typed JSON round-trips over ICloudSaveDataApi.</summary>
internal static class CloudSaveHelper
{
    // customId used for project-wide "global" custom data store
    private const string GlobalCustomId = "default";

    internal static async Task<T?> GetPlayerDataAsync<T>(
        IGameApiClient client, IExecutionContext ctx, string playerId, string key)
    {
        try
        {
            var response = await client.CloudSaveData.GetItemsAsync(
                ctx, ctx.AccessToken, ctx.ProjectId, playerId,
                new List<string> { key }, after: null);
            if (response.Data.Results.Count == 0) return default;
            var raw = response.Data.Results[0].Value?.ToString();
            return string.IsNullOrEmpty(raw) ? default : JsonSerializer.Deserialize<T>(raw);
        }
        catch { return default; }
    }

    internal static async Task SetPlayerDataAsync<T>(
        IGameApiClient client, IExecutionContext ctx, string playerId, string key, T value)
    {
        var json = JsonSerializer.Serialize(value);
        var body = new SetItemBody(key, json, string.Empty);
        await client.CloudSaveData.SetItemAsync(ctx, ctx.AccessToken, ctx.ProjectId, playerId, body);
    }

    internal static async Task<T?> GetCustomDataAsync<T>(
        IGameApiClient client, IExecutionContext ctx, string key)
    {
        try
        {
            var response = await client.CloudSaveData.GetCustomItemsAsync(
                ctx, ctx.AccessToken, ctx.ProjectId, GlobalCustomId,
                new List<string> { key }, after: null);
            if (response.Data.Results.Count == 0) return default;
            var raw = response.Data.Results[0].Value?.ToString();
            return string.IsNullOrEmpty(raw) ? default : JsonSerializer.Deserialize<T>(raw);
        }
        catch { return default; }
    }

    internal static async Task SetCustomDataAsync<T>(
        IGameApiClient client, IExecutionContext ctx, string key, T value)
    {
        var json = JsonSerializer.Serialize(value);
        var body = new SetItemBody(key, json, string.Empty);
        await client.CloudSaveData.SetCustomItemAsync(ctx, ctx.AccessToken, ctx.ProjectId, GlobalCustomId, body);
    }
}
