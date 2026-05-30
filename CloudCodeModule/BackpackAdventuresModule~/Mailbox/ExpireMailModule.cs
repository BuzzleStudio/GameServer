using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

public class ExpireMailModule
{
    private readonly IExecutionContext _context;
    private readonly IGameApiClient _gameApiClient;
    private readonly ILogger<ExpireMailModule> _logger;

    public ExpireMailModule(IExecutionContext context, IGameApiClient gameApiClient, ILogger<ExpireMailModule> logger)
    {
        _context = context;
        _gameApiClient = gameApiClient;
        _logger = logger;
    }

    [CloudCodeFunction("ExpireMail")]
    public async Task<ExpireMailResponse> ExpireMailAsync(ExpireMailRequest request)
    {
        await AdminAuth.RequireAdminToolAsync(_gameApiClient, _context, request.AdminToken, request.OperatorId, _logger);
        if (string.IsNullOrWhiteSpace(request.MailId))
            throw new ArgumentException(MailboxError.InvalidInput);
        if (!request.MailId.StartsWith("gm_", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(MailboxError.CannotExpireUserMail);

        var (index, writeLock) = await CloudSaveHelper.GetCustomDataWithLockAsync<GlobalMailIndexV2>(_gameApiClient, _context, MailboxConstants.KeyGlobalMailIndexV2);
        if (index == null || index.Refs.Count == 0)
            throw new InvalidOperationException(MailboxError.MailNotFound);

        var mailRef = index.Refs.Find(r => string.Equals(r.MessageId, request.MailId, StringComparison.OrdinalIgnoreCase));
        if (mailRef == null)
            throw new InvalidOperationException(MailboxError.MailNotFound);

        var payloadKey = string.Format(MailboxConstants.KeyGlobalMailPayloadFmt, request.MailId);
        var payload = await CloudSaveHelper.GetCustomDataAsync<GlobalMailPayload>(_gameApiClient, _context, payloadKey);
        if (payload?.Mail == null)
            throw new InvalidOperationException(MailboxError.MailNotFound);

        var expiredAt = DateTime.UtcNow.ToString("o");
        mailRef.ExpireTime = expiredAt;
        payload.Mail.MailInfo.ExpireTime = expiredAt;
        payload.Mail.MailInfo.Period = MailSchemaHelper.CalculatePeriodSeconds(payload.Mail.MailInfo.StartTime, expiredAt);

        try
        {
            await CloudSaveHelper.SetCustomDataAsync(_gameApiClient, _context, payloadKey, payload);
            await CloudSaveHelper.SetCustomDataWithLockAsync(_gameApiClient, _context, MailboxConstants.KeyGlobalMailIndexV2, index, writeLock);
        }
        catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex))
        {
            _logger.LogWarning(ex, "ExpireMail conflict for {MailId}", request.MailId);
            throw new InvalidOperationException(MailboxError.Conflict);
        }

        return new ExpireMailResponse { MailId = request.MailId, ExpiredAt = expiredAt };
    }
}
