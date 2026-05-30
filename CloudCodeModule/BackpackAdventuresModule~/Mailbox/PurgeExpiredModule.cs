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
        var (mails, writeLock) = await CloudSaveHelper.GetCustomDataWithLockAsync<List<GlobalMailPayload>>(_gameApiClient, _context, MailboxConstants.KeyMailsAll);
        mails ??= new List<GlobalMailPayload>();

        if (mails.Count == 0)
            return new PurgeExpiredResponse { PurgedCount = 0, PurgedAt = DateTime.UtcNow.ToString("o") };

        var purgedCount = GlobalMailStore.RemoveExpired(mails);
        if (purgedCount == 0)
            return new PurgeExpiredResponse { PurgedCount = 0, PurgedAt = DateTime.UtcNow.ToString("o") };

        var purgedAt = DateTime.UtcNow.ToString("o");
        await CloudSaveHelper.SetCustomDataWithLockAsync(_gameApiClient, _context, MailboxConstants.KeyMailsAll, mails, writeLock);

        return new PurgeExpiredResponse { PurgedCount = purgedCount, PurgedAt = purgedAt };
    }
}
