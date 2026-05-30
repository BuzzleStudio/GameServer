using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

public class PurgeExpiredModule
{
    private readonly IExecutionContext _context;
    private readonly IGameApiClient _gameApiClient;
    private readonly ILogger<PurgeExpiredModule> _logger;

    public PurgeExpiredModule(IExecutionContext context, IGameApiClient gameApiClient, ILogger<PurgeExpiredModule> logger)
    {
        _context = context;
        _gameApiClient = gameApiClient;
        _logger = logger;
    }

    [CloudCodeFunction("PurgeExpired")]
    public async Task<PurgeExpiredResponse> PurgeExpiredAsync(PurgeExpiredRequest request)
    {
        await AdminAuth.RequireAdminToolAsync(_gameApiClient, _context, request.AdminToken, request.OperatorId, _logger);
        var (index, writeLock) = await CloudSaveHelper.GetCustomDataWithLockAsync<GlobalMailIndexV2>(_gameApiClient, _context, MailboxConstants.KeyGlobalMailIndexV2);

        if (index == null || index.Refs.Count == 0)
            return new PurgeExpiredResponse { PurgedCount = 0, PurgedAt = DateTime.UtcNow.ToString("o") };

        var expiredRefs = new List<GlobalMailRef>();
        var activeRefs = new List<GlobalMailRef>();
        foreach (var mailRef in index.Refs)
        {
            if (mailRef.IsExpired()) expiredRefs.Add(mailRef);
            else activeRefs.Add(mailRef);
        }

        if (expiredRefs.Count == 0)
            return new PurgeExpiredResponse { PurgedCount = 0, PurgedAt = DateTime.UtcNow.ToString("o") };

        index.Refs = activeRefs;
        var purgedAt = DateTime.UtcNow.ToString("o");
        await CloudSaveHelper.SetCustomDataWithLockAsync(_gameApiClient, _context, MailboxConstants.KeyGlobalMailIndexV2, index, writeLock);

        var deleteTasks = new List<Task>();
        foreach (var mailRef in expiredRefs)
            deleteTasks.Add(CloudSaveHelper.DeleteCustomDataAsync(_gameApiClient, _context, string.Format(MailboxConstants.KeyGlobalMailPayloadFmt, mailRef.MessageId)));

        try
        {
            await Task.WhenAll(deleteTasks);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PurgeExpired payload delete had failures");
        }

        return new PurgeExpiredResponse { PurgedCount = expiredRefs.Count, PurgedAt = purgedAt };
    }
}
