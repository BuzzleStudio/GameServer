using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

public class MarkReadModule
{
    private readonly IExecutionContext _context;
    private readonly IGameApiClient _gameApiClient;
    private readonly ILogger<MarkReadModule> _logger;

    public MarkReadModule(IExecutionContext context, IGameApiClient gameApiClient, ILogger<MarkReadModule> logger)
    {
        _context = context;
        _gameApiClient = gameApiClient;
        _logger = logger;
    }

    [CloudCodeFunction("MarkMailRead")]
    public async Task<MarkMailReadResponse> MarkMailReadAsync(MarkMailReadRequest request)
    {
        var playerId = _context.PlayerId ?? string.Empty;
        _logger.LogInformation("MarkMailRead called for {PlayerId}, mailId={MailId}", playerId, request.MailId);
        try
        {
            if (string.IsNullOrWhiteSpace(request.MailId))
                throw new ArgumentException(MailboxError.InvalidInput);

            // Read both sources in parallel, then determine which owns the mail.
            var globalIndexTask = CloudSaveHelper.GetCustomDataAsync<GlobalMailIndex>(
                _gameApiClient, _context, MailboxConstants.KeyGlobalMailIndex);
            var userMailboxTask = CloudSaveHelper.GetPlayerDataAsync<PlayerUserMailbox>(
                _gameApiClient, _context, playerId, MailboxConstants.KeyUserItems);

            await Task.WhenAll(globalIndexTask, userMailboxTask);

            var globalIndex = globalIndexTask.Result ?? new GlobalMailIndex();
            var userMailbox = userMailboxTask.Result ?? new PlayerUserMailbox();

            var globalMail = globalIndex.Mails.Find(m => m.GlobalMailId == request.MailId);
            var userMail   = userMailbox.Mails.Find(m => m.MailId == request.MailId);

            if (globalMail == null && userMail == null)
                throw new InvalidOperationException(MailboxError.MailNotFound);

            if (globalMail != null)
            {
                var state = await CloudSaveHelper.GetPlayerDataAsync<PlayerGlobalMailState>(
                    _gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState)
                    ?? new PlayerGlobalMailState();

                if (!state.ReadIds.Contains(request.MailId))
                {
                    state.ReadIds.Add(request.MailId);
                    await CloudSaveHelper.SetPlayerDataAsync(
                        _gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState, state);
                }
            }
            else
            {
                if (!userMail!.IsRead)
                {
                    userMail.IsRead = true;
                    await CloudSaveHelper.SetPlayerDataAsync(
                        _gameApiClient, _context, playerId, MailboxConstants.KeyUserItems, userMailbox);
                }
            }

            _logger.LogInformation("MarkMailRead success for mailId={MailId}", request.MailId);
            return new MarkMailReadResponse { Success = true, MailId = request.MailId, IsRead = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MarkMailRead failed for {PlayerId}, mailId={MailId}", playerId, request.MailId);
            throw;
        }
    }
}
