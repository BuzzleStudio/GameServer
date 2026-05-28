using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

/// <summary>
/// Returns the calling player's personal mailbox with pagination.
/// Also prunes expired mails lazily (in-band) and rewrites the key.
/// </summary>
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
        _logger.LogInformation("GetUserMails called for {PlayerId}, page={Page}, pageSize={PageSize}",
            playerId, request.Page, request.PageSize);

        if (request.Page < 0 || request.PageSize > MailboxConstants.MaxPageSize)
            throw new ArgumentException(MailboxError.InvalidInput);

        var pageSize = request.PageSize <= 0 ? MailboxConstants.DefaultPageSize : request.PageSize;

        var mailbox = await CloudSaveHelper.GetPlayerDataAsync<PlayerUserMailbox>(
            _gameApiClient, _context, playerId, MailboxConstants.KeyUserItems) ?? new PlayerUserMailbox();

        // Lazy expiry prune (in-band) — rewrite only if something was pruned
        var beforeCount = mailbox.Mails.Count;
        mailbox.Mails.RemoveAll(m => m.IsExpired());
        if (mailbox.Mails.Count != beforeCount)
        {
            await CloudSaveHelper.SetPlayerDataAsync(
                _gameApiClient, _context, playerId, MailboxConstants.KeyUserItems, mailbox);
        }

        // Sort newest first
        mailbox.Mails.Sort((a, b) => string.Compare(b.SentAt, a.SentAt, StringComparison.Ordinal));

        var totalCount = mailbox.Mails.Count;
        var startIdx = request.Page * pageSize;
        var slice = new List<MailItemDto>();

        for (var i = startIdx; i < Math.Min(startIdx + pageSize, totalCount); i++)
        {
            var m = mailbox.Mails[i];
            slice.Add(new MailItemDto
            {
                MailId            = m.MailId,
                Subject           = m.Subject,
                Body              = m.Body,
                SentAt            = m.SentAt,
                ExpiresAt         = m.ExpiresAt,
                IsRead            = m.IsRead,
                AttachmentClaimed = m.AttachmentClaimed,
                MailType          = m.MailType,
                MailCategory      = m.MailCategory,
                SenderType        = m.SenderType,
                Sender            = m.Sender,
                Attachments       = m.Attachments
            });
        }

        _logger.LogInformation("GetUserMails returning {Count}/{Total} for {PlayerId}", slice.Count, totalCount, playerId);
        return new PagedMailResponse
        {
            Success    = true,
            Mails      = slice,
            TotalCount = totalCount,
            Page       = request.Page,
            PageSize   = pageSize,
            HasMore    = (startIdx + pageSize) < totalCount
        };
    }
}
