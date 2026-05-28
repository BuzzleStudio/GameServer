using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BackpackAdventures.CloudCode;

// ────────────────────────────────────────────────────────────────────────────
// Constants
// ────────────────────────────────────────────────────────────────────────────

public static class MailboxConstants
{
    // v1 key — READ-ONLY fallback, one release compat layer. NEVER written.
    public const string KeyGlobalMailIndex = "global_mail_index";

    // v2 keys — active write path
    public const string KeyGlobalMailIndexV2 = "global_mail_index_v2";
    // per-mail payload key template: string.Format(KeyGlobalMailPayloadFmt, mailId)
    public const string KeyGlobalMailPayloadFmt = "mail_global_{0}";

    // Player-private keys
    public const string KeyGlobalState = "mailbox_global_state";
    public const string KeyUserItems   = "mailbox_user_items";
    public const string KeyMeta        = "mailbox_meta";
    public const string KeyIdemCache   = "mailbox_idem_cache";

    // Player wallet key used by CloudSaveRewardGrantService placeholder
    public const string KeyPlayerWallet = "player_wallet";

    // Mail caps
    public const int MaxUserMailsStored    = 200;  // soft cap — eviction triggered
    public const int HardCapUserMailsStored = 250;  // hard cap — reject insert

    // Global mail index cap
    public const int MaxGlobalMailRefs = 500;

    // Pagination
    public const int DefaultPageSize = 20;
    public const int MaxPageSize     = 50;

    // Idempotency cache
    public const int MaxIdemCacheEntries = 50;
    public const int IdemCacheTtlHours   = 24;

    // Gift daily quota
    public const int MaxGiftsPerDay = 5;

    // Validation
    public const int MaxSubjectLength = 128;
    public const int MaxBodyLength    = 1024;
}

// ────────────────────────────────────────────────────────────────────────────
// Enums
// ────────────────────────────────────────────────────────────────────────────

public enum MailType
{
    Notification,  // informational, no attachment
    Attachment     // has claimable items
}

public enum MailCategory
{
    System,       // automated system mail
    Event,        // limited-time event reward
    Compensation, // admin-issued compensation
    Gift,         // player-to-player gift
    Support,      // CS team support resolution
    PatchNote     // content patch notification
}

public enum SenderType
{
    System, // automated Cloud Code call
    Admin,  // admin operator authenticated via ADMIN_SERVICE_TOKEN
    Player  // player-to-player gift sender
}

// ────────────────────────────────────────────────────────────────────────────
// Attachment
// ────────────────────────────────────────────────────────────────────────────

public class MailAttachment
{
    [JsonPropertyName("itemId")]   public string ItemId   { get; set; } = string.Empty; // server-canonical name
    [JsonPropertyName("type")]     public string Type     { get; set; } = "none";       // "currency" | "item"
    [JsonPropertyName("quantity")] public int    Quantity { get; set; }
}

// ────────────────────────────────────────────────────────────────────────────
// Cloud Save storage models
// ────────────────────────────────────────────────────────────────────────────

// ── Global mail: v2 sharded index ──────────────────────────────────────────

public class GlobalMailRef
{
    public string MailId    { get; set; } = string.Empty;
    public string SentAt    { get; set; } = string.Empty;
    public string? ExpiresAt { get; set; }
    public int    Version   { get; set; } = 1;

    public bool IsExpired() =>
        ExpiresAt != null &&
        DateTime.TryParse(ExpiresAt, out var exp) &&
        exp < DateTime.UtcNow;
}

public class GlobalMailIndexV2
{
    public int Version { get; set; } = 2;
    public List<GlobalMailRef> Refs { get; set; } = new();
}

// Per-mail payload stored under mail_global_{mailId}
public class GlobalMailPayload
{
    public string  MailId       { get; set; } = string.Empty;
    public string  Subject      { get; set; } = string.Empty;
    public string  Body         { get; set; } = string.Empty;
    public string  SentAt       { get; set; } = string.Empty;
    public string? ExpiresAt    { get; set; }
    public MailType     MailType     { get; set; }
    public MailCategory MailCategory { get; set; } = MailCategory.System;
    public SenderType   SenderType   { get; set; }
    public string? Sender      { get; set; }
    public string? DedupKey    { get; set; }
    public List<MailAttachment>? Attachments { get; set; }
    public int     Version     { get; set; } = 1;

    public bool IsExpired() =>
        ExpiresAt != null &&
        DateTime.TryParse(ExpiresAt, out var exp) &&
        exp < DateTime.UtcNow;
}

// ── v1 legacy read-compat (READ-ONLY fallback) ─────────────────────────────

public class GlobalMailItem
{
    public string GlobalMailId { get; set; } = string.Empty;
    public string Subject      { get; set; } = string.Empty;
    public string Body         { get; set; } = string.Empty;
    public string SentAt       { get; set; } = string.Empty;
    public string? ExpiresAt   { get; set; }
    public List<MailAttachment>? Attachments { get; set; }

