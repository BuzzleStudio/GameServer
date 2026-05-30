using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

public class GetUserMailsModule
{
    private readonly IExecutionContext _context;
    private readonly IGameApiClient _gameApiClient;
    private readonly ILogger<GetUserMailsModule> _logger;

    public GetUserMailsModule(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        ILogger<GetUserMailsModule> logger)
    {
        _context = context;
        _gameApiClient = gameApiClient;
        _logger = logger;
    }

    [CloudCodeFunction("GetUserMails")]
    public async Task<PagedMailResponse> GetUserMailsAsync(GetMailsRequest request)
    {
        var playerId = _context.PlayerId ?? string.Empty;
        _logger.LogInformation("GetUserMails called for {PlayerId}, page={Page}, pageSize={PageSize}", playerId, request.Page, request.PageSize);

        if (request.Page < 0 || request.PageSize > MailboxConstants.MaxPageSize)
            throw new ArgumentException(MailboxError.InvalidInput);

        var pageSize = request.PageSize <= 0 ? MailboxConstants.DefaultPageSize : request.PageSize;
        var mailbox = await CloudSaveHelper.GetPlayerDataAsync<PlayerUserMailbox>(
            _gameApiClient, _context, playerId, MailboxConstants.KeyUserItems) ?? new PlayerUserMailbox();

        var beforeCount = mailbox.Mails.Count;
        mailbox.Mails.RemoveAll(m => m.IsExpired());
        if (mailbox.Mails.Count != beforeCount)
        {
            await CloudSaveHelper.SetPlayerDataAsync(_gameApiClient, _context, playerId, MailboxConstants.KeyUserItems, mailbox);
        }

        mailbox.Mails.Sort((a, b) => string.Compare(b.MailInfo.StartTime, a.MailInfo.StartTime, StringComparison.Ordinal));

        var totalCount = mailbox.Mails.Count;
        var startIdx = request.Page * pageSize;
        var slice = new List<MailItemDto>();
        for (var i = startIdx; i < Math.Min(startIdx + pageSize, totalCount); i++)
            slice.Add(mailbox.Mails[i]);

        return new PagedMailResponse
        {
            Mails = slice,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = pageSize,
            HasMore = (startIdx + pageSize) < totalCount
        };
    }
}
