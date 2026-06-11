using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

/// <summary>
/// Admin endpoint: read another player's personal mailbox (mailbox_user_items).
/// Must be called from a service-account context; player-authenticated calls are rejected.
/// Read-only — no writes (no expiry pruning).
/// </summary>
public class GetUserMailsAdminModule
{
    private readonly IExecutionContext _context;
    private readonly IGameApiClient _gameApiClient;
    private readonly ILogger<GetUserMailsAdminModule> _logger;

    public GetUserMailsAdminModule(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        ILogger<GetUserMailsAdminModule> logger)
    {
        _context = context;
        _gameApiClient = gameApiClient;
        _logger = logger;
    }

    [CloudCodeFunction("GetUserMailsAdmin")]
    public async Task<ApiResponse<PagedMailResponse>> GetUserMailsAdminAsync(GetUserMailsAdminRequest request)
    {
        await AdminAuth.RequireAdminToolAsync(
            _gameApiClient, _context,
            request.AdminToken,
            request.OperatorId,
            _logger);

        if (string.IsNullOrWhiteSpace(request.TargetPlayerId))
            throw new ArgumentException(MailboxError.InvalidInput);

        if (request.Page < 0 || request.PageSize > MailboxConstants.MaxPageSize)
            throw new ArgumentException(MailboxError.InvalidInput);

        var pageSize = request.PageSize <= 0 ? MailboxConstants.DefaultPageSize : request.PageSize;

        _logger.LogInformation(
            "GetUserMailsAdmin called for targetPlayer={TargetPlayerId}, page={Page}, pageSize={PageSize}, operatorId={OperatorId}",
            request.TargetPlayerId, request.Page, pageSize, request.OperatorId);

        // Read-only: no expiry pruning write so the caller cannot mutate player data.
        var mailbox = await CloudSaveHelper.GetPlayerDataAsync<PlayerUserMailbox>(
            _gameApiClient, _context, request.TargetPlayerId, MailboxConstants.KeyUserItems)
            ?? new PlayerUserMailbox();

        mailbox.Mails.Sort((a, b) =>
            string.Compare(b.MailInfo.StartTime, a.MailInfo.StartTime, StringComparison.Ordinal));

        var totalCount = mailbox.Mails.Count;
        var startIdx = request.Page * pageSize;
        var slice = new List<MailItemDto>();
        for (var i = startIdx; i < Math.Min(startIdx + pageSize, totalCount); i++)
            slice.Add(mailbox.Mails[i]);

        return ApiResponse<PagedMailResponse>.Ok(new PagedMailResponse
        {
            Mails = slice,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = pageSize,
            HasMore = (startIdx + pageSize) < totalCount
        });
    }
}
