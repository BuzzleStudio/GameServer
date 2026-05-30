using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BackpackAdventures.CloudCode;

public static class MailboxConstants
{
    public const string KeyGlobalMailIndex = "global_mail_index";
    public const string KeyGlobalMailIndexV2 = "global_mail_index_v2";
    public const string KeyGlobalMailPayloadFmt = "mail_global_{0}";
    public const string KeyGlobalState = "mailbox_global_state";
    public const string KeyUserItems = "mailbox_user_items";
    public const string KeyMeta = "mailbox_meta";
    public const string KeyIdemCache = "mailbox_idem_cache";
    public const string KeyPlayerWallet = "player_wallet";
    public const int MaxUserMailsStored = 200;
    public const int HardCapUserMailsStored = 250;
    public const int MaxGlobalMailRefs = 500;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;
    public const int MaxIdemCacheEntries = 50;
    public const int IdemCacheTtlHours = 24;
    public const int MaxGiftsPerDay = 5;
    public const int MaxSubjectLength = 128;
    public const int MaxBodyLength = 1024;
}

public enum MailType
{
    Notification,
    Attachment
}

public enum MailCategory
{
    System,
    Event,
    Compensation,
    Gift,
    Support,
    PatchNote
}

public enum SenderType
{
    System,
    Admin,
    Player
}

public class MailAttachment
{
    [JsonPropertyName("itemId")]
    public string ItemId { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "none";

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }
}

public class GlobalMailIndexV2
{
    public int Version { get; set; } = 3;
    public List<GlobalMailRef> Refs { get; set; } = new();
}

public class GlobalMailRef
{
    public string MessageId { get; set; } = string.Empty;
    public string StartTime { get; set; } = string.Empty;
    public string? ExpireTime { get; set; }
    public int Version { get; set; } = 3;

    public bool IsExpired() => MailSchemaHelper.IsExpired(ExpireTime);
}

public class GlobalMailPayload
{
    public int Version { get; set; } = 3;
    public MailItemDto Mail { get; set; } = new();

    public bool IsExpired() => MailSchemaHelper.IsExpired(Mail?.MailInfo?.ExpireTime);
}

public class PlayerGlobalMailState
{
    public int Version { get; set; } = 3;
    public List<string> ClaimedIds { get; set; } = new();
    public List<string> ReadIds { get; set; } = new();
    public List<string> DeletedIds { get; set; } = new();
}

public class PlayerUserMailbox
{
    public int Version { get; set; } = 3;
    public List<MailItemDto> Mails { get; set; } = new();
}

public class PlayerMailboxMeta
{
    public int Version { get; set; } = 2;
    public string? LastReadAt { get; set; }
    public int TotalUserMails { get; set; }
    public int TotalGlobalMails { get; set; }
    public int PendingRewardCount { get; set; }
    public string? LastPurgeAt { get; set; }
    public int GiftsSentToday { get; set; }
    public string? LastGiftResetAt { get; set; }
}

public class IdemCacheEntry
{
    public string RequestId { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string MailId { get; set; } = string.Empty;
    public string ResolvedAt { get; set; } = string.Empty;
    public object? ResponseSummary { get; set; }
}

public class IdemCache
{
    public int Version { get; set; } = 1;
    public List<IdemCacheEntry> Entries { get; set; } = new();
}

public class GlobalMailItem
{
    public string GlobalMailId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string SentAt { get; set; } = string.Empty;
    public string? ExpiresAt { get; set; }
    public List<MailAttachment>? Attachments { get; set; }

    public bool IsExpired() => MailSchemaHelper.IsExpired(ExpiresAt);
}

public class GlobalMailIndex
{
    public int Version { get; set; } = 1;
    public List<GlobalMailItem> Mails { get; set; } = new();
}

public class MailItemDto
{
    [JsonPropertyName("MessageId")]
    public string MessageId { get; set; } = string.Empty;

    [JsonPropertyName("MailInfo")]
    public MailInfoDto MailInfo { get; set; } = new();

