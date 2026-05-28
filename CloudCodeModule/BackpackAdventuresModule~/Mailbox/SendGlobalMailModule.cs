using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

/// <summary>
/// Admin-only endpoint: broadcasts a global mail visible to all players.
/// Writes payload to mail_global_{mailId} and appends a ref to global_mail_index_v2 under writeLock.
/// </summary>
public class SendGlobalMailModule
{
    private readonly IExecutionContext _context;
    private readonly IGameApiClient _gameApiClient;
    private readonly ILogger<SendGlobalMailModule> _logger;
    private readonly AdminAuthService _adminAuth;

    public SendGlobalMailModule(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        ILogger<SendGlobalMailModule> logger,
        AdminAuthService adminAuth)
    {
        _context = context;
        _gameApiClient = gameApiClient;
        _logger = logger;
        _adminAuth = adminAuth;
    }

    [CloudCodeFunction("SendGlobalMail")]
    public async Task<SendGlobalMailResponse> SendGlobalMailAsync(SendGlobalMailRequest request)
    {
        var callerId = _context.PlayerId ?? string.Empty;
        _logger.LogInformation("SendGlobalMail called by {PlayerId}", callerId);

        await _adminAuth.RequireAdminAsync(callerId);

        ValidateRequest(request.Subject, request.Body, request.Attachments);

        // Read index with writeLock
        var (index, writeLock) = await CloudSaveHelper.GetCustomDataWithLockAsync<GlobalMailIndexV2>(
            _gameApiClient, _context, MailboxConstants.KeyGlobalMailIndexV2);
        index ??= new GlobalMailIndexV2();

        // Dedup check
        if (!string.IsNullOrEmpty(request.DedupKey))
        {
            foreach (var existingRef in index.Refs)
            {
                var payload = await CloudSaveHelper.GetCustomDataAsync<GlobalMailPayload>(
                    _gameApiClient, _context,
                    string.Format(MailboxConstants.KeyGlobalMailPayloadFmt, existingRef.MailId));
                if (payload?.DedupKey == request.DedupKey)
                {
                    _logger.LogInformation("SendGlobalMail dedupKey={DedupKey} already exists, returning existing mailId={MailId}",
                        request.DedupKey, existingRef.MailId);
                    return new SendGlobalMailResponse
                    {
                        Success = true,
                        GlobalMailId = existingRef.MailId,
                        SentAt = existingRef.SentAt
                    };
                }
            }
        }

        // Prune expired refs
        index.Refs.RemoveAll(r => r.IsExpired());

        // Cap check
        if (index.Refs.Count >= MailboxConstants.MaxGlobalMailRefs)
        {
            _logger.LogWarning("Global mail index cap ({Cap}) reached — rejecting SendGlobalMail", MailboxConstants.MaxGlobalMailRefs);
            throw new InvalidOperationException(MailboxError.MailboxFull);
        }

        var sentAt = DateTime.UtcNow.ToString("o");
        var mailId = "gm_" + Guid.NewGuid().ToString("N")[..8];

        var mailType = (request.Attachments != null && request.Attachments.Count > 0)
            ? MailType.Attachment
            : MailType.Notification;

        // Write per-mail payload FIRST (so if ref-index write fails, payload exists but is unreferenced — safe)
        var mailPayload = new GlobalMailPayload
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
            Attachments  = request.Attachments,
            Version      = 1
        };
        await CloudSaveHelper.SetCustomDataAsync(_gameApiClient, _context,
            string.Format(MailboxConstants.KeyGlobalMailPayloadFmt, mailId), mailPayload);

        // Append ref and write index with writeLock (retry once on conflict)
        index.Refs.Add(new GlobalMailRef
        {
            MailId    = mailId,
            SentAt    = sentAt,
            ExpiresAt = request.ExpiresAt,
            Version   = 1
        });

        try
        {
            await CloudSaveHelper.SetCustomDataWithLockAsync(
                _gameApiClient, _context, MailboxConstants.KeyGlobalMailIndexV2, index, writeLock);
        }
        catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex))
        {
            _logger.LogWarning("SendGlobalMail: write conflict on index — retrying once");
            var (freshIndex, freshLock) = await CloudSaveHelper.GetCustomDataWithLockAsync<GlobalMailIndexV2>(
                _gameApiClient, _context, MailboxConstants.KeyGlobalMailIndexV2);
            freshIndex ??= new GlobalMailIndexV2();
            freshIndex.Refs.RemoveAll(r => r.IsExpired());
            freshIndex.Refs.Add(new GlobalMailRef
            {
                MailId    = mailId,
                SentAt    = sentAt,
                ExpiresAt = request.ExpiresAt,
                Version   = 1
            });
            try
            {
                await CloudSaveHelper.SetCustomDataWithLockAsync(
                    _gameApiClient, _context, MailboxConstants.KeyGlobalMailIndexV2, freshIndex, freshLock);
            }
            catch (Exception retryEx) when (CloudSaveHelper.IsWriteLockConflict(retryEx))
            {
                _logger.LogError("SendGlobalMail: write conflict on retry — returning Conflict");
                throw new InvalidOperationException(MailboxError.Conflict);
            }
        }

        _logger.LogInformation("Global mail {MailId} stored by admin {PlayerId}", mailId, callerId);
        return new SendGlobalMailResponse { Success = true, GlobalMailId = mailId, SentAt = sentAt };
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
