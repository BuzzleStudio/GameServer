using System;
using System.Collections.Generic;
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
        {
            await DeleteGlobalForPlayerWithRetryAsync(playerId, request.MailId);
            return new DeleteMailResponse { MailId = request.MailId };
        }

        await DeleteWithRetryAsync(playerId, request.MailId);
        return new DeleteMailResponse { MailId = request.MailId };
    }

    private async Task DeleteGlobalForPlayerWithRetryAsync(string playerId, string mailId)
    {
        var payload = await CloudSaveHelper.GetCustomDataAsync<GlobalMailPayload>(_gameApiClient, _context, string.Format(MailboxConstants.KeyGlobalMailPayloadFmt, mailId));
        if (payload?.Mail == null)
        {
            var v1Index = await CloudSaveHelper.GetCustomDataAsync<GlobalMailIndex>(_gameApiClient, _context, MailboxConstants.KeyGlobalMailIndex);
            if (v1Index?.Mails.Find(m => m.GlobalMailId == mailId) == null)
                throw new InvalidOperationException(MailboxError.MailNotFound);
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var (state, writeLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerGlobalMailState>(_gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState);
            state ??= new PlayerGlobalMailState();
            state.DeletedIds ??= new List<string>();
            state.ReadIds ??= new List<string>();
            state.ClaimedIds ??= new List<string>();
            if (state.DeletedIds.Contains(mailId)) return;

            state.DeletedIds.Add(mailId);
            state.ReadIds.RemoveAll(id => id == mailId);
            state.ClaimedIds.RemoveAll(id => id == mailId);

            try
            {
                await CloudSaveHelper.SetPlayerDataAsync(_gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState, state, writeLock);
                return;
            }
            catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex) && attempt == 0)
            {
                _logger.LogWarning(ex, "Delete global mail conflict retry for {PlayerId}", playerId);
            }
        }

        throw new InvalidOperationException(MailboxError.Conflict);
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