    public bool IsExpired() =>
        ExpiresAt != null &&
        DateTime.TryParse(ExpiresAt, out var exp) &&
        exp < DateTime.UtcNow;
}

public class GlobalMailIndex
{
    public int Version { get; set; } = 1;
    public List<GlobalMailItem> Mails { get; set; } = new();
}

// ── Player global mail state (v2) ──────────────────────────────────────────

public class PlayerGlobalMailState
{
    public int Version { get; set; } = 2;
    public List<string> ClaimedIds { get; set; } = new();
    public List<string> ReadIds    { get; set; } = new();
}

// ── User mail item (v2) ────────────────────────────────────────────────────

public class UserMailItem
{
    public string  MailId            { get; set; } = string.Empty;
    public string  Subject           { get; set; } = string.Empty;
    public string  Body              { get; set; } = string.Empty;
    public string  SentAt            { get; set; } = string.Empty;
    public string? ExpiresAt         { get; set; }
    public bool    IsRead            { get; set; }
    public bool    AttachmentClaimed { get; set; }
    public MailType     MailType     { get; set; }
    public MailCategory MailCategory { get; set; } = MailCategory.System;
    public SenderType   SenderType   { get; set; }
    public string? Sender            { get; set; }
    public string? DedupKey          { get; set; }
    public List<MailAttachment>? Attachments { get; set; }

    public bool IsExpired() =>
        ExpiresAt != null &&
        DateTime.TryParse(ExpiresAt, out var exp) &&
        exp < DateTime.UtcNow;
}

public class PlayerUserMailbox
{
    public int Version { get; set; } = 2;
    public List<UserMailItem> Mails { get; set; } = new();
}

// ── Player mailbox meta (v2) ───────────────────────────────────────────────

public class PlayerMailboxMeta
{
    public int     Version           { get; set; } = 2;
    public string? LastReadAt        { get; set; }
    public int     TotalUserMails    { get; set; }
    public int     TotalGlobalMails  { get; set; }
    public int     PendingRewardCount { get; set; }
    public string? LastPurgeAt       { get; set; }
    public int     GiftsSentToday    { get; set; }
    public string? LastGiftResetAt   { get; set; }
}

// ── Idempotency cache ──────────────────────────────────────────────────────

public class IdemCacheEntry
{
    public string  RequestId      { get; set; } = string.Empty;
    public string  Operation      { get; set; } = string.Empty;
    public string  MailId         { get; set; } = string.Empty;
    public string  ResolvedAt     { get; set; } = string.Empty;
    public object? ResponseSummary { get; set; }
}

public class IdemCache
{
    public int Version { get; set; } = 1;
    public List<IdemCacheEntry> Entries { get; set; } = new();
}

// ────────────────────────────────────────────────────────────────────────────
// API DTOs
// ────────────────────────────────────────────────────────────────────────────

public class MailItemDto
{
    public string  MailId            { get; set; } = string.Empty;
    public string  Subject           { get; set; } = string.Empty;
    public string  Body              { get; set; } = string.Empty;
    public bool    IsRead            { get; set; }
    public bool    AttachmentClaimed { get; set; }
    public string  SentAt            { get; set; } = string.Empty;
    public string? ExpiresAt         { get; set; }
    public MailType     MailType     { get; set; }
    public MailCategory MailCategory { get; set; }
    public SenderType   SenderType   { get; set; }
    public string? Sender            { get; set; }
    public List<MailAttachment>? Attachments { get; set; }
}

// ── Paginated response ─────────────────────────────────────────────────────

public class PagedMailResponse
{
    public bool          Success    { get; set; }
    public List<MailItemDto> Mails  { get; set; } = new();
    public int           TotalCount { get; set; }
    public int           Page       { get; set; }
    public int           PageSize   { get; set; }
    public bool          HasMore    { get; set; }
}

// ── Request types ──────────────────────────────────────────────────────────

