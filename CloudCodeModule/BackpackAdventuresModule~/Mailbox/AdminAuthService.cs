using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

/// <summary>
/// Verifies whether a player ID is present in the mailbox admin allowlist.
/// Reads the "mailbox_admin_allowlist" Cloud Save custom data key.
/// On missing or null allowlist, all callers are rejected (fail-closed).
/// </summary>
public sealed class AdminAuthService
{
    private readonly IGameApiClient _gameApiClient;
    private readonly IExecutionContext _context;
    private readonly ILogger<AdminAuthService> _logger;

    // Module-lifetime in-memory cache for the allowlist (refreshed on each module instantiation by the CC runtime).
    private AdminAllowlist? _cachedAllowlist;

    public AdminAuthService(
        IGameApiClient gameApiClient,
        IExecutionContext context,
        ILogger<AdminAuthService> logger)
    {
        _gameApiClient = gameApiClient;
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Returns true when <paramref name="playerId"/> is present in the admin allowlist.
    /// Returns false if the allowlist key is absent (fail-closed) or player is not listed.
    /// </summary>
    public async Task<bool> IsAdminAsync(string playerId)
    {
        if (string.IsNullOrEmpty(playerId)) return false;

        if (_cachedAllowlist == null)
        {
            _cachedAllowlist = await CloudSaveHelper.GetCustomDataAsync<AdminAllowlist>(
                _gameApiClient, _context, MailboxConstants.KeyAdminAllowlist);
        }

        if (_cachedAllowlist == null || _cachedAllowlist.PlayerIds.Count == 0)
        {
            _logger.LogWarning("Admin allowlist is absent or empty — all admin calls rejected (fail-closed).");
            return false;
        }

        return _cachedAllowlist.PlayerIds.Contains(playerId);
    }

    /// <summary>
    /// Throws <see cref="UnauthorizedAccessException"/> with <see cref="MailboxError.Unauthorized"/>
    /// if the caller is not an admin. Call at the top of every admin endpoint.
    /// </summary>
    public async Task RequireAdminAsync(string playerId)
    {
        if (!await IsAdminAsync(playerId))
        {
            _logger.LogWarning("Unauthorized admin call attempt by player {PlayerId}", playerId);
            throw new UnauthorizedAccessException(MailboxError.Unauthorized);
        }
    }
}
