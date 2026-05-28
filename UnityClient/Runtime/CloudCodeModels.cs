using System;
using System.Collections.Generic;

namespace BackpackAdventures.CloudCode.Client
{
    [Serializable]
    public class HealthCheckResponse
    {
        public bool success;
        public string message;
        public string timestamp;
    }

    [Serializable]
    public class PlayerEchoRequest
    {
        public string playerId;
    }

    [Serializable]
    public class PlayerEchoResponse
    {
        public bool success;
        public string playerId;
        public string serverTime;
    }

    [Serializable]
    public class ServerConfigResponse
    {
        public string environment;
        public string version;
        public string deploymentTime;
    }

    // --- Mailbox enums (§5.2) ---

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

    // --- Mailbox attachment (v2 — server-canonical field names: itemId / quantity) ---

    [Serializable]
    public class MailAttachment
    {
        // v1 legacy fields — kept for backward compatibility with existing callers
        public string type;   // "currency" or "item"
        /// <summary>Legacy field. Use <see cref="itemId"/> for v2 backend calls.</summary>
        public string id;
        /// <summary>Legacy field. Use <see cref="quantity"/> for v2 backend calls.</summary>
        public int amount;

        // v2 server-canonical fields (§5.1 MailboxModels.cs contract)
        public string itemId;
        public int quantity;
    }

    // --- Send Global Mail (v2 — admin-gated, §5.3) ---

    [Serializable]
    public class SendGlobalMailRequest
    {
        public string subject;
        public string body;
        public string expiresAt;                  // ISO 8601 UTC, nullable
        public string mailCategory;               // MailCategory enum string, optional (default: System)
        public string senderName;                 // optional human-readable sender, e.g. "GM_Ninh"
        public string dedupKey;                   // optional idempotency key
        public List<MailAttachment> attachments;  // nullable
    }

    [Serializable]
    public class SendGlobalMailResponse
    {
        public bool success;
        public string mailId;
        public string globalMailId;               // server may return either field name
        public string sentAt;
    }

    // --- Send User Mail (v2 — admin-gated, §5.3) ---

    [Serializable]
    public class SendUserMailRequest
    {
        public string targetPlayerId;             // v2 canonical name
        /// <summary>Legacy alias. Prefer <see cref="targetPlayerId"/>.</summary>
        public string userId;
        public string subject;
        public string body;
        public string expiresAt;
        public string mailCategory;
        public string senderName;
        public string dedupKey;
        public List<MailAttachment> attachments;
    }

    [Serializable]
    public class SendUserMailResponse
    {
        public bool success;
        public string mailId;
        public string sentAt;
    }

    // --- Gift Mail (any player, §5.3 GiftMail restrictions) ---

    [Serializable]
    public class GiftMailRequest
    {
        public string targetPlayerId;
        public string subject;
        public string body;
    }

    [Serializable]
    public class GiftMailResponse
    {
        public bool success;
        public string mailId;
        public string sentAt;
    }

    // --- Mail item v2 (paginated list element) ---

    [Serializable]
    public class MailItem
    {
        public string mailId;
        public string subject;
        public string body;
        public bool isRead;
        public bool attachmentClaimed;
        public string sentAt;
        public string expiresAt;
        public string mailType;       // MailType enum string
        public string mailCategory;   // MailCategory enum string
        public string senderType;     // SenderType enum string
        public string sender;         // optional display name
        public string dedupKey;       // optional
        public List<MailAttachment> attachments;
    }

    // --- Get Mailbox (legacy, kept for backward compatibility) ---

    [Serializable]
    public class GetMailboxResponse
    {
        public bool success;
        public List<MailItem> mails;
    }

    // --- Paginated mail responses (§5.6) ---

    [Serializable]
    public class GetMailboxPageRequest
    {
        public int page;
        public int pageSize;
    }

    [Serializable]
    public class GetMailboxPageResponse
    {
        public bool success;
        public List<MailItem> mails;
        public int totalCount;
        public int page;
        public int pageSize;
        public bool hasMore;
    }

    // --- Mark Mail Read (v2 — requires mailType, §5.11) ---

    [Serializable]
    public class MarkMailReadRequest
    {
        public string mailId;
        public string mailType;  // MailType enum string (required in v2)
    }

    [Serializable]
    public class MarkMailReadResponse
    {
        public bool success;
        public string mailId;
        public bool isRead;
    }

    // --- Mark All Read ---

    [Serializable]
    public class MarkAllReadResponse
    {
        public bool success;
        public string lastReadAt;
    }

    // --- Claim Attachment (v2 — with mailType and optional requestId, §5.8) ---

    [Serializable]
    public class ClaimAttachmentRequest
    {
        public string mailId;
        public string mailType;   // MailType enum string (required in v2)
        public string requestId;  // optional idempotency GUID
    }

    [Serializable]
    public class ClaimAttachmentResponse
    {
        public bool success;
        public string mailId;
        public bool alreadyClaimed;
        public List<MailAttachment> claimedAttachments;
        public List<MailAttachment> grantedAttachments;  // v2 alias
    }

    // --- Delete Mail (§5.11, any player — user mail only) ---

    [Serializable]
    public class DeleteMailRequest
    {
        public string mailId;
    }

    [Serializable]
    public class DeleteMailResponse
    {
        public bool success;
        public string mailId;
    }

    // --- Expire Mail (admin — §5.11) ---

    [Serializable]
    public class ExpireMailRequest
    {
        public string mailId;
    }

    [Serializable]
    public class ExpireMailResponse
    {
        public bool success;
        public string mailId;
    }

    // --- Purge Expired (admin-gated, §5.9) ---

    [Serializable]
    public class PurgeExpiredResponse
    {
        public bool success;
        public int purgedCount;
        public string purgedAt;
    }

    // --- Admin allowlist (read-only display, §5.1) ---

    [Serializable]
    public class AdminAllowlistResponse
    {
        public bool success;
        public int version;
        public List<string> playerIds;
    }
}
