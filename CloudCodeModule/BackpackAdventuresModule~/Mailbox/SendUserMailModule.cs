using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

public class SendUserMailModule
{
    private readonly IExecutionContext _context;
    private readonly IGameApiClient _gameApiClient;
    private readonly ILogger<SendUserMailModule> _logger;

    public SendUserMailModule(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        ILogger<SendUserMailModule> logger)
    {
        _context = context;
        _gameApiClient = gameApiClient;
        _logger = logger;
    }

    [CloudCodeFunction("SendUserMail")]
    public async Task<SendUserMailResponse> SendUserMailAsync(SendUserMailRequest request)
    {
        await AdminAuth.RequireAdminToolAsync(_gameApiClient, _context, request.AdminToken, request.OperatorId, _logger);
        if (string.IsNullOrWhiteSpace(request.TargetPlayerId))
            throw new ArgumentException(MailboxError.InvalidInput);

        ValidateRequest(request.Subject, request.Body, request.Attachments);

        var sentAt = DateTime.UtcNow.ToString("o");
        var mailId = "um_" + Guid.NewGuid().ToString("N")[..8];
        var newMail = MailSchemaHelper.CreateMail(
            mailId,
            request.Subject,
            request.Body,
            sentAt,
            request.ExpiresAt,
            request.Attachments,
            false,
            false,
            request.MailCategory,
            SenderType.Admin,
            request.SenderName,
            request.DedupKey);

        await InsertMailWithRetryAsync(request.TargetPlayerId, newMail);
        return new SendUserMailResponse { MailId = mailId, SentAt = sentAt };
    }

    private async Task InsertMailWithRetryAsync(string targetPlayerId, MailItemDto newMail)
    {
        var (mailbox, writeLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerUserMailbox>(_gameApiClient, _context, targetPlayerId, MailboxConstants.KeyUserItems);
        mailbox ??= new PlayerUserMailbox();
        MailboxEviction.Evict(mailbox, targetPlayerId, _logger);
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

    private static void ValidateRequest(string subject, string body, System.Collections.Generic.List<MailAttachment>? attachments)
    {
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > MailboxConstants.MaxSubjectLength)
            throw new ArgumentException(MailboxError.InvalidInput);
        if (string.IsNullOrWhiteSpace(body) || body.Length > MailboxConstants.MaxBodyLength)
            throw new ArgumentException(MailboxError.InvalidInput);
        if (attachments == null) return;
        foreach (var att in attachments)
        {
            if (string.IsNullOrEmpty(att.ItemId) || att.Quantity <= 0 || (att.Type != "currency" && att.Type != "item"))
                throw new ArgumentException(MailboxError.InvalidInput);
        }
    }
}
