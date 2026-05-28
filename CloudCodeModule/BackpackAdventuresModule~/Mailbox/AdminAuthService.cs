using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

/// <summary>
/// Verifies whether a player ID is in the mailbox_admin_allowlist Cloud Save custom key.
/// Static helper — Cloud Code's DI only auto-provides IExecutionContext + IGameApiClient
/// to module classes (those with [CloudCodeFunction]), NOT to custom registered services.
/// Therefore admin auth runs as static methods taking those scoped services as params.
/// On missing or empty allowlist all callers are rejected (fail-closed).
/// </summary>
public static class AdminAuth
{
    /// <summary>Returns true when <paramref name="playerId"/> is in the admin allowlist.</summary>
    public static async Task<bool> IsAdminAsync(
        IGameApiClient gameApiClient,
        IExecutionContext context,
        string playerId,
        ILogger logger)
    {
        if (string.IsNullOrEmpty(playerId)) return false;

        var allowlist = await CloudSaveHelper.GetCustomDataAsync<AdminAllowlist>(
            gameApiClient, context, MailboxConstants.KeyAdminAllowlist);

        if (allowlist == null || allowlist.PlayerIds == null || allowlist.PlayerIds.Count == 0)
        {
            logger.LogWarning("Admin allowlist is absent or empty — all admin calls rejected (fail-closed).");
            return false;
        }

        return allowlist.PlayerIds.Contains(playerId);
    }

    /// <summary>
    /// Throws <see cref="UnauthorizedAccessException"/>(<see cref="MailboxError.Unauthorized"/>)
    /// when the caller is not in the allowlist. Call at the top of every admin endpoint.
    /// </summary>
    public static async Task RequireAdminAsync(
        IGameApiClient gameApiClient,
        IExecutionContext context,
        string playerId,
        ILogger logger)
    {
        if (!await IsAdminAsync(gameApiClient, context, playerId, logger))
        {
            logger.LogWarning("Unauthorized admin call attempt by player {PlayerId}", playerId);
            throw new UnauthorizedAccessException(MailboxError.Unauthorized);
        }
    }
}
