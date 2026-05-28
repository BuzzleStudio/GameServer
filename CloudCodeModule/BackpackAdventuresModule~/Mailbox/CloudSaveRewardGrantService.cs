// TODO(economy-sdk): replace CloudSaveRewardGrantService with EconomyRewardGrantService
// and wire IRewardGrantService in DI once Com.Unity.Services.Economy is added to BackpackAdventuresModule.csproj.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

/// <summary>
/// Placeholder reward-grant implementation backed by Cloud Save player data (key: player_wallet).
/// Increments currency and item counts in the wallet document.
/// This is a seam — swap for a real Economy SDK call when the package is available.
/// </summary>
public sealed class CloudSaveRewardGrantService : IRewardGrantService
{
    private readonly IGameApiClient _gameApiClient;
    private readonly IExecutionContext _context;
    private readonly ILogger<CloudSaveRewardGrantService> _logger;

    public CloudSaveRewardGrantService(
        IGameApiClient gameApiClient,
        IExecutionContext context,
        ILogger<CloudSaveRewardGrantService> logger)
    {
        _gameApiClient = gameApiClient;
        _context = context;
        _logger = logger;
    }

    public async Task<bool> GrantRewardsAsync(
        string playerId,
        IReadOnlyList<MailAttachment> attachments,
        string idempotencyKey)
    {
        if (string.IsNullOrEmpty(playerId)) throw new ArgumentException("playerId is required", nameof(playerId));
        if (attachments == null || attachments.Count == 0) return true;

        _logger.LogInformation(
            "CloudSaveRewardGrantService: granting {Count} attachment(s) for player {PlayerId}, idempotencyKey={Key}",
            attachments.Count, playerId, idempotencyKey);

        try
        {
            // Read existing wallet, increment, write back (no writeLock on wallet — last-write-wins acceptable for placeholder).
            var wallet = await CloudSaveHelper.GetPlayerDataAsync<Dictionary<string, int>>(
                _gameApiClient, _context, playerId, MailboxConstants.KeyPlayerWallet)
                ?? new Dictionary<string, int>();

            foreach (var att in attachments)
            {
                if (string.IsNullOrEmpty(att.ItemId)) continue;
                wallet.TryGetValue(att.ItemId, out var current);
                wallet[att.ItemId] = current + att.Quantity;
            }

            await CloudSaveHelper.SetPlayerDataAsync(
                _gameApiClient, _context, playerId, MailboxConstants.KeyPlayerWallet, wallet);

            _logger.LogInformation(
                "CloudSaveRewardGrantService: wallet updated for player {PlayerId}", playerId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CloudSaveRewardGrantService: transient failure for player {PlayerId}", playerId);
            throw new RetryableGrantException($"Transient grant failure for player {playerId}", ex);
        }
    }
}