    [JsonPropertyName("MailMetaData")]
    public MailMetaDataDto MailMetaData { get; set; } = new();

    public bool IsExpired() => MailSchemaHelper.IsExpired(MailInfo?.ExpireTime);
}

public class MailInfoDto
{
    [JsonPropertyName("Title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("Content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("StartTime")]
    public string StartTime { get; set; } = string.Empty;

    [JsonPropertyName("Period")]
    public int Period { get; set; }

    [JsonPropertyName("ExpireTime")]
    public string? ExpireTime { get; set; }

    [JsonPropertyName("Attachment")]
    public List<MailAttachmentDto>? Attachment { get; set; }
}

public class MailMetaDataDto
{
    [JsonPropertyName("IsRead")]
    public bool IsRead { get; set; }

    [JsonPropertyName("IsClaimed")]
    public bool IsClaimed { get; set; }

    [JsonPropertyName("MailCategory")]
    public string MailCategory { get; set; } = "System";

    [JsonPropertyName("SenderType")]
    public string SenderType { get; set; } = "System";

    [JsonPropertyName("Sender")]
    public string? Sender { get; set; }

    [JsonPropertyName("DedupKey")]
    public string? DedupKey { get; set; }
}

public class MailAttachmentDto
{
    [JsonPropertyName("PayoutAssetId")]
    public string PayoutAssetId { get; set; } = string.Empty;

    [JsonPropertyName("Chance")]
    public double Chance { get; set; } = 1.0;

    [JsonPropertyName("AssetType")]
    public string AssetType { get; set; } = string.Empty;

    [JsonPropertyName("PayoutAmount")]
    public int PayoutAmount { get; set; }
}

public static class MailSchemaHelper
{
    public static MailItemDto CreateMail(
        string messageId,
        string title,
        string content,
        string startTime,
        string? expireTime,
        List<MailAttachment>? attachments,
        bool isRead,
        bool isClaimed,
        MailCategory mailCategory,
        SenderType senderType,
        string? sender,
        string? dedupKey)
    {
        return new MailItemDto
        {
            MessageId = messageId,
            MailInfo = new MailInfoDto
            {
                Title = title,
                Content = content,
                StartTime = startTime,
                Period = CalculatePeriodSeconds(startTime, expireTime),
                ExpireTime = expireTime,
                Attachment = MapAttachments(attachments)
            },
            MailMetaData = new MailMetaDataDto
            {
                IsRead = isRead,
                IsClaimed = isClaimed,
                MailCategory = mailCategory.ToString(),
                SenderType = senderType.ToString(),
                Sender = sender,
                DedupKey = dedupKey
            }
        };
    }

    public static GlobalMailRef CreateGlobalRef(MailItemDto mail)
    {
        return new GlobalMailRef
        {
            MessageId = mail.MessageId,
            StartTime = mail.MailInfo.StartTime,
            ExpireTime = mail.MailInfo.ExpireTime,
            Version = 3
        };
    }

    public static List<MailAttachment>? ToAttachments(List<MailAttachmentDto>? attachments)
    {
        if (attachments == null || attachments.Count == 0) return null;
        var result = new List<MailAttachment>(attachments.Count);
        foreach (var attachment in attachments)
        {
            result.Add(new MailAttachment
            {
                ItemId = attachment.PayoutAssetId,
                Quantity = attachment.PayoutAmount,
                Type = NormalizeAssetTypeToStorage(attachment.AssetType)
            });
        }
        return result;
    }

    public static MailCategory ParseMailCategory(MailItemDto mail)
    {
        if (Enum.TryParse(mail.MailMetaData?.MailCategory, true, out MailCategory category))
            return category;
        return MailCategory.System;
    }

    public static SenderType ParseSenderType(MailItemDto mail)
    {
        if (Enum.TryParse(mail.MailMetaData?.SenderType, true, out SenderType senderType))
            return senderType;
        return SenderType.System;
    }

