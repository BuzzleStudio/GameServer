using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BackpackAdventures.CloudCode;

public static class MailboxConstants
{
    public const string KeyGlobalMailIndex = "global_mail_index_legacy";
    public const string KeyMailsAll = "mails_all";
    public const string KeyGlobalState = "mail_meta_state";
    public const string KeyUserItems = "mailbox_user_items";
    public const string KeyMeta = "mailbox_meta";
    public const string KeyIdemCache = "mailbox_idem_cache";
    public const string KeyPlayerWallet = "player_wallet";
    public const int MaxUserMailsStored = 200;
    public const int HardCapUserMailsStored = 250;
    public const int MaxGlobalMailsStored = 500;
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

// Stored inside the `mails_all` array. Serializes as the bare Mail object (the
// redundant { "Mail": { … } } wrapper was dropped). Reads BOTH shapes:
//   new  → { "MessageId": …, "Title": …, … }          (bare mail)
//   old  → { "Mail": { "MessageId": …, "Title": … } }  (legacy wrapper)
// so existing data keeps deserializing; the next write rewrites it flat.
[JsonConverter(typeof(GlobalMailPayloadConverter))]
public class GlobalMailPayload
{
    public Mail Mail { get; set; } = new();

    public bool IsExpired() => Mail?.IsExpired ?? false;
}

public class GlobalMailPayloadConverter : JsonConverter<GlobalMailPayload>
{
    public override GlobalMailPayload Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        // Legacy wrapper: { "Mail": { … } }. Detected only when a "Mail" property
        // holds an object — a bare mail never nests another mail under "Mail".
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("Mail", out var inner)
            && inner.ValueKind == JsonValueKind.Object)
        {
            return new GlobalMailPayload
            {
                Mail = inner.Deserialize<Mail>(options) ?? new Mail()
            };
        }

        return new GlobalMailPayload
        {
            Mail = root.Deserialize<Mail>(options) ?? new Mail()
        };
    }

    public override void Write(Utf8JsonWriter writer, GlobalMailPayload value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value.Mail ?? new Mail(), options);
    }
}

// The value stored under the `mails_all` Cloud Save key. Serializes as
// { "Mails": [ <bare mail>, … ] }. Reads ALL of these shapes:
//   new    → { "Mails": [ … ] }                 (current)
//   legacy → [ … ]                              (raw array — wrapped into Mails)
//   foreign/empty/null → treated as no mails    (self-heals on next write)
// Inner elements stay bare mail objects via GlobalMailPayloadConverter; mail
// fields are preserved exactly, never mapped to IsRead/IsClaim/IsDelete state.
[JsonConverter(typeof(GlobalMailCollectionConverter))]
public class GlobalMailCollection
{
    public List<GlobalMailPayload> Mails { get; set; } = new();
}

public class GlobalMailCollectionConverter : JsonConverter<GlobalMailCollection>
{
    public override GlobalMailCollection Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        // Legacy raw array: [ … ] → wrap the entire array under Mails, in order.
        if (root.ValueKind == JsonValueKind.Array)
        {
            return new GlobalMailCollection
            {
                Mails = root.Deserialize<List<GlobalMailPayload>>(options) ?? new List<GlobalMailPayload>()
            };
        }

        // Expected object shape: { "Mails": [ … ] }.
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("Mails", out var mailsProp)
            && mailsProp.ValueKind == JsonValueKind.Array)
        {
            return new GlobalMailCollection
            {
                Mails = mailsProp.Deserialize<List<GlobalMailPayload>>(options) ?? new List<GlobalMailPayload>()
            };
        }

        // Foreign / null / malformed value — treat as no mails; the next write
        // overwrites the key with the correct { "Mails": [ … ] } shape.
        return new GlobalMailCollection();
    }

    public override void Write(Utf8JsonWriter writer, GlobalMailCollection value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("Mails");
        JsonSerializer.Serialize(writer, value.Mails ?? new List<GlobalMailPayload>(), options);
        writer.WriteEndObject();
    }
}

