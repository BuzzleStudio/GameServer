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

    // --- Mailbox models ---

    [Serializable]
    public class MailAttachment
    {
        public string type;   // e.g. "currency", "item"
        public string id;
        public int amount;
    }

    [Serializable]
    public class SendGlobalMailRequest
    {
        public string subject;
        public string body;
        public string expiresAt;                  // ISO 8601 UTC, nullable
        public List<MailAttachment> attachments;  // nullable
    }

    [Serializable]
    public class SendGlobalMailResponse
    {
        public bool success;
        public string mailId;
        public string sentAt;
    }

    [Serializable]
    public class SendUserMailRequest
    {
        public string userId;
        public string subject;
        public string body;
        public string expiresAt;
        public List<MailAttachment> attachments;
    }

    [Serializable]
    public class SendUserMailResponse
    {
        public bool success;
        public string mailId;
        public string sentAt;
    }

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
        public List<MailAttachment> attachments;
    }

    [Serializable]
    public class GetMailboxResponse
    {
        public bool success;
        public List<MailItem> mails;
    }

    [Serializable]
    public class MarkMailReadRequest
    {
        public string mailId;
    }

    [Serializable]
    public class MarkMailReadResponse
    {
        public bool success;
        public string mailId;
        public bool isRead;
    }

    [Serializable]
    public class ClaimAttachmentRequest
    {
        public string mailId;
    }

    [Serializable]
    public class ClaimAttachmentResponse
    {
        public bool success;
        public string mailId;
        public bool alreadyClaimed;
        public List<MailAttachment> claimedAttachments;
    }
}
