using System;
using System.Collections.Generic;

namespace BackpackAdventures.CloudCode.Client
{
    [Serializable]
    public class HealthCheckResponse
    {
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

    [Serializable]
    public class MailAttachment
    {
        public string type;
        public string id;
        public int amount;
        public string itemId;
        public int quantity;
    }

    [Serializable]
    public class SendGlobalMailRequest
    {
        public string subject;
        public string body;
        public string expiresAt;
        public string mailCategory;
        public string senderName;
        public string dedupKey;
        public List<MailAttachment> attachments;
        public string adminToken;
        public string operatorId;
    }

    [Serializable]
    public class SendGlobalMailResponse
    {
        public string mailId;
        public string globalMailId;
        public string sentAt;
    }

    [Serializable]
    public class SendUserMailRequest
    {
        public string targetPlayerId;
        public string userId;
        public string subject;
        public string body;
        public string expiresAt;
        public string mailCategory;
        public string senderName;
        public string dedupKey;
        public List<MailAttachment> attachments;
        public string adminToken;
        public string operatorId;
    }

    [Serializable]
    public class SendUserMailResponse
    {
        public string mailId;
        public string sentAt;
    }

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
        public string mailId;
        public string sentAt;
    }

    [Serializable]
    public class MailAttachmentInfo
    {
        public string PayoutAssetId;
        public double Chance;
        public string AssetType;
        public int PayoutAmount;
    }

    [Serializable]
    public class MailInfo
    {
        public string Title;
        public string Content;
        public string StartTime;
        public int Period;
        public string ExpireTime;
        public List<MailAttachmentInfo> Attachment;
    }
    [Serializable]
    public class MailMetaData
    {
        public bool IsRead;
        public bool IsClaimed;
        public string MailCategory;
        public string SenderType;
        public string Sender;
        public string DedupKey;
    }
    [Serializable]
    public class MailItem
    {
        public string MessageId;
        public MailInfo MailInfo;
        public MailMetaData MailMetaData;

        public string mailId => MessageId;
        public string subject => MailInfo?.Title;
        public string body => MailInfo?.Content;
        public bool isRead => MailMetaData != null && MailMetaData.IsRead;
        public bool attachmentClaimed => MailMetaData != null && MailMetaData.IsClaimed;
        public string sentAt => MailInfo?.StartTime;
        public string expiresAt => MailInfo?.ExpireTime;
        public string mailType => (MailInfo?.Attachment != null && MailInfo.Attachment.Count > 0) ? "Attachment" : "Notification";
        public string mailCategory => MailMetaData?.MailCategory;
        public string senderType => MailMetaData?.SenderType;
        public string sender => MailMetaData?.Sender;
        public string dedupKey => MailMetaData?.DedupKey;
        public List<MailAttachment> attachments
        {
            get
            {
                if (MailInfo?.Attachment == null) return null;
                var result = new List<MailAttachment>(MailInfo.Attachment.Count);
                foreach (var item in MailInfo.Attachment)
                {
                    result.Add(new MailAttachment
                    {
                        itemId = item.PayoutAssetId,
                        id = item.PayoutAssetId,
                        quantity = item.PayoutAmount,
                        amount = item.PayoutAmount,
                        type = item.AssetType?.ToLowerInvariant()
                    });
                }
                return result;
            }
        }
    }

    [Serializable]
    public class GetMailboxResponse
    {
        public List<MailItem> mails;
    }

    [Serializable]
    public class GetMailboxPageRequest
    {
        public int page;
        public int pageSize;
    }

    [Serializable]
    public class GetMailboxPageResponse
    {
        public List<MailItem> mails;
        public int totalCount;
        public int page;
        public int pageSize;
        public bool hasMore;
    }

    [Serializable]
    public class MarkMailReadRequest
    {
        public string mailId;
        public string mailType;
    }

    [Serializable]
    public class MarkMailReadResponse
    {
        public string mailId;
        public bool isRead;
    }

    [Serializable]
    public class MarkAllReadResponse
    {
        public string lastReadAt;
    }

    [Serializable]
    public class ClaimAttachmentRequest
    {
        public string mailId;
        public string mailType;
        public string requestId;
    }

    [Serializable]
    public class ClaimAttachmentResponse
    {
        public string mailId;
        public bool alreadyClaimed;
        public List<MailAttachment> claimedAttachments;
        public List<MailAttachment> grantedAttachments;
    }

    [Serializable]
    public class DeleteMailRequest
    {
        public string mailId;
    }

    [Serializable]
    public class DeleteMailResponse
    {
        public string mailId;
    }

    [Serializable]
    public class ExpireMailRequest
    {
        public string mailId;
        public string adminToken;
        public string operatorId;
    }

    [Serializable]
    public class ExpireMailResponse
    {
        public string mailId;
    }

    [Serializable]
    public class PurgeExpiredResponse
    {
        public int purgedCount;
        public string purgedAt;
    }

    [Serializable]
    public class PurgeExpiredRequest
    {
        public string adminToken;
        public string operatorId;
    }
}


