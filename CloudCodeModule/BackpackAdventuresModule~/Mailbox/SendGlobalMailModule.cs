using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

public class SendGlobalMailModule
{
    private readonly IExecutionContext _context;
    private readonly IGameApiClient _gameApiClient;
    private readonly ILogger<SendGlobalMailModule> _logger;

    public SendGlobalMailModule(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        ILogger<SendGlobalMailModule> logger)
    {
        _context = context;
        _gameApiClient = gameApiClient;
        _logger = logger;
    }

    [CloudCodeFunction("SendGlobalMail")]
    public async Task<SendGlobalMailResponse> SendGlobalMailAsync(SendGlobalMailRequest request)
    {
        await AdminAuth.RequireAdminToolAsync(_gameApiClient, _context, request.AdminToken, request.OperatorId, _logger);
        ValidateRequest(request.Subject, request.Body, request.Attachments);

        var (index, writeLock) = await CloudSaveHelper.GetCustomDataWithLockAsync<GlobalMailIndexV2>(_gameApiClient, _context, MailboxConstants.KeyGlobalMailIndexV2);
        index ??= new GlobalMailIndexV2();

        if (!string.IsNullOrEmpty(request.DedupKey))
        {
            foreach (var existingRef in index.Refs)
            {
                if (existingRef.DedupKey == request.DedupKey)
                    return new SendGlobalMailResponse { GlobalMailId = existingRef.MessageId, SentAt = existingRef.StartTime };
            }
        }

        index.Refs.RemoveAll(r => r.IsExpired());
        if (index.Refs.Count >= MailboxConstants.MaxGlobalMailRefs)
            throw new InvalidOperationException(MailboxError.MailboxFull);

        var startTime = DateTime.UtcNow;
        var endTime = ResolveEndTime(startTime, request.ExpiresAt);
        var sentAt = startTime.ToString("o");
        var mailId = "gm_" + Guid.NewGuid().ToString("N")[..8];
        var targetUserIds = NormalizeTargetUserIds(request.TargetUserIds);
        var mail = MailSchemaHelper.CreateAdminMail(
            mailId,
            targetUserIds,
            request.Subject,
            request.Body,
            startTime,
            endTime,
            request.Attachments);

        await CloudSaveHelper.SetCustomDataAsync(_gameApiClient, _context, string.Format(MailboxConstants.KeyGlobalMailPayloadFmt, mailId), new GlobalMailPayload { Mail = mail, Version = 4 });
        index.Refs.Add(MailSchemaHelper.CreateGlobalRef(mail, request.DedupKey));

        try
        {
            await CloudSaveHelper.SetCustomDataWithLockAsync(_gameApiClient, _context, MailboxConstants.KeyGlobalMailIndexV2, index, writeLock);
        }
        catch (Exception ex) when (CloudSaveHelper.IsWriteLockConflict(ex))
        {
            var (freshIndex, freshLock) = await CloudSaveHelper.GetCustomDataWithLockAsync<GlobalMailIndexV2>(_gameApiClient, _context, MailboxConstants.KeyGlobalMailIndexV2);
            freshIndex ??= new GlobalMailIndexV2();
            freshIndex.Refs.RemoveAll(r => r.IsExpired());
            freshIndex.Refs.Add(MailSchemaHelper.CreateGlobalRef(mail, request.DedupKey));
            await CloudSaveHelper.SetCustomDataWithLockAsync(_gameApiClient, _context, MailboxConstants.KeyGlobalMailIndexV2, freshIndex, freshLock);
        }

        return new SendGlobalMailResponse { GlobalMailId = mailId, SentAt = sentAt };
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

    private static List<string>? NormalizeTargetUserIds(List<string>? targetUserIds)
    {
        if (targetUserIds == null || targetUserIds.Count == 0)
            return null;

        var result = new List<string>();
        foreach (var targetUserId in targetUserIds)
        {
            if (string.IsNullOrWhiteSpace(targetUserId))
                continue;

            var normalized = targetUserId.Trim();
            if (!result.Contains(normalized))
                result.Add(normalized);
        }

        return result.Count == 0 ? null : result;
    }

    private static DateTime ResolveEndTime(DateTime startTime, string? expiresAt)
    {
        if (!string.IsNullOrEmpty(expiresAt) && DateTime.TryParse(expiresAt, out var parsed))
            return parsed.ToUniversalTime();
        return startTime.AddDays(7);
    }
}
