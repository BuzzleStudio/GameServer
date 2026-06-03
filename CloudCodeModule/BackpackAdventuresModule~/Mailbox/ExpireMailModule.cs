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
    public async Task<ApiResponse<ExpireMailResponse>> ExpireMailAsync(ExpireMailRequest request)
    {
        await AdminAuth.RequireAdminToolAsync(_gameApiClient, _context, request.AdminToken, request.OperatorId, _logger);
        if (string.IsNullOrWhiteSpace(request.MailId))
            throw new ArgumentException(MailboxError.InvalidInput);
        if (!request.MailId.StartsWith("gm_", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(MailboxError.CannotExpireUserMail);

        var (collection, writeLock) = await CloudSaveHelper.GetCustomDataWithLockAsync<GlobalMailCollection>(_gameApiClient, _context, MailboxConstants.KeyMailsAll);
        collection ??= new GlobalMailCollection();
        var payload = GlobalMailStore.FindById(collection.Mails, request.MailId);
        if (payload?.Mail == null)
            throw new InvalidOperationException(MailboxError.MailNotFound);

        var expiredAt = DateTime.UtcNow.ToString("o");
        payload.Mail.EndTime = DateTime.UtcNow;

        try
        {
            await CloudSaveHelper.SetCustomDataWithLockAsync(_gameApiClient, _context, MailboxConstants.KeyMailsAll, collection, writeLock, _logger);
        }
        catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex))
        {
            _logger.LogWarning(ex, "ExpireMail conflict for {MailId}", request.MailId);
            throw new InvalidOperationException(MailboxError.Conflict);
        }

        return ApiResponse<ExpireMailResponse>.Ok(new ExpireMailResponse { MailId = request.MailId, ExpiredAt = expiredAt });
    }

    [CloudCodeFunction("SetMailEndTime")]
    public async Task<ApiResponse<SetMailEndTimeResponse>> SetMailEndTimeAsync(SetMailEndTimeRequest request)
    {
        await AdminAuth.RequireAdminToolAsync(_gameApiClient, _context, request.AdminToken, request.OperatorId, _logger);
        if (string.IsNullOrWhiteSpace(request.MailId))
            throw new ArgumentException(MailboxError.InvalidInput);
        if (!request.MailId.StartsWith("gm_", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(MailboxError.CannotExpireUserMail);

        var endTime = ResolveEndTime(request.EndTime);
        var endTimeIso = endTime?.ToString("o");
        var (collection, writeLock) = await CloudSaveHelper.GetCustomDataWithLockAsync<GlobalMailCollection>(_gameApiClient, _context, MailboxConstants.KeyMailsAll);
        collection ??= new GlobalMailCollection();
        var payload = GlobalMailStore.FindById(collection.Mails, request.MailId);
        if (payload?.Mail == null)
            throw new InvalidOperationException(MailboxError.MailNotFound);

        payload.Mail.EndTime = endTime;

        try
        {
            await CloudSaveHelper.SetCustomDataWithLockAsync(_gameApiClient, _context, MailboxConstants.KeyMailsAll, collection, writeLock, _logger);
        }
        catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex))
        {
            _logger.LogWarning(ex, "SetMailEndTime conflict for {MailId}", request.MailId);
            throw new InvalidOperationException(MailboxError.Conflict);
        }

        return ApiResponse<SetMailEndTimeResponse>.Ok(new SetMailEndTimeResponse { MailId = request.MailId, EndTime = endTimeIso });
    }

    [CloudCodeFunction("DeleteGlobalMail")]
    public async Task<ApiResponse<DeleteMailResponse>> DeleteGlobalMailAsync(AdminDeleteMailRequest request)
    {
        await AdminAuth.RequireAdminToolAsync(_gameApiClient, _context, request.AdminToken, request.OperatorId, _logger);
        if (string.IsNullOrWhiteSpace(request.MailId))
            throw new ArgumentException(MailboxError.InvalidInput);
        if (!request.MailId.StartsWith("gm_", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(MailboxError.InvalidInput);

        var (collection, writeLock) = await CloudSaveHelper.GetCustomDataWithLockAsync<GlobalMailCollection>(_gameApiClient, _context, MailboxConstants.KeyMailsAll);
        collection ??= new GlobalMailCollection();
        var removed = collection.Mails.RemoveAll(m => string.Equals(m.Mail?.MessageId, request.MailId, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
            throw new InvalidOperationException(MailboxError.MailNotFound);

        try
        {
            await CloudSaveHelper.SetCustomDataWithLockAsync(_gameApiClient, _context, MailboxConstants.KeyMailsAll, collection, writeLock, _logger);
        }
        catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex))
        {
            _logger.LogWarning(ex, "DeleteGlobalMail conflict for {MailId}", request.MailId);
            throw new InvalidOperationException(MailboxError.Conflict);
        }

        return ApiResponse<DeleteMailResponse>.Ok(new DeleteMailResponse { MailId = request.MailId });
    }

    private static DateTime? ResolveEndTime(string? endTime)
    {
        if (string.IsNullOrWhiteSpace(endTime))
            return null;
        if (DateTime.TryParse(endTime, out var parsed))
            return parsed.ToUniversalTime();
        throw new ArgumentException(MailboxError.InvalidInput);
    }
}
