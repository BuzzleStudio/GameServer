using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;

namespace BackpackAdventures.CloudCode.Client
{
    public static class BackpackCloudCodeService
    {
        public static ICloudCodeBackend Backend { get; set; } = new UnityCloudCodeBackend();

        public static void ResetToDefault()
        {
            Backend = new UnityCloudCodeBackend();
        }

        public static async Task InitializeAsync()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
                await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        public static async Task<HealthCheckResponse> CallHealthCheckAsync()
        {
            return await CallEndpointDataAsync<HealthCheckResponse>("HealthCheck", null);
        }

        public static async Task<PlayerEchoResponse> CallPlayerEchoAsync(string playerId)
        {
            var request = new PlayerEchoRequest { playerId = playerId };
            return await CallEndpointDataAsync<PlayerEchoResponse>("PlayerEcho", request);
        }

        public static async Task<ServerConfigResponse> CallServerConfigAsync()
        {
            return await CallEndpointDataAsync<ServerConfigResponse>("ServerConfig", null);
        }

        // --- Legacy v1 endpoints (kept for backward compatibility) ---

        public static async Task<GetMailboxResponse> GetMailboxAsync()
        {
            return await CallEndpointDataAsync<GetMailboxResponse>("GetMailbox", null);
        }

        public static async Task<MarkMailReadResponse> MarkMailReadAsync(string mailId)
        {
            var request = new MarkMailReadRequest { mailId = mailId };
            return await CallEndpointDataAsync<MarkMailReadResponse>("MarkMailRead", request);
        }

        public static async Task<ClaimAttachmentResponse> ClaimAttachmentAsync(string mailId)
        {
            var request = new ClaimAttachmentRequest { mailId = mailId };
            return await CallEndpointDataAsync<ClaimAttachmentResponse>("ClaimAttachment", request);
        }

        // --- Mailbox API v2 ---

        public static async Task<GetMailboxPageResponse> CallGetMailboxAsync(int page = 0, int pageSize = 20)
        {
            var request = new GetMailboxPageRequest { page = page, pageSize = pageSize };
            return await CallEndpointDataAsync<GetMailboxPageResponse>("GetUserMails", request);
        }

        public static async Task<GetMailboxPageResponse> CallGetGlobalMailsAsync(int page = 0, int pageSize = 20)
        {
            var request = new GetMailboxPageRequest { page = page, pageSize = pageSize };
            return await CallEndpointDataAsync<GetMailboxPageResponse>("GetGlobalMails", request);
        }

        public static async Task<MarkMailReadResponse> CallMarkMailReadAsync(string mailId, string mailType)
        {
            var request = new MarkMailReadRequest { mailId = mailId, mailType = ResolveMailType(mailId, mailType) };
            return await CallEndpointDataAsync<MarkMailReadResponse>("MarkMailRead", request);
        }

        public static async Task<MarkAllReadResponse> CallMarkAllReadAsync()
        {
            return await CallEndpointDataAsync<MarkAllReadResponse>("MarkAllRead", null);
        }

        public static async Task<ClaimAttachmentResponse> CallClaimAttachmentAsync(
            string mailId, string mailType, string requestId = null)
        {
            var request = new ClaimAttachmentRequest
            {
                mailId = mailId,
                mailType = ResolveMailType(mailId, mailType),
                requestId = requestId
            };
            return await CallEndpointDataAsync<ClaimAttachmentResponse>("ClaimAttachment", request);
        }

        public static async Task<ClaimAllAttachmentsResponse> CallClaimAllAttachmentsAsync(
            string mailType = "all", string requestId = null)
        {
            var request = new ClaimAllAttachmentsRequest
            {
                mailType = string.IsNullOrEmpty(mailType) ? "all" : mailType,
                requestId = requestId
            };
            return await CallEndpointDataAsync<ClaimAllAttachmentsResponse>("ClaimAllAttachments", request);
        }

        public static async Task<SendGlobalMailResponse> CallAdminSendGlobalMailAsync(
            string subject,
            string body,
            string expiresAt = null,
            string mailCategory = null,
            string senderName = null,
            string dedupKey = null,
            List<MailAttachment> attachments = null,
            string adminToken = null,
            string operatorId = null,
            List<string> targetUserIds = null)
        {
            var request = new SendGlobalMailRequest
            {
                targetUserIds = NormalizeTargetUserIds(targetUserIds),
                subject = subject,
                body = body,
                expiresAt = expiresAt,
                mailCategory = string.IsNullOrEmpty(mailCategory) ? "System" : mailCategory,
                senderName = senderName,
                dedupKey = dedupKey,
                attachments = attachments,
                adminToken = adminToken ?? string.Empty,
                operatorId = operatorId ?? string.Empty
            };
            return await CallEndpointDataAsync<SendGlobalMailResponse>("SendGlobalMail", request);
        }

