using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

/// <summary>
/// Verifies whether a player ID is in the admin allowlist.
///
/// Storage strategy: hybrid.
///   1. Compile-time hardcoded set (always-honoured, no UGS Dashboard step needed).
///   2. Optional Cloud Save custom-data override (mailbox_admin_allowlist) for ops
///      to add/remove admins without a code deploy. Read is best-effort — Cloud Save
///      404 (e.g. access class not configured in the project) is treated as "no
///      override; use hardcoded list only".
///
/// Why hybrid: the Cloud Save Custom Data feature requires an Access Class to be
/// manually defined in the UGS Dashboard (the "default" class is NOT auto-created),
/// which produces HTTP 404 with error code 54 if the project hasn't set one up.
/// Hardcoding the bootstrap admin guarantees the gate works out-of-the-box without
/// blocking on Dashboard configuration.
///
/// Static helper — Cloud Code's DI only auto-provides IExecutionContext +
/// IGameApiClient to module classes, NOT to custom registered services.
/// </summary>
public static class AdminAuth
{
    // Compile-time admin allowlist. Add player IDs here for new permanent admins.
    // Operations can additionally promote via Cloud Save custom data when configured.
    private static readonly System.Collections.Generic.HashSet<string> HardcodedAdmins
        = new System.Collections.Generic.HashSet<string>
        {
            "7gSw1RxzqY6iSCQe99L9tQFFj6Kd", // bootstrap admin — primary dev/test account
        };

    /// <summary>Returns true when <paramref name="playerId"/> is in the admin allowlist.</summary>
    public static async Task<bool> IsAdminAsync(
        IGameApiClient gameApiClient,
        IExecutionContext context,
        string playerId,
        ILogger logger)
    {
        if (string.IsNullOrEmpty(playerId)) return false;

        if (HardcodedAdmins.Contains(playerId))
            return true;

        // Best-effort Cloud Save override (lets ops promote without a code deploy).
        // Any failure here is treated as "no override available" — the hardcoded
        // set is the authoritative source.
        try
        {
            var allowlist = await CloudSaveHelper.GetCustomDataAsync<AdminAllowlist>(
                gameApiClient, context, MailboxConstants.KeyAdminAllowlist);

            if (allowlist?.PlayerIds != null && allowlist.PlayerIds.Contains(playerId))
                return true;
        }
        catch (System.Exception ex)
        {
            logger.LogInformation("Cloud Save allowlist read skipped ({Message}); using hardcoded admins only.", ex.Message);
        }

        logger.LogWarning("Player {PlayerId} is NOT admin (not in hardcoded set or Cloud Save override).", playerId);
        return false;
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
