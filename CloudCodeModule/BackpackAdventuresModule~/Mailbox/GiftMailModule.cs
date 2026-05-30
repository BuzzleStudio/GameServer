using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

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
        if (string.IsNullOrWhiteSpace(request.TargetPlayerId) || senderId == request.TargetPlayerId)
            throw new ArgumentException(MailboxError.InvalidInput);
        if (string.IsNullOrWhiteSpace(request.Subject) || request.Subject.Length > MailboxConstants.MaxSubjectLength)
            throw new ArgumentException(MailboxError.InvalidInput);
        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > MailboxConstants.MaxBodyLength)
            throw new ArgumentException(MailboxError.InvalidInput);

        var (senderMeta, senderMetaLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerMailboxMeta>(_gameApiClient, _context, senderId, MailboxConstants.KeyMeta);
        senderMeta ??= new PlayerMailboxMeta();
        ResetGiftQuotaIfNewDay(senderMeta);
        if (senderMeta.GiftsSentToday >= MailboxConstants.MaxGiftsPerDay)
            throw new InvalidOperationException(MailboxError.GiftQuotaExceeded);

        var sentAt = DateTime.UtcNow.ToString("o");
        var mailId = "gf_" + Guid.NewGuid().ToString("N")[..8];
        var newMail = MailSchemaHelper.CreateMail(
            mailId,
            request.Subject,
            request.Body,
            sentAt,
            null,
            null,
            false,
            false,
            MailCategory.Gift,
            SenderType.Player,
            senderId,
            null);

        await InsertIntoTargetMailboxAsync(request.TargetPlayerId, newMail);
        senderMeta.GiftsSentToday++;
        try
        {
            await CloudSaveHelper.SetPlayerDataAsync(_gameApiClient, _context, senderId, MailboxConstants.KeyMeta, senderMeta, senderMetaLock);
        }
        catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex))
        {
            _logger.LogWarning(ex, "GiftMail: sender meta update conflict for {SenderId}", senderId);
        }

        return new GiftMailResponse { MailId = mailId, SentAt = sentAt };
    }

    private async Task InsertIntoTargetMailboxAsync(string targetPlayerId, MailItemDto newMail)
    {
        var (mailbox, writeLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerUserMailbox>(_gameApiClient, _context, targetPlayerId, MailboxConstants.KeyUserItems);
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
            await CloudSaveHelper.SetPlayerDataAsync(_gameApiClient, _context, targetPlayerId, MailboxConstants.KeyUserItems, mailbox, writeLock);
        }
        catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex))
        {
            var (freshMailbox, freshLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerUserMailbox>(_gameApiClient, _context, targetPlayerId, MailboxConstants.KeyUserItems);
            freshMailbox ??= new PlayerUserMailbox();
            MailboxEviction.Evict(freshMailbox, targetPlayerId, _logger);
            freshMailbox.Mails.Add(newMail);
            await CloudSaveHelper.SetPlayerDataAsync(_gameApiClient, _context, targetPlayerId, MailboxConstants.KeyUserItems, freshMailbox, freshLock);
        }
    }

    private static void ResetGiftQuotaIfNewDay(PlayerMailboxMeta meta)
    {
        var utcNow = DateTime.UtcNow;
        var todayMidnight = utcNow.Date;
        if (string.IsNullOrEmpty(meta.LastGiftResetAt) || !DateTime.TryParse(meta.LastGiftResetAt, out var lastReset) || lastReset < todayMidnight)
        {
            meta.GiftsSentToday = 0;
            meta.LastGiftResetAt = todayMidnight.ToString("o");
        }
    }
}