        public static async Task<SendUserMailResponse> CallAdminSendUserMailAsync(
            string targetPlayerId,
            string subject,
            string body,
            string expiresAt = null,
            string mailCategory = null,
            string senderName = null,
            string dedupKey = null,
            List<MailAttachment> attachments = null,
            string adminToken = null,
            string operatorId = null)
        {
            var request = new SendGlobalMailRequest
            {
                targetUserIds = NormalizeTargetUserIds(new List<string> { targetPlayerId }),
                subject = subject,
                body = body,
                expiresAt = expiresAt,
                mailCategory = string.IsNullOrEmpty(mailCategory) ? "System" : mailCategory,
                senderName = senderName,
                dedupKey = dedupKey,
                attachments = attachments,
                adminToken = adminToken ?? string.Empty,
                operatorId = operatorId ?? string.Empty
            };
            var response = await CallEndpointDataAsync<SendGlobalMailResponse>("SendGlobalMail", request);
            return new SendUserMailResponse
            {
                mailId = string.IsNullOrEmpty(response.mailId) ? response.globalMailId : response.mailId,
                sentAt = response.sentAt
            };
        }

        public static async Task<GiftMailResponse> CallUserSendGiftMailAsync(
            string targetPlayerId, string subject, string body)
        {
            var request = new GiftMailRequest
            {
                targetPlayerId = targetPlayerId,
                subject = subject,
                body = body
            };
            return await CallEndpointDataAsync<GiftMailResponse>("GiftMail", request);
        }

        public static async Task<DeleteMailResponse> CallDeleteMailAsync(string mailId)
        {
            var request = new DeleteMailRequest { mailId = mailId };
            return await CallEndpointDataAsync<DeleteMailResponse>("DeleteMail", request);
        }

        public static async Task<ExpireMailResponse> CallExpireMailAsync(
            string mailId,
            string adminToken = null,
            string operatorId = null)
        {
            var request = new ExpireMailRequest
            {
                mailId = mailId,
                adminToken = adminToken ?? string.Empty,
                operatorId = operatorId ?? string.Empty
            };
            return await CallEndpointDataAsync<ExpireMailResponse>("ExpireMail", request);
        }

        public static async Task<SetMailEndTimeResponse> CallSetMailEndTimeAsync(
            string mailId,
            string endTime,
            string adminToken = null,
            string operatorId = null)
        {
            var request = new SetMailEndTimeRequest
            {
                mailId = mailId,
                endTime = endTime,
                adminToken = adminToken ?? string.Empty,
                operatorId = operatorId ?? string.Empty
            };
            return await CallEndpointDataAsync<SetMailEndTimeResponse>("SetMailEndTime", request);
        }

        public static async Task<DeleteMailResponse> CallAdminDeleteGlobalMailAsync(
            string mailId,
            string adminToken = null,
            string operatorId = null)
        {
            var request = new AdminDeleteMailRequest
            {
                mailId = mailId,
                adminToken = adminToken ?? string.Empty,
                operatorId = operatorId ?? string.Empty
            };
            return await CallEndpointDataAsync<DeleteMailResponse>("DeleteGlobalMail", request);
        }

        public static async Task<PurgeExpiredResponse> CallPurgeExpiredAsync(
            string adminToken = null,
            string operatorId = null)
        {
            var request = new PurgeExpiredRequest
            {
                adminToken = adminToken ?? string.Empty,
                operatorId = operatorId ?? string.Empty
            };
            return await CallEndpointDataAsync<PurgeExpiredResponse>("PurgeExpired", request);
        }

        public static async Task<ApiResponse<TData>> CallApiResponseAsync<TData>(string endpoint, object request)
        {
            return await Backend.CallEndpointAsync<ApiResponse<TData>>(endpoint, request);
        }

        private static async Task<TData> CallEndpointDataAsync<TData>(string endpoint, object request)
        {
            return await Backend.CallEndpointAsync<TData>(endpoint, request);
        }

        private static List<string> NormalizeTargetUserIds(List<string> targetUserIds)
        {
            if (targetUserIds == null || targetUserIds.Count == 0)
                return null;

            var result = new List<string>();
            foreach (string targetUserId in targetUserIds)
            {
                if (string.IsNullOrWhiteSpace(targetUserId))
                    continue;

                string normalized = targetUserId.Trim();
                if (!result.Contains(normalized))
                    result.Add(normalized);
            }

            return result.Count == 0 ? null : result;
        }

        private static string ResolveMailType(string mailId, string mailType)
        {
            if (!string.IsNullOrEmpty(mailId) && mailId.StartsWith("gm_", System.StringComparison.OrdinalIgnoreCase))
                return "global";

            return string.IsNullOrEmpty(mailType) ? "user" : mailType;
        }
    }
}
