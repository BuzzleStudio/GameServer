using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BackpackAdventures.CloudCode.Client
{
    public class ApiResponse
    {
        [JsonProperty("statusCode")]
        public int StatusCode { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; } = string.Empty;

        // The Cloud Code SDK may deserialize with MissingMemberHandling.Error.
        // Keep data as a real public member so non-generic ApiResponse callers can
        // receive the envelope without failing on Path 'data'.
        [JsonProperty("data")]
        public JToken Data { get; set; }
    }

    public class ApiResponse<T> : ApiResponse
    {
        [JsonIgnore]
        public new T Data
        {
            get
            {
                JToken data = base.Data;
                if (data == null || data.Type == JTokenType.Null)
                    return default;

                return data.ToObject<T>();
            }
            set
            {
                base.Data = value == null ? null : JToken.FromObject(value);
            }
        }

        public static ApiResponse<T> Ok(T data, string message = "OK")
        {
            return new ApiResponse<T>
            {
                StatusCode = 200,
                Message = message,
                Data = data
            };
        }
    }

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
        public List<string> targetUserIds;
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
        public List<string> targetUserIds;
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
    public class ClaimAllAttachmentsRequest
    {
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
    public class ClaimAllAttachmentResult
    {
        public string mailId;
        public string mailType;
        public bool alreadyClaimed;
        public string skippedReason;
        public List<MailAttachment> grantedAttachments;
    }

    [Serializable]
    public class ClaimAllAttachmentsResponse
    {
        public int claimedCount;
        public int alreadyClaimedCount;
        public int skippedCount;
        public List<ClaimAllAttachmentResult> results;
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
    public class SetMailEndTimeRequest
    {
        public string mailId;
        public string endTime;
        public string adminToken;
        public string operatorId;
    }

    [Serializable]
    public class AdminDeleteMailRequest
    {
        public string mailId;
        public string adminToken;
        public string operatorId;
    }

    [Serializable]
    public class ExpireMailResponse
    {
        public string mailId;
        public string expiredAt;
    }

    [Serializable]
    public class SetMailEndTimeResponse
    {
        public string mailId;
        public string endTime;
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

    [Serializable] public class HealthCheckData : HealthCheckResponse { }
    [Serializable] public class PlayerEchoData : PlayerEchoResponse { }
    [Serializable] public class ServerConfigData : ServerConfigResponse { }
    [Serializable] public class SendGlobalMailData : SendGlobalMailResponse { }
    [Serializable] public class SendUserMailData : SendUserMailResponse { }
    [Serializable] public class GiftMailData : GiftMailResponse { }
    [Serializable] public class GetMailboxData : GetMailboxResponse { }
    [Serializable] public class GetMailboxPageData : GetMailboxPageResponse { }
    [Serializable] public class MarkMailReadData : MarkMailReadResponse { }
    [Serializable] public class MarkAllReadData : MarkAllReadResponse { }
    [Serializable] public class ClaimAttachmentData : ClaimAttachmentResponse { }
    [Serializable] public class ClaimAllAttachmentsData : ClaimAllAttachmentsResponse { }
    [Serializable] public class DeleteMailData : DeleteMailResponse { }
    [Serializable] public class ExpireMailData : ExpireMailResponse { }
    [Serializable] public class SetMailEndTimeData : SetMailEndTimeResponse { }
    [Serializable] public class PurgeExpiredData : PurgeExpiredResponse { }
}

