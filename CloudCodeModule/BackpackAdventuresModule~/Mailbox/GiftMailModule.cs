using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

/// <summary>
/// Any authenticated player can gift a notification mail to another player.
/// No attachment items (notification-only in this iteration per §5.3).
/// Daily quota: 5 gifts per sender per UTC day.
/// </summary>
public class GiftMailModule
{
    private readonly IExecutionContext _context;
    private readonly IGameApiClient _gameApiClient;
    private readonly ILogger<GiftMailModule> _logger;

    public GiftMailModule(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        ILogger<GiftMailModule> logger)
    {
        _context = context;
        _gameApiClient = gameApiClient;
        _logger = logger;
    }

    [CloudCodeFunction("GiftMail")]
    public async Task<GiftMailResponse> GiftMailAsync(GiftMailRequest request)
    {
        var senderId = _context.PlayerId ?? string.Empty;
        _logger.LogInformation("GiftMail from {SenderId} to {TargetPlayerId}", senderId, request.TargetPlayerId);

        if (string.IsNullOrWhiteSpace(request.TargetPlayerId))
            throw new ArgumentException(MailboxError.InvalidInput);

        if (senderId == request.TargetPlayerId)
            throw new ArgumentException(MailboxError.InvalidInput);

        if (string.IsNullOrWhiteSpace(request.Subject) || request.Subject.Length > MailboxConstants.MaxSubjectLength)
            throw new ArgumentException(MailboxError.InvalidInput);

        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > MailboxConstants.MaxBodyLength)
            throw new ArgumentException(MailboxError.InvalidInput);

        // Check and update gift quota for sender
        var (senderMeta, senderMetaLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerMailboxMeta>(
            _gameApiClient, _context, senderId, MailboxConstants.KeyMeta);
        senderMeta ??= new PlayerMailboxMeta();

        ResetGiftQuotaIfNewDay(senderMeta);

        if (senderMeta.GiftsSentToday >= MailboxConstants.MaxGiftsPerDay)
        {
            _logger.LogWarning("Gift quota exceeded for sender {SenderId}", senderId);
            throw new InvalidOperationException(MailboxError.GiftQuotaExceeded);
        }

        var sentAt = DateTime.UtcNow.ToString("o");
        var mailId = "gf_" + Guid.NewGuid().ToString("N")[..8];

        var newMail = new UserMailItem
        {
            MailId       = mailId,
            Subject      = request.Subject,
            Body         = request.Body,
            SentAt       = sentAt,
            ExpiresAt    = null,
            MailType     = MailType.Notification,
            MailCategory = MailCategory.Gift,
            SenderType   = SenderType.Player,
            Sender       = senderId,
            Attachments  = null
        };

        // Insert into target mailbox with writeLock + eviction
        await InsertIntoTargetMailboxAsync(request.TargetPlayerId, newMail);

        // Increment gift counter for sender
        senderMeta.GiftsSentToday++;
        try
        {
            await CloudSaveHelper.SetPlayerDataAsync(
                _gameApiClient, _context, senderId, MailboxConstants.KeyMeta, senderMeta, senderMetaLock);
        }
        catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex))
        {
            // Best-effort — gift was delivered; meta counter update failed. Log and continue.
            _logger.LogWarning("GiftMail: write conflict on sender meta for {SenderId} — quota counter may be stale", senderId);
        }

        _logger.LogInformation("Gift mail {MailId} delivered from {SenderId} to {TargetPlayerId}", mailId, senderId, request.TargetPlayerId);
        return new GiftMailResponse { MailId = mailId, SentAt = sentAt };
    }

    private async Task InsertIntoTargetMailboxAsync(string targetPlayerId, UserMailItem newMail)
    {
        var (mailbox, writeLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerUserMailbox>(
            _gameApiClient, _context, targetPlayerId, MailboxConstants.KeyUserItems);
        mailbox ??= new PlayerUserMailbox();

        try
        {
            MailboxEviction.Evict(mailbox, targetPlayerId, _logger);
        }
        catch (InvalidOperationException ex) when (ex.Message == MailboxError.MailboxFull)
        {
            throw new InvalidOperationException(MailboxError.TargetMailboxFull);
        }

        mailbox.Mails.Add(newMail);

        try
        {
            await CloudSaveHelper.SetPlayerDataAsync(
                _gameApiClient, _context, targetPlayerId, MailboxConstants.KeyUserItems, mailbox, writeLock);
        }
        catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex))
        {
            _logger.LogWarning("GiftMail: write conflict on target mailbox — retrying once for {TargetPlayerId}", targetPlayerId);
            var (freshMailbox, freshLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerUserMailbox>(
                _gameApiClient, _context, targetPlayerId, MailboxConstants.KeyUserItems);
            freshMailbox ??= new PlayerUserMailbox();

            try
            {
                MailboxEviction.Evict(freshMailbox, targetPlayerId, _logger);
            }
            catch (InvalidOperationException evEx) when (evEx.Message == MailboxError.MailboxFull)
            {
                throw new InvalidOperationException(MailboxError.TargetMailboxFull);
            }

            freshMailbox.Mails.Add(newMail);

            try
            {
                await CloudSaveHelper.SetPlayerDataAsync(
                    _gameApiClient, _context, targetPlayerId, MailboxConstants.KeyUserItems, freshMailbox, freshLock);
            }
            catch (Exception retryEx) when (CloudSaveHelper.IsWriteLockConflict(retryEx))
            {
                _logger.LogError("GiftMail: write conflict on retry for {TargetPlayerId}", targetPlayerId);
                throw new InvalidOperationException(MailboxError.Conflict);
            }
        }
    }

    private static void ResetGiftQuotaIfNewDay(PlayerMailboxMeta meta)
    {
        var utcNow = DateTime.UtcNow;
        var todayMidnight = utcNow.Date; // UTC midnight

        if (string.IsNullOrEmpty(meta.LastGiftResetAt) ||
            !DateTime.TryParse(meta.LastGiftResetAt, out var lastReset) ||
            lastReset < todayMidnight)
        {
            meta.GiftsSentToday = 0;
            meta.LastGiftResetAt = todayMidnight.ToString("o");
        }
    }
}