public static class GlobalMailStore
{
    public static GlobalMailPayload? FindById(List<GlobalMailPayload>? mails, string mailId)
    {
        return mails?.Find(m => string.Equals(m.Mail?.MessageId, mailId, StringComparison.OrdinalIgnoreCase));
    }

    public static int RemoveExpired(List<GlobalMailPayload> mails)
    {
        return mails.RemoveAll(m => m.Mail == null || m.Mail.IsExpired);
    }

    public static HashSet<string> LiveIds(List<GlobalMailPayload>? mails)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (mails == null) return result;
        foreach (var payload in mails)
        {
            if (!string.IsNullOrEmpty(payload.Mail?.MessageId))
                result.Add(payload.Mail.MessageId);
        }
        return result;
    }
}

public class PlayerGlobalMailState
{
    public List<MailMetadata> Mails { get; set; } = new();

    [JsonPropertyName("ClaimedIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? LegacyClaimedIds { get; set; }

    [JsonPropertyName("ReadIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? LegacyReadIds { get; set; }

    [JsonPropertyName("DeletedIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? LegacyDeletedIds { get; set; }
}

public class PlayerUserMailbox
{
    public List<MailItemDto> Mails { get; set; } = new();
}

public class PlayerMailboxMeta
{
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
    public List<GlobalMailItem> Mails { get; set; } = new();
}

public class Mail
{
    public string MessageId { get; set; } = string.Empty;
    public List<string>? TargetUserIds { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public List<Payout> Attachments { get; set; } = new();

    [JsonIgnore]
    public bool HasAttachment => Attachments != null && Attachments.Count > 0;

    [JsonIgnore]
    public bool IsExpired => EndTime.HasValue && DateTime.UtcNow > EndTime.Value;

    [JsonIgnore]
    public bool IsAvailable => DateTime.UtcNow >= StartTime && (!EndTime.HasValue || DateTime.UtcNow <= EndTime.Value);
}

public class Payout
{
    public string PayoutAssetId { get; set; } = string.Empty;
    public double Chance { get; set; } = 1.0;
    public string AssetType { get; set; } = string.Empty;
    public int PayoutAmount { get; set; }
}

public class MailMetadata
{
    public string MessageId { get; set; } = string.Empty;
    public bool IsClaim { get; set; }
    public bool IsRead { get; set; }
    public bool IsDelete { get; set; }
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
    public static Mail CreateAdminMail(
        string messageId,
        List<string>? targetUserIds,
        string title,
        string content,
        DateTime startTime,
        DateTime? endTime,
        List<MailAttachment>? attachments)
    {
        return new Mail
        {
            MessageId = messageId,
            TargetUserIds = targetUserIds == null || targetUserIds.Count == 0 ? null : new List<string>(targetUserIds),
            Title = title,
            Content = content,
            StartTime = startTime,
            EndTime = endTime,
            Attachments = MapPayouts(attachments)
        };
    }

    public static MailItemDto ToMailItemDto(Mail mail, MailMetadata? metadata)
    {
        var startTime = mail.StartTime.ToUniversalTime().ToString("o");
        var expireTime = mail.EndTime?.ToUniversalTime().ToString("o");
        return new MailItemDto
        {
            MessageId = mail.MessageId,
            MailInfo = new MailInfoDto
            {
                Title = mail.Title,
                Content = mail.Content,
                StartTime = startTime,
                Period = CalculatePeriodSeconds(startTime, expireTime),
                ExpireTime = expireTime,
                Attachment = MapAttachmentDtos(mail.Attachments)
            },
            MailMetaData = new MailMetaDataDto
            {
                IsRead = metadata?.IsRead ?? false,
                IsClaimed = metadata?.IsClaim ?? false,
                MailCategory = "System",
                SenderType = "Admin",
                Sender = null,
                DedupKey = null
            }
        };
    }

    public static bool IsVisibleToPlayer(Mail mail, string playerId)
    {
        return mail.TargetUserIds == null || mail.TargetUserIds.Count == 0 || mail.TargetUserIds.Contains(playerId);
    }

    public static MailMetadata GetOrCreateMetadata(PlayerGlobalMailState state, string mailId)
    {
        state.Mails ??= new List<MailMetadata>();
        MigrateLegacyMetadata(state);
        var metadata = state.Mails.Find(m => m.MessageId == mailId);
        if (metadata != null) return metadata;
        metadata = new MailMetadata { MessageId = mailId };
        state.Mails.Add(metadata);
        return metadata;
    }

    public static MailMetadata? FindMetadata(PlayerGlobalMailState state, string mailId)
    {
        state.Mails ??= new List<MailMetadata>();
        MigrateLegacyMetadata(state);
        return state.Mails.Find(m => m.MessageId == mailId);
    }

    public static void MigrateLegacyMetadata(PlayerGlobalMailState state)
    {
        state.Mails ??= new List<MailMetadata>();
        AddLegacyMetadata(state, state.LegacyClaimedIds, m => m.IsClaim = true);
        AddLegacyMetadata(state, state.LegacyReadIds, m => m.IsRead = true);
        AddLegacyMetadata(state, state.LegacyDeletedIds, m => m.IsDelete = true);
        state.LegacyClaimedIds = null;
        state.LegacyReadIds = null;
        state.LegacyDeletedIds = null;
    }

    private static void AddLegacyMetadata(PlayerGlobalMailState state, List<string>? ids, Action<MailMetadata> mutate)
    {
        if (ids == null) return;
        foreach (var id in ids)
        {
            var metadata = state.Mails.Find(m => m.MessageId == id);
            if (metadata == null)
            {
                metadata = new MailMetadata { MessageId = id };
                state.Mails.Add(metadata);
            }
            mutate(metadata);
        }
    }

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

    public static List<MailAttachment>? ToAttachments(List<Payout>? attachments)
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

    public static bool HasAttachments(Mail mail)
    {
        return mail.Attachments != null && mail.Attachments.Count > 0;
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

    public static List<Payout> MapPayouts(List<MailAttachment>? attachments)
    {
        var result = new List<Payout>();
        if (attachments == null || attachments.Count == 0) return result;
        foreach (var attachment in attachments)
        {
            result.Add(new Payout
            {
                PayoutAssetId = attachment.ItemId,
                Chance = 1.0,
                AssetType = NormalizeAssetTypeForDto(attachment.Type),
                PayoutAmount = attachment.Quantity
            });
        }
        return result;
    }

    public static List<MailAttachmentDto>? MapAttachmentDtos(List<Payout>? attachments)
    {
        if (attachments == null || attachments.Count == 0) return null;
        var result = new List<MailAttachmentDto>(attachments.Count);
        foreach (var attachment in attachments)
        {
            result.Add(new MailAttachmentDto
            {
                PayoutAssetId = attachment.PayoutAssetId,
                Chance = attachment.Chance,
                AssetType = NormalizeAssetTypeForDto(attachment.AssetType),
                PayoutAmount = attachment.PayoutAmount
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
    [JsonPropertyName("targetUserIds")]
    public List<string>? TargetUserIds { get; set; }

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
    [JsonPropertyName("targetUserIds")]
    public List<string>? TargetUserIds { get; set; }

    [JsonPropertyName("targetPlayerId")]
    public string TargetPlayerId { get; set; } = string.Empty;

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

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

public class SetMailEndTimeRequest
{
    [JsonPropertyName("mailId")]
    public string MailId { get; set; } = string.Empty;

    [JsonPropertyName("endTime")]
    public string? EndTime { get; set; }

    [JsonPropertyName("adminToken")]
    public string AdminToken { get; set; } = string.Empty;

    [JsonPropertyName("operatorId")]
    public string OperatorId { get; set; } = string.Empty;
}

public class AdminDeleteMailRequest
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

public class SetMailEndTimeResponse
{
    public string MailId { get; set; } = string.Empty;
    public string? EndTime { get; set; }
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

