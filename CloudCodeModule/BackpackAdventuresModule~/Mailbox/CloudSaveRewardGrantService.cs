// TODO(economy-sdk): replace this static helper with an EconomyRewardGrant call
// once Com.Unity.Services.Economy is added to BackpackAdventuresModule.csproj.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

/// <summary>
/// Placeholder reward-grant implementation backed by Cloud Save player data (key: player_wallet).
/// Increments currency and item counts in the wallet document.
/// Static helper — Cloud Code's DI does not auto-provide IExecutionContext/IGameApiClient
/// to registered services; module classes pass them as method args instead.
/// </summary>
public static class RewardGrant
{
    /// <summary>Grants attachments to the player's wallet. Throws <see cref="RetryableGrantException"/> on transient failure.</summary>
    public static async Task<bool> GrantRewardsAsync(
        IGameApiClient gameApiClient,
        IExecutionContext context,
        string playerId,
        IReadOnlyList<MailAttachment> attachments,
        string idempotencyKey,
        ILogger logger)
    {
        if (string.IsNullOrEmpty(playerId)) throw new ArgumentException("playerId is required", nameof(playerId));
        if (attachments == null || attachments.Count == 0) return true;

        logger.LogInformation(
            "RewardGrant: granting {Count} attachment(s) for player {PlayerId}, idempotencyKey={Key}",
            attachments.Count, playerId, idempotencyKey);

        try
        {
            var wallet = await CloudSaveHelper.GetPlayerDataAsync<Dictionary<string, int>>(
                gameApiClient, context, playerId, MailboxConstants.KeyPlayerWallet)
                ?? new Dictionary<string, int>();

            foreach (var att in attachments)
            {
                if (string.IsNullOrEmpty(att.ItemId)) continue;
                wallet.TryGetValue(att.ItemId, out var current);
                wallet[att.ItemId] = current + att.Quantity;
            }

            await CloudSaveHelper.SetPlayerDataAsync(
                gameApiClient, context, playerId, MailboxConstants.KeyPlayerWallet, wallet);

            logger.LogInformation("RewardGrant: wallet updated for player {PlayerId}", playerId);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RewardGrant: transient failure for player {PlayerId}", playerId);
            throw new RetryableGrantException($"Transient grant failure for player {playerId}", ex);
        }
    }
}
