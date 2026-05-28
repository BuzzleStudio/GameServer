using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

/// <summary>
/// Admin-only endpoint: removes all expired refs from global_mail_index_v2
/// and deletes the corresponding mail_global_{mailId} custom keys.
/// WriteLock on global_mail_index_v2; returns Conflict if 409 occurs (admin retries manually).
/// </summary>
public class PurgeExpiredModule
{
    private readonly IExecutionContext _context;
    private readonly IGameApiClient _gameApiClient;
    private readonly ILogger<PurgeExpiredModule> _logger;

    public PurgeExpiredModule(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        ILogger<PurgeExpiredModule> logger)
    {
        _context = context;
        _gameApiClient = gameApiClient;
        _logger = logger;
    }

    [CloudCodeFunction("PurgeExpired")]
    public async Task<PurgeExpiredResponse> PurgeExpiredAsync(PurgeExpiredRequest request)
    {
        _logger.LogInformation("PurgeExpired called by operatorId={OperatorId}", request.OperatorId);

        AdminAuth.RequireAdminToolAsync(request.AdminToken, request.OperatorId, _logger);

        var (index, writeLock) = await CloudSaveHelper.GetCustomDataWithLockAsync<GlobalMailIndexV2>(
            _gameApiClient, _context, MailboxConstants.KeyGlobalMailIndexV2);

        if (index == null || index.Refs.Count == 0)
        {
            _logger.LogInformation("PurgeExpired: index empty, nothing to purge");
            return new PurgeExpiredResponse { Success = true, PurgedCount = 0, PurgedAt = DateTime.UtcNow.ToString("o") };
        }

        var expiredRefs = new List<GlobalMailRef>();
        var activeRefs  = new List<GlobalMailRef>();

        foreach (var r in index.Refs)
        {
            if (r.IsExpired()) expiredRefs.Add(r);
            else              activeRefs.Add(r);
        }

        if (expiredRefs.Count == 0)
        {
            _logger.LogInformation("PurgeExpired: no expired refs found");
            return new PurgeExpiredResponse { Success = true, PurgedCount = 0, PurgedAt = DateTime.UtcNow.ToString("o") };
        }

        index.Refs = activeRefs;
        var purgedAt = DateTime.UtcNow.ToString("o");

        try
        {
            await CloudSaveHelper.SetCustomDataWithLockAsync(
                _gameApiClient, _context, MailboxConstants.KeyGlobalMailIndexV2, index, writeLock);
        }
        catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex))
        {
            _logger.LogWarning("PurgeExpired: write conflict on index — returning Conflict for admin retry");
            throw new InvalidOperationException(MailboxError.Conflict);
        }

        // Delete per-mail payload keys for expired refs (best-effort — orphaned keys are inert)
        var deleteTasks = new List<Task>();
        foreach (var r in expiredRefs)
        {
            var key = string.Format(MailboxConstants.KeyGlobalMailPayloadFmt, r.MailId);
            deleteTasks.Add(CloudSaveHelper.DeleteCustomDataAsync(_gameApiClient, _context, key));
        }

        try
        {
            await Task.WhenAll(deleteTasks);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PurgeExpired: some payload key deletions failed (non-fatal — refs already removed from index)");
        }

        _logger.LogInformation("PurgeExpired: removed {Count} expired refs by operatorId={OperatorId}", expiredRefs.Count, request.OperatorId);
        return new PurgeExpiredResponse
        {
            Success     = true,
            PurgedCount = expiredRefs.Count,
            PurgedAt    = purgedAt
        };
    }
}
