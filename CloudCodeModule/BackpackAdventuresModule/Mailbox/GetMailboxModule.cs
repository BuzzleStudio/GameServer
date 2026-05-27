using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

public class GetMailboxModule
{
    private readonly IExecutionContext _context;
    private readonly IGameApiClient _gameApiClient;
    private readonly ILogger<GetMailboxModule> _logger;

    public GetMailboxModule(IExecutionContext context, IGameApiClient gameApiClient, ILogger<GetMailboxModule> logger)
    {
        _context = context;
        _gameApiClient = gameApiClient;
        _logger = logger;
    }

    /// <summary>Returns the calling player's mailbox: merged global + user-specific mails with read/claimed state applied and expired mails filtered out.</summary>
    [CloudCodeFunction("GetMailbox")]
    public async Task<GetMailboxResponse> GetMailboxAsync(GetMailboxRequest request)
    {
        var playerId = _context.PlayerId ?? string.Empty;
        _logger.LogInformation("GetMailbox called for {PlayerId}", playerId);
        try
        {
            var page = Math.Max(1, request.Page);
            var pageSize = Math.Clamp(request.PageSize <= 0 ? MailboxConstants.DefaultPageSize : request.PageSize, 1, MailboxConstants.MaxPageSize);

            var globalIndexTask = CloudSaveHelper.GetCustomDataAsync<GlobalMailIndex>(
                _gameApiClient, _context, MailboxConstants.KeyGlobalMailIndex);
            var userMailboxTask = CloudSaveHelper.GetPlayerDataAsync<PlayerUserMailbox>(
                _gameApiClient, _context, playerId, MailboxConstants.KeyUserItems);
            var globalStateTask = CloudSaveHelper.GetPlayerDataAsync<PlayerGlobalMailState>(
                _gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState);
            var metaTask = CloudSaveHelper.GetPlayerDataAsync<PlayerMailboxMeta>(
                _gameApiClient, _context, playerId, MailboxConstants.KeyMeta);

            await Task.WhenAll(globalIndexTask, userMailboxTask, globalStateTask, metaTask);

            var globalIndex = globalIndexTask.Result ?? new GlobalMailIndex();
            var userMailbox = userMailboxTask.Result ?? new PlayerUserMailbox();
            var globalState = globalStateTask.Result ?? new PlayerGlobalMailState();
            var meta = metaTask.Result ?? new PlayerMailboxMeta();

            var claimedGlobal = new HashSet<string>(globalState.ClaimedIds);
            var readGlobal = new HashSet<string>(globalState.ReadIds);

            var items = new List<MailItemDto>();

            foreach (var m in globalIndex.Mails)
            {
                if (m.IsExpired()) continue;
                var isRead = readGlobal.Contains(m.GlobalMailId) ||
                    (meta.LastReadAt != null && string.Compare(m.SentAt, meta.LastReadAt, StringComparison.Ordinal) <= 0);
                items.Add(new MailItemDto
                {
                    MailId = m.GlobalMailId,
                    MailType = "global",
                    Title = m.Title,
                    Body = m.Body,
                    SentAt = m.SentAt,
                    ExpiresAt = m.ExpiresAt,
                    Read = isRead,
                    Claimed = claimedGlobal.Contains(m.GlobalMailId),
                    Attachment = m.Attachment
                });
            }

            foreach (var m in userMailbox.Mails)
            {
                if (m.IsExpired()) continue;
                items.Add(new MailItemDto
                {
                    MailId = m.MailId,
                    MailType = "user",
                    Title = m.Title,
                    Body = m.Body,
                    SentAt = m.SentAt,
                    ExpiresAt = m.ExpiresAt,
                    Read = m.Read,
                    Claimed = m.Claimed,
                    Attachment = m.Attachment
                });
            }

            items.Sort((a, b) => string.Compare(b.SentAt, a.SentAt, StringComparison.Ordinal));

            var totalCount = items.Count;
            var offset = (page - 1) * pageSize;
            var paged = offset < items.Count
                ? items.GetRange(offset, Math.Min(pageSize, items.Count - offset))
                : new List<MailItemDto>();

            _logger.LogInformation("GetMailbox returned {Count}/{Total} items for {PlayerId}", paged.Count, totalCount, playerId);
            return new GetMailboxResponse
            {
                Success = true,
                Mails = paged,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetMailbox failed for {PlayerId}", playerId);
            throw;
        }
    }
}

public class GetMailboxRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = MailboxConstants.DefaultPageSize;
}

public class GetMailboxResponse
{
    public bool Success { get; set; }
    public List<MailItemDto> Mails { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