public class SendGlobalMailRequest
{
    [JsonPropertyName("subject")]      public string Subject            { get; set; } = string.Empty;
    [JsonPropertyName("body")]         public string Body               { get; set; } = string.Empty;
    [JsonPropertyName("expiresAt")]    public string? ExpiresAt         { get; set; }
    [JsonPropertyName("mailCategory")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MailCategory MailCategory { get; set; } = MailCategory.System;
    [JsonPropertyName("senderName")]   public string? SenderName        { get; set; }
    [JsonPropertyName("dedupKey")]     public string? DedupKey          { get; set; }
    [JsonPropertyName("attachments")]  public List<MailAttachment>? Attachments { get; set; }
    [JsonPropertyName("adminToken")]   public string AdminToken         { get; set; } = string.Empty;
    [JsonPropertyName("operatorId")]   public string OperatorId         { get; set; } = string.Empty;
}

public class SendUserMailRequest
{
    [JsonPropertyName("targetPlayerId")] public string TargetPlayerId { get; set; } = string.Empty;
    [JsonPropertyName("subject")]        public string Subject        { get; set; } = string.Empty;
    [JsonPropertyName("body")]           public string Body           { get; set; } = string.Empty;
    [JsonPropertyName("expiresAt")]      public string? ExpiresAt     { get; set; }
    [JsonPropertyName("mailCategory")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MailCategory MailCategory { get; set; } = MailCategory.System;
    [JsonPropertyName("senderName")]     public string? SenderName    { get; set; }
    [JsonPropertyName("dedupKey")]       public string? DedupKey      { get; set; }
    [JsonPropertyName("attachments")]    public List<MailAttachment>? Attachments { get; set; }
    [JsonPropertyName("adminToken")]     public string AdminToken     { get; set; } = string.Empty;
    [JsonPropertyName("operatorId")]     public string OperatorId     { get; set; } = string.Empty;
}

public class GiftMailRequest
{
    [JsonPropertyName("targetPlayerId")] public string TargetPlayerId { get; set; } = string.Empty;
    [JsonPropertyName("subject")]        public string Subject        { get; set; } = string.Empty;
    [JsonPropertyName("body")]           public string Body           { get; set; } = string.Empty;
}

public class GetMailsRequest
{
    [JsonPropertyName("page")]     public int Page     { get; set; } = 0;
    [JsonPropertyName("pageSize")] public int PageSize { get; set; } = MailboxConstants.DefaultPageSize;
}

public class MarkMailReadRequest
{
    [JsonPropertyName("mailId")]    public string MailId    { get; set; } = string.Empty;
    [JsonPropertyName("mailType")]  public string MailType  { get; set; } = string.Empty; // "global" | "user"
    [JsonPropertyName("requestId")] public string? RequestId { get; set; }
}

public class ClaimAttachmentRequest
{
    [JsonPropertyName("mailId")]    public string MailId    { get; set; } = string.Empty;
    [JsonPropertyName("mailType")]  public string MailType  { get; set; } = string.Empty; // "global" | "user"
    [JsonPropertyName("requestId")] public string? RequestId { get; set; }
}

public class DeleteMailRequest
{
    [JsonPropertyName("mailId")] public string MailId { get; set; } = string.Empty;
}

// ── Response types ─────────────────────────────────────────────────────────

public class SendGlobalMailResponse
{
    public bool   Success      { get; set; }
    public string GlobalMailId { get; set; } = string.Empty;
    public string SentAt       { get; set; } = string.Empty;
}

public class SendUserMailResponse
{
    public bool   Success { get; set; }
    public string MailId  { get; set; } = string.Empty;
    public string SentAt  { get; set; } = string.Empty;
}

public class GiftMailResponse
{
    public bool   Success { get; set; }
    public string MailId  { get; set; } = string.Empty;
    public string SentAt  { get; set; } = string.Empty;
}

public class GetMailboxResponse
{
    public bool Success { get; set; }
    public List<MailItemDto> Mails { get; set; } = new();
}

public class MarkMailReadResponse
{
    public bool   Success { get; set; }
    public string MailId  { get; set; } = string.Empty;
    public bool   IsRead  { get; set; }
}

public class MarkAllReadResponse
{
    public bool   Success    { get; set; }
    public string LastReadAt { get; set; } = string.Empty;
}

public class ClaimAttachmentResponse
{
    public bool   Success          { get; set; }
    public string MailId           { get; set; } = string.Empty;
    public bool   AlreadyClaimed   { get; set; }
    public List<MailAttachment>? GrantedAttachments { get; set; }
}

public class DeleteMailResponse
{
    public bool   Success { get; set; }
    public string MailId  { get; set; } = string.Empty;
}

public class PurgeExpiredResponse
{
    public bool Success      { get; set; }
    public int  PurgedCount  { get; set; }
    public string PurgedAt   { get; set; } = string.Empty;
}

public class PurgeExpiredRequest
{
    [JsonPropertyName("adminToken")]  public string AdminToken  { get; set; } = string.Empty;
    [JsonPropertyName("operatorId")]  public string OperatorId  { get; set; } = string.Empty;
}

// ── Error codes ────────────────────────────────────────────────────────────

public static class MailboxError
{
    public const string InvalidInput              = "InvalidInput";
    public const string MailNotFound              = "MailNotFound";
    public const string MailExpired               = "MailExpired";
    public const string AlreadyClaimed            = "AlreadyClaimed";
    public const string NoAttachment              = "NoAttachment";
    public const string Unauthorized              = "Unauthorized";
    public const string MailboxFull               = "MailboxFull";
    public const string Conflict                  = "Conflict";
    public const string GrantUnavailable          = "GrantUnavailable";
    public const string GiftQuotaExceeded         = "GiftQuotaExceeded";
    public const string CannotDeleteUnclaimedReward = "CannotDeleteUnclaimedReward";
    public const string CannotDeleteGlobal        = "CannotDeleteGlobal";
    public const string TargetMailboxFull         = "TargetMailboxFull";
}
