using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

/// <summary>
/// Admin-only endpoint: marks a global mail as expired by updating its v2 index ref.
/// PurgeExpired can later remove the ref and payload key.
/// </summary>
public class ExpireMailModule
{
    private readonly IExecutionContext _context;
    private readonly IGameApiClient _gameApiClient;
    private readonly ILogger<ExpireMailModule> _logger;

    public ExpireMailModule(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        ILogger<ExpireMailModule> logger)
    {
        _context = context;
        _gameApiClient = gameApiClient;
        _logger = logger;
    }

    [CloudCodeFunction("ExpireMail")]
    public async Task<ExpireMailResponse> ExpireMailAsync(ExpireMailRequest request)
    {
        _logger.LogInformation("ExpireMail called by operatorId={OperatorId}, mailId={MailId}",
            request.OperatorId, request.MailId);

        await AdminAuth.RequireAdminToolAsync(
            _gameApiClient, _context, request.AdminToken, request.OperatorId, _logger);

        if (string.IsNullOrWhiteSpace(request.MailId))
            throw new ArgumentException(MailboxError.InvalidInput);

        if (!request.MailId.StartsWith("gm_", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(MailboxError.CannotExpireUserMail);

        var (index, writeLock) = await CloudSaveHelper.GetCustomDataWithLockAsync<GlobalMailIndexV2>(
            _gameApiClient, _context, MailboxConstants.KeyGlobalMailIndexV2);

        if (index == null || index.Refs.Count == 0)
            throw new InvalidOperationException(MailboxError.MailNotFound);

        var mailRef = index.Refs.Find(r =>
            string.Equals(r.MailId, request.MailId, StringComparison.OrdinalIgnoreCase));
        if (mailRef == null)
            throw new InvalidOperationException(MailboxError.MailNotFound);

        var expiredAt = DateTime.UtcNow.ToString("o");
        mailRef.ExpiresAt = expiredAt;

        try
        {
            await CloudSaveHelper.SetCustomDataWithLockAsync(
                _gameApiClient, _context, MailboxConstants.KeyGlobalMailIndexV2, index, writeLock);
        }
        catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex))
        {
            _logger.LogWarning("ExpireMail: write conflict on index for mailId={MailId}", request.MailId);
            throw new InvalidOperationException(MailboxError.Conflict);
        }

        _logger.LogInformation("ExpireMail success for mailId={MailId} by operatorId={OperatorId}",
            request.MailId, request.OperatorId);

        return new ExpireMailResponse
        {
            MailId = request.MailId,
            ExpiredAt = expiredAt
        };
    }
}

