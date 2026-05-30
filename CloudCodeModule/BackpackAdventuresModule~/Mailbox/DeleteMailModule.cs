using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

public class DeleteMailModule
{
    private readonly IExecutionContext _context;
    private readonly IGameApiClient _gameApiClient;
    private readonly ILogger<DeleteMailModule> _logger;

    public DeleteMailModule(IExecutionContext context, IGameApiClient gameApiClient, ILogger<DeleteMailModule> logger)
    {
        _context = context;
        _gameApiClient = gameApiClient;
        _logger = logger;
    }

    [CloudCodeFunction("DeleteMail")]
    public async Task<DeleteMailResponse> DeleteMailAsync(DeleteMailRequest request)
    {
        var playerId = _context.PlayerId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(request.MailId))
            throw new ArgumentException(MailboxError.InvalidInput);
        if (request.MailId.StartsWith("gm_", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(MailboxError.CannotDeleteGlobal);

        await DeleteWithRetryAsync(playerId, request.MailId);
        return new DeleteMailResponse { MailId = request.MailId };
    }

    private async Task DeleteWithRetryAsync(string playerId, string mailId)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var (mailbox, writeLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerUserMailbox>(_gameApiClient, _context, playerId, MailboxConstants.KeyUserItems);
            mailbox ??= new PlayerUserMailbox();

            var mail = mailbox.Mails.Find(m => m.MessageId == mailId);
            if (mail == null) throw new InvalidOperationException(MailboxError.MailNotFound);
            if (!mail.MailMetaData.IsClaimed && MailSchemaHelper.HasAttachments(mail))
                throw new InvalidOperationException(MailboxError.CannotDeleteUnclaimedReward);

            mailbox.Mails.Remove(mail);
            try
            {
                await CloudSaveHelper.SetPlayerDataAsync(_gameApiClient, _context, playerId, MailboxConstants.KeyUserItems, mailbox, writeLock);
                return;
            }
            catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex) && attempt == 0)
            {
                _logger.LogWarning(ex, "DeleteMail conflict retry for {PlayerId}", playerId);
            }
        }

        throw new InvalidOperationException(MailboxError.Conflict);
    }
}
