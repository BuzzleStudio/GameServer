using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

/// <summary>
/// Admin-only endpoint: sends a direct mail to a specific player.
/// Writes to the target player's mailbox_user_items with writeLock (retry-once on conflict).
/// </summary>
public class SendUserMailModule
{
    private readonly IExecutionContext _context;
    private readonly IGameApiClient _gameApiClient;
    private readonly ILogger<SendUserMailModule> _logger;
    private readonly AdminAuthService _adminAuth;

    public SendUserMailModule(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        ILogger<SendUserMailModule> logger,
        AdminAuthService adminAuth)
    {
        _context = context;
        _gameApiClient = gameApiClient;
        _logger = logger;
        _adminAuth = adminAuth;
    }

    [CloudCodeFunction("SendUserMail")]
    public async Task<SendUserMailResponse> SendUserMailAsync(SendUserMailRequest request)
    {
        var callerId = _context.PlayerId ?? string.Empty;
        _logger.LogInformation("SendUserMail called by {CallerId} for target {TargetPlayerId}", callerId, request.TargetPlayerId);

        await _adminAuth.RequireAdminAsync(callerId);

        if (string.IsNullOrWhiteSpace(request.TargetPlayerId))
            throw new ArgumentException(MailboxError.InvalidInput);

        ValidateRequest(request.Subject, request.Body, request.Attachments);

        var sentAt = DateTime.UtcNow.ToString("o");
        var mailId = "um_" + Guid.NewGuid().ToString("N")[..8];

        var mailType = (request.Attachments != null && request.Attachments.Count > 0)
            ? MailType.Attachment
            : MailType.Notification;

        var newMail = new UserMailItem
        {
            MailId       = mailId,
            Subject      = request.Subject,
            Body         = request.Body,
            SentAt       = sentAt,
            ExpiresAt    = request.ExpiresAt,
            MailType     = mailType,
            MailCategory = request.MailCategory,
            SenderType   = SenderType.Admin,
            Sender       = request.SenderName,
            DedupKey     = request.DedupKey,
            Attachments  = request.Attachments
        };

        await InsertMailWithRetryAsync(request.TargetPlayerId, newMail);

        _logger.LogInformation("Admin user mail {MailId} sent to {TargetPlayerId}", mailId, request.TargetPlayerId);
        return new SendUserMailResponse { Success = true, MailId = mailId, SentAt = sentAt };
    }

    private async Task InsertMailWithRetryAsync(string targetPlayerId, UserMailItem newMail)
    {
        var (mailbox, writeLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerUserMailbox>(
            _gameApiClient, _context, targetPlayerId, MailboxConstants.KeyUserItems);
        mailbox ??= new PlayerUserMailbox();

        MailboxEviction.Evict(mailbox, targetPlayerId, _logger);
        mailbox.Mails.Add(newMail);

        try
        {
            await CloudSaveHelper.SetPlayerDataAsync(
                _gameApiClient, _context, targetPlayerId, MailboxConstants.KeyUserItems, mailbox, writeLock);
        }
        catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex))
        {
            _logger.LogWarning("SendUserMail: write conflict — retrying once for {TargetPlayerId}", targetPlayerId);
            var (freshMailbox, freshLock) = await CloudSaveHelper.GetPlayerDataWithLockAsync<PlayerUserMailbox>(
                _gameApiClient, _context, targetPlayerId, MailboxConstants.KeyUserItems);
            freshMailbox ??= new PlayerUserMailbox();

            MailboxEviction.Evict(freshMailbox, targetPlayerId, _logger);
            freshMailbox.Mails.Add(newMail);

            try
            {
                await CloudSaveHelper.SetPlayerDataAsync(
                    _gameApiClient, _context, targetPlayerId, MailboxConstants.KeyUserItems, freshMailbox, freshLock);
            }
            catch (Exception retryEx) when (CloudSaveHelper.IsWriteLockConflict(retryEx))
            {
                _logger.LogError("SendUserMail: write conflict on retry for {TargetPlayerId}", targetPlayerId);
                throw new InvalidOperationException(MailboxError.Conflict);
            }
        }
    }

    private static void ValidateRequest(string subject, string body, System.Collections.Generic.List<MailAttachment>? attachments)
    {
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > MailboxConstants.MaxSubjectLength)
            throw new ArgumentException(MailboxError.InvalidInput);
        if (string.IsNullOrWhiteSpace(body) || body.Length > MailboxConstants.MaxBodyLength)
            throw new ArgumentException(MailboxError.InvalidInput);
        if (attachments != null)
        {
            foreach (var att in attachments)
            {
                if (string.IsNullOrEmpty(att.ItemId) || att.Quantity <= 0 ||
                    (att.Type != "currency" && att.Type != "item"))
                    throw new ArgumentException(MailboxError.InvalidInput);
            }
        }
    }
}