    public static bool HasAttachments(MailItemDto mail)
    {
        return mail.MailInfo?.Attachment != null && mail.MailInfo.Attachment.Count > 0;
    }

    public static bool IsExpired(string? expireTime)
    {
        if (string.IsNullOrEmpty(expireTime)) return false;
        if (!DateTimeOffset.TryParse(expireTime, out var exp)) return false;
        return exp < DateTimeOffset.UtcNow;
    }

    public static int CalculatePeriodSeconds(string startTime, string? expireTime)
    {
        if (string.IsNullOrEmpty(expireTime)) return 0;
        if (!DateTimeOffset.TryParse(startTime, out var start)) return 0;
        if (!DateTimeOffset.TryParse(expireTime, out var end)) return 0;
        var seconds = (end - start).TotalSeconds;
        if (seconds <= 0) return 0;
        return seconds > int.MaxValue ? int.MaxValue : (int)Math.Round(seconds);
    }

    public static List<MailAttachmentDto>? MapAttachments(List<MailAttachment>? attachments)
    {
        if (attachments == null || attachments.Count == 0) return null;
        var result = new List<MailAttachmentDto>(attachments.Count);
        foreach (var attachment in attachments)
        {
            result.Add(new MailAttachmentDto
            {
                PayoutAssetId = attachment.ItemId,
                Chance = 1.0,
                AssetType = NormalizeAssetTypeForDto(attachment.Type),
                PayoutAmount = attachment.Quantity
            });
        }
        return result;
    }

    public static MailItemDto FromLegacyGlobalMail(GlobalMailItem mail, bool isRead, bool isClaimed)
    {
        return CreateMail(
            mail.GlobalMailId,
            mail.Subject,
            mail.Body,
            mail.SentAt,
            mail.ExpiresAt,
            mail.Attachments,
            isRead,
            isClaimed,
            MailCategory.System,
            SenderType.System,
            null,
            null);
    }

    private static string NormalizeAssetTypeForDto(string assetType)
    {
        if (string.IsNullOrEmpty(assetType)) return string.Empty;
        if (assetType.Length == 1) return assetType.ToUpperInvariant();
        return char.ToUpperInvariant(assetType[0]) + assetType.Substring(1).ToLowerInvariant();
    }

    private static string NormalizeAssetTypeToStorage(string assetType)
    {
        if (string.IsNullOrEmpty(assetType)) return string.Empty;
        return assetType.Equals("Currency", StringComparison.OrdinalIgnoreCase) ? "currency" : "item";
    }
}

public class PagedMailResponse
{
    public List<MailItemDto> Mails { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public bool HasMore { get; set; }
}

public class SendGlobalMailRequest
{
    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("expiresAt")]
    public string? ExpiresAt { get; set; }

    [JsonPropertyName("mailCategory")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MailCategory MailCategory { get; set; } = MailCategory.System;

    [JsonPropertyName("senderName")]
    public string? SenderName { get; set; }

    [JsonPropertyName("dedupKey")]
    public string? DedupKey { get; set; }

    [JsonPropertyName("attachments")]
    public List<MailAttachment>? Attachments { get; set; }

    [JsonPropertyName("adminToken")]
    public string AdminToken { get; set; } = string.Empty;

    [JsonPropertyName("operatorId")]
    public string OperatorId { get; set; } = string.Empty;
}

public class SendUserMailRequest
{
    [JsonPropertyName("targetPlayerId")]
    public string TargetPlayerId { get; set; } = string.Empty;

    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("expiresAt")]
    public string? ExpiresAt { get; set; }

