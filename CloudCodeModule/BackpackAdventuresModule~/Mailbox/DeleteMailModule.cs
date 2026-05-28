using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

/// <summary>
/// Any authenticated player can delete their own user mail.
/// Rejects deletion of: unclaimed reward mails, global mails (not player-owned).
/// WriteLock on mailbox_user_items; retry once on conflict.
/// </summary>
public class DeleteMailModule
{
    private readonly IExecutionContext _context;
    private readonly IGameApiClient _gameApiClient;
    private readonly ILogger<DeleteMailModule> _logger;

    public DeleteMailModule(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        ILogger<DeleteMailModule> logger)
    {
        _context = context;
        _gameApiClient = gameApiClient;
        _logger = logger;
    }

    [CloudCodeFunction("DeleteMail")]
    public async Task<DeleteMailResponse> DeleteMailAsync(DeleteMailRequest request)
    {
        var playerId = _context.PlayerId ?? string.Empty;
        _logger.LogInformation("DeleteMail called for {PlayerId}, mailId={MailId}", playerId, request.MailId);

        if (string.IsNullOrWhiteSpace(request.MailId))
            throw new ArgumentException(MailboxError.InvalidInput);

        // Reject deletion of global mails (identified by gm_ prefix convention)
        if (request.MailId.StartsWith("gm_", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(MailboxError.CannotDeleteGlobal);

        await DeleteWithRetryAsync(playerId, request.MailId);

        _logger.LogInformation("DeleteMail success for mailId={MailId} by {PlayerId}", request.MailId, playerId);
        return new DeleteMailResponse { Success = true, MailId = request.MailId };
    }

    private async Task DeleteWithRetryAsync(string playerId, string mailId)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var (mailbox, writeLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerUserMailbox>(
                _gameApiClient, _context, playerId, MailboxConstants.KeyUserItems);
            mailbox ??= new PlayerUserMailbox();

            var mail = mailbox.Mails.Find(m => m.MailId == mailId);
            if (mail == null) throw new InvalidOperationException(MailboxError.MailNotFound);

            // Reject deletion of unclaimed reward mail
            if (!mail.AttachmentClaimed && mail.Attachments != null && mail.Attachments.Count > 0)
                throw new InvalidOperationException(MailboxError.CannotDeleteUnclaimedReward);

            mailbox.Mails.Remove(mail);

            try
            {
                await CloudSaveHelper.SetPlayerDataAsync(
                    _gameApiClient, _context, playerId, MailboxConstants.KeyUserItems, mailbox, writeLock);
                return;
            }
            catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex) && attempt == 0)
            {
                _logger.LogWarning("DeleteMail: write conflict — retrying for {PlayerId}, mailId={MailId}", playerId, mailId);
            }
        }

        _logger.LogError("DeleteMail: write conflict on retry for {PlayerId}, mailId={MailId}", playerId, mailId);
        throw new InvalidOperationException(MailboxError.Conflict);
    }
}
