using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

public class SendMailModule
{
    private readonly IExecutionContext _context;
    private readonly IGameApiClient _gameApiClient;
    private readonly ILogger<SendMailModule> _logger;

    public SendMailModule(IExecutionContext context, IGameApiClient gameApiClient, ILogger<SendMailModule> logger)
    {
        _context = context;
        _gameApiClient = gameApiClient;
        _logger = logger;
    }

    /// <summary>Broadcast a mail to all players via Cloud Save custom data (key: global_mail_index).</summary>
    [CloudCodeFunction("SendGlobalMail")]
    public async Task<SendGlobalMailResponse> SendGlobalMailAsync(SendGlobalMailRequest request)
    {
        _logger.LogInformation("SendGlobalMail called by {PlayerId}", _context.PlayerId);
        try
        {
            ValidateRequest(request.Subject, request.Body);

            var index = await CloudSaveHelper.GetCustomDataAsync<GlobalMailIndex>(
                _gameApiClient, _context, MailboxConstants.KeyGlobalMailIndex) ?? new GlobalMailIndex();

            var sentAt = DateTime.UtcNow.ToString("o");
            var mail = new GlobalMailItem
            {
                GlobalMailId = Guid.NewGuid().ToString(),
                Subject = request.Subject,
                Body = request.Body,
                SentAt = sentAt,
                ExpiresAt = request.ExpiresAt,
                Attachments = request.Attachments
            };

            index.Mails.Add(mail);
            await CloudSaveHelper.SetCustomDataAsync(_gameApiClient, _context, MailboxConstants.KeyGlobalMailIndex, index);

            _logger.LogInformation("Global mail {MailId} stored", mail.GlobalMailId);
            return new SendGlobalMailResponse { Success = true, GlobalMailId = mail.GlobalMailId, SentAt = sentAt };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SendGlobalMail failed");
            throw;
        }
    }

    /// <summary>Send a mail directly to a specific player's Cloud Save player data (key: mailbox_user_items).</summary>
    [CloudCodeFunction("SendUserMail")]
    public async Task<SendUserMailResponse> SendUserMailAsync(SendUserMailRequest request)
    {
        _logger.LogInformation("SendUserMail to {UserId} by {PlayerId}", request.TargetPlayerId, _context.PlayerId);
        try
        {
            if (string.IsNullOrWhiteSpace(request.TargetPlayerId))
                throw new ArgumentException(MailboxError.InvalidInput);
            ValidateRequest(request.Subject, request.Body);

            var mailbox = await CloudSaveHelper.GetPlayerDataAsync<PlayerUserMailbox>(
                _gameApiClient, _context, request.TargetPlayerId, MailboxConstants.KeyUserItems) ?? new PlayerUserMailbox();

            var sentAt = DateTime.UtcNow.ToString("o");
            var mail = new UserMailItem
            {
                MailId = Guid.NewGuid().ToString(),
                Subject = request.Subject,
                Body = request.Body,
                SentAt = sentAt,
                ExpiresAt = request.ExpiresAt,
                Attachments = request.Attachments
            };

            if (mailbox.Mails.Count >= MailboxConstants.MaxUserMailsStored)
                mailbox.Mails.RemoveAt(0);

            mailbox.Mails.Add(mail);
            await CloudSaveHelper.SetPlayerDataAsync(_gameApiClient, _context, request.TargetPlayerId, MailboxConstants.KeyUserItems, mailbox);

            _logger.LogInformation("User mail {MailId} stored for {UserId}", mail.MailId, request.TargetPlayerId);
            return new SendUserMailResponse { Success = true, MailId = mail.MailId, SentAt = sentAt };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SendUserMail failed for {UserId}", request.TargetPlayerId);
            throw;
        }
    }

    private static void ValidateRequest(string subject, string body)
    {
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > MailboxConstants.MaxSubjectLength)
            throw new ArgumentException(MailboxError.InvalidInput);
        if (string.IsNullOrWhiteSpace(body) || body.Length > MailboxConstants.MaxBodyLength)
            throw new ArgumentException(MailboxError.InvalidInput);
    }
}