    [JsonPropertyName("mailCategory")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MailCategory MailCategory { get; set; } = MailCategory.System;

    [JsonPropertyName("senderName")]
    public string? SenderName { get; set; }

    [JsonPropertyName("dedupKey")]
    public string? DedupKey { get; set; }

    [JsonPropertyName("attachments")]
    public List<MailAttachment>? Attachments { get; set; }

    [JsonPropertyName("adminToken")]
    public string AdminToken { get; set; } = string.Empty;

    [JsonPropertyName("operatorId")]
    public string OperatorId { get; set; } = string.Empty;
}

public class GiftMailRequest
{
    [JsonPropertyName("targetPlayerId")]
    public string TargetPlayerId { get; set; } = string.Empty;

    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;
}

public class GetMailsRequest
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; } = MailboxConstants.DefaultPageSize;
}

public class MarkMailReadRequest
{
    [JsonPropertyName("mailId")]
    public string MailId { get; set; } = string.Empty;

    [JsonPropertyName("mailType")]
    public string MailType { get; set; } = string.Empty;

    [JsonPropertyName("requestId")]
    public string? RequestId { get; set; }
}

public class ClaimAttachmentRequest
{
    [JsonPropertyName("mailId")]
    public string MailId { get; set; } = string.Empty;

    [JsonPropertyName("mailType")]
    public string MailType { get; set; } = string.Empty;

    [JsonPropertyName("requestId")]
    public string? RequestId { get; set; }
}

public class DeleteMailRequest
{
    [JsonPropertyName("mailId")]
    public string MailId { get; set; } = string.Empty;
}

public class ExpireMailRequest
{
    [JsonPropertyName("mailId")]
    public string MailId { get; set; } = string.Empty;

    [JsonPropertyName("adminToken")]
    public string AdminToken { get; set; } = string.Empty;

    [JsonPropertyName("operatorId")]
    public string OperatorId { get; set; } = string.Empty;
}

public class SendGlobalMailResponse
{
    public string GlobalMailId { get; set; } = string.Empty;
    public string SentAt { get; set; } = string.Empty;
}

public class SendUserMailResponse
{
    public string MailId { get; set; } = string.Empty;
    public string SentAt { get; set; } = string.Empty;
}

public class GiftMailResponse
{
    public string MailId { get; set; } = string.Empty;
    public string SentAt { get; set; } = string.Empty;
}

public class GetMailboxResponse
{
    public List<MailItemDto> Mails { get; set; } = new();
}

public class MarkMailReadResponse
{
    public string MailId { get; set; } = string.Empty;
    public bool IsRead { get; set; }
}

public class MarkAllReadResponse
{
    public string LastReadAt { get; set; } = string.Empty;
}

public class ClaimAttachmentResponse
{
    public string MailId { get; set; } = string.Empty;
    public bool AlreadyClaimed { get; set; }
    public List<MailAttachment>? GrantedAttachments { get; set; }
}

public class DeleteMailResponse
{
    public string MailId { get; set; } = string.Empty;
}

public class ExpireMailResponse
{
    public string MailId { get; set; } = string.Empty;
    public string ExpiredAt { get; set; } = string.Empty;
}

public class PurgeExpiredResponse
{
    public int PurgedCount { get; set; }
    public string PurgedAt { get; set; } = string.Empty;
}

public class PurgeExpiredRequest
{
    [JsonPropertyName("adminToken")]
    public string AdminToken { get; set; } = string.Empty;

    [JsonPropertyName("operatorId")]
    public string OperatorId { get; set; } = string.Empty;
}

public static class MailboxError
{
    public const string InvalidInput = "InvalidInput";
    public const string MailNotFound = "MailNotFound";
    public const string MailExpired = "MailExpired";
    public const string AlreadyClaimed = "AlreadyClaimed";
    public const string NoAttachment = "NoAttachment";
    public const string Unauthorized = "Unauthorized";
    public const string MailboxFull = "MailboxFull";
    public const string Conflict = "Conflict";
    public const string GrantUnavailable = "GrantUnavailable";
    public const string GiftQuotaExceeded = "GiftQuotaExceeded";
    public const string CannotDeleteUnclaimedReward = "CannotDeleteUnclaimedReward";
    public const string CannotDeleteGlobal = "CannotDeleteGlobal";
    public const string CannotExpireUserMail = "CannotExpireUserMail";
    public const string TargetMailboxFull = "TargetMailboxFull";
}

