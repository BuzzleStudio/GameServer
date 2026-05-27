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

    [CloudCodeFunction("GetMailbox")]
    public async Task<GetMailboxResponse> GetMailboxAsync()
    {
        var playerId = _context.PlayerId ?? string.Empty;
        _logger.LogInformation("GetMailbox called for {PlayerId}", playerId);
        try
        {
            var globalIndexTask = CloudSaveHelper.GetCustomDataAsync<GlobalMailIndex>(
                _gameApiClient, _context, MailboxConstants.KeyGlobalMailIndex);
            var userMailboxTask = CloudSaveHelper.GetPlayerDataAsync<PlayerUserMailbox>(
                _gameApiClient, _context, playerId, MailboxConstants.KeyUserItems);
            var globalStateTask = CloudSaveHelper.GetPlayerDataAsync<PlayerGlobalMailState>(
                _gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState);

            await Task.WhenAll(globalIndexTask, userMailboxTask, globalStateTask);

            var globalIndex = globalIndexTask.Result ?? new GlobalMailIndex();
            var userMailbox = userMailboxTask.Result ?? new PlayerUserMailbox();
            var globalState = globalStateTask.Result ?? new PlayerGlobalMailState();

            var claimedGlobal = new HashSet<string>(globalState.ClaimedIds);
            var readGlobal = new HashSet<string>(globalState.ReadIds);

            var mails = new List<MailItemDto>();

            foreach (var m in globalIndex.Mails)
            {
                if (m.IsExpired()) continue;
                mails.Add(new MailItemDto
                {
                    MailId = m.GlobalMailId,
                    Subject = m.Subject,
                    Body = m.Body,
                    SentAt = m.SentAt,
                    ExpiresAt = m.ExpiresAt,
                    IsRead = readGlobal.Contains(m.GlobalMailId),
                    AttachmentClaimed = claimedGlobal.Contains(m.GlobalMailId),
                    Attachments = m.Attachments
                });
            }

            foreach (var m in userMailbox.Mails)
            {
                if (m.IsExpired()) continue;
                mails.Add(new MailItemDto
                {
                    MailId = m.MailId,
                    Subject = m.Subject,
                    Body = m.Body,
                    SentAt = m.SentAt,
                    ExpiresAt = m.ExpiresAt,
                    IsRead = m.IsRead,
                    AttachmentClaimed = m.AttachmentClaimed,
                    Attachments = m.Attachments
                });
            }

            mails.Sort((a, b) => string.Compare(b.SentAt, a.SentAt, StringComparison.Ordinal));

            _logger.LogInformation("GetMailbox returning {Count} mails for {PlayerId}", mails.Count, playerId);
            return new GetMailboxResponse { Success = true, Mails = mails };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetMailbox failed for {PlayerId}", playerId);
            throw;
        }
    }
}
