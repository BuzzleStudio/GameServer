using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
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
    public async Task<ApiResponse<SendUserMailResponse>> SendUserMailAsync(SendUserMailRequest request)
    {
        await AdminAuth.RequireAdminToolAsync(_gameApiClient, _context, request.AdminToken, request.OperatorId, _logger);
        var targetUserIds = ResolveTargetUserIds(request);
        if (targetUserIds.Count == 0)
            throw new ArgumentException(MailboxError.InvalidInput);

        ValidateRequest(request.Subject, request.Body, request.Attachments);

        var (collection, writeLock) = await CloudSaveHelper.GetCustomDataWithLockAsync<GlobalMailCollection>(_gameApiClient, _context, MailboxConstants.KeyMailsAll);
        collection ??= new GlobalMailCollection();
        var mails = collection.Mails;
        GlobalMailStore.RemoveExpired(mails);

        var mailId = CreateMailId(request.DedupKey);
        var existing = GlobalMailStore.FindById(mails, mailId);
        if (existing?.Mail != null)
            return ApiResponse<SendUserMailResponse>.Ok(new SendUserMailResponse { MailId = existing.Mail.MessageId, SentAt = existing.Mail.StartTime.ToUniversalTime().ToString("o") });

        if (mails.Count >= MailboxConstants.MaxGlobalMailsStored)
            throw new InvalidOperationException(MailboxError.MailboxFull);

        var startTime = DateTime.UtcNow;
        var endTime = ResolveEndTime(request.ExpiresAt);
        var sentAt = startTime.ToString("o");
        var newMail = MailSchemaHelper.CreateAdminMail(
            mailId,
            targetUserIds,
            request.Subject,
            request.Body,
            startTime,
            endTime,
            request.Attachments);

        mails.Add(new GlobalMailPayload { Mail = newMail });

        try
        {
            await CloudSaveHelper.SetCustomDataWithLockAsync(_gameApiClient, _context, MailboxConstants.KeyMailsAll, collection, writeLock);
        }
        catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex))
        {
            var (freshCollection, freshLock) = await CloudSaveHelper.GetCustomDataWithLockAsync<GlobalMailCollection>(_gameApiClient, _context, MailboxConstants.KeyMailsAll);
            freshCollection ??= new GlobalMailCollection();
            var freshMails = freshCollection.Mails;
            GlobalMailStore.RemoveExpired(freshMails);
            var freshExisting = GlobalMailStore.FindById(freshMails, mailId);
            if (freshExisting?.Mail != null)
                return ApiResponse<SendUserMailResponse>.Ok(new SendUserMailResponse { MailId = freshExisting.Mail.MessageId, SentAt = freshExisting.Mail.StartTime.ToUniversalTime().ToString("o") });
            freshMails.Add(new GlobalMailPayload { Mail = newMail });
            await CloudSaveHelper.SetCustomDataWithLockAsync(_gameApiClient, _context, MailboxConstants.KeyMailsAll, freshCollection, freshLock);
        }

        return ApiResponse<SendUserMailResponse>.Ok(new SendUserMailResponse { MailId = mailId, SentAt = sentAt });
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

    private static List<string> ResolveTargetUserIds(SendUserMailRequest request)
    {
        var result = new List<string>();
        AddTargetUserIds(result, request.TargetUserIds);
        AddTargetUserId(result, request.TargetPlayerId);
        AddTargetUserId(result, request.UserId);
        return result;
    }

    private static void AddTargetUserIds(List<string> result, List<string>? targetUserIds)
    {
        if (targetUserIds == null) return;
        foreach (var targetUserId in targetUserIds)
            AddTargetUserId(result, targetUserId);
    }

    private static void AddTargetUserId(List<string> result, string? targetUserId)
    {
        if (string.IsNullOrWhiteSpace(targetUserId)) return;
        var normalized = targetUserId.Trim();
        if (!result.Contains(normalized))
            result.Add(normalized);
    }

    private static DateTime? ResolveEndTime(string? expiresAt)
    {
        if (!string.IsNullOrEmpty(expiresAt) && DateTime.TryParse(expiresAt, out var parsed))
            return parsed.ToUniversalTime();
        return null;
    }

    private static string CreateMailId(string? dedupKey)
    {
        if (string.IsNullOrWhiteSpace(dedupKey))
            return "gm_" + Guid.NewGuid().ToString("N")[..8];

        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(dedupKey.Trim()));
        return "gm_" + Convert.ToHexString(bytes).ToLowerInvariant()[..8];
    }
}
