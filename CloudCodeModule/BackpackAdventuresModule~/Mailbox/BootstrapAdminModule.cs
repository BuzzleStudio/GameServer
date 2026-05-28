using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

/// <summary>
/// One-shot bootstrap endpoint: adds the calling player to mailbox_admin_allowlist
/// ONLY when the allowlist is empty or absent. After the allowlist has at least
/// one entry, this endpoint becomes a no-op for non-admin callers. This avoids
/// the chicken-and-egg problem of needing an admin to add the first admin.
///
/// Security: there is a brief privilege-escalation window — the first ever caller
/// becomes admin. Remove or replace this endpoint with a dashboard-driven seed
/// step once a permanent admin exists.
/// </summary>
public class BootstrapAdminModule
{
    private readonly IExecutionContext _context;
    private readonly IGameApiClient _gameApiClient;
    private readonly ILogger<BootstrapAdminModule> _logger;

    public BootstrapAdminModule(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        ILogger<BootstrapAdminModule> logger)
    {
        _context = context;
        _gameApiClient = gameApiClient;
        _logger = logger;
    }

    public class BootstrapResponse
    {
        public bool Success { get; set; }
        public bool WasSeeded { get; set; }
        public string Message { get; set; } = string.Empty;
        public System.Collections.Generic.List<string> Allowlist { get; set; } = new();
    }

    [CloudCodeFunction("BootstrapAdminAllowlist")]
    public async Task<BootstrapResponse> BootstrapAsync()
    {
        var callerId = _context.PlayerId ?? string.Empty;
        _logger.LogInformation("BootstrapAdminAllowlist called by {PlayerId}", callerId);

        var current = await CloudSaveHelper.GetCustomDataAsync<AdminAllowlist>(
            _gameApiClient, _context, MailboxConstants.KeyAdminAllowlist);

        // Already populated — do not allow self-promotion.
        if (current != null && current.PlayerIds != null && current.PlayerIds.Count > 0)
        {
            var alreadyAdmin = current.PlayerIds.Contains(callerId);
            _logger.LogInformation("BootstrapAdminAllowlist no-op: allowlist already has {Count} entries (callerIsAdmin={IsAdmin})",
                current.PlayerIds.Count, alreadyAdmin);
            return new BootstrapResponse
            {
                Success = true,
                WasSeeded = false,
                Message = alreadyAdmin
                    ? "Allowlist already populated; caller is already admin."
                    : "Allowlist already populated; bootstrap refused (caller not admin).",
                Allowlist = alreadyAdmin ? current.PlayerIds : new System.Collections.Generic.List<string>()
            };
        }

        // First-run seed: add the caller.
        var seed = new AdminAllowlist
        {
            PlayerIds = new System.Collections.Generic.List<string> { callerId }
        };
        await CloudSaveHelper.SetCustomDataAsync(
            _gameApiClient, _context, MailboxConstants.KeyAdminAllowlist, seed);

        _logger.LogInformation("BootstrapAdminAllowlist: seeded with {PlayerId}", callerId);
        return new BootstrapResponse
        {
            Success = true,
            WasSeeded = true,
            Message = $"Seeded mailbox_admin_allowlist with caller {callerId}.",
            Allowlist = seed.PlayerIds
        };
    }
}
