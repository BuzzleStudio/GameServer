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
            return await Backend.CallEndpointAsync<HealthCheckResponse>("HealthCheck", null);
        }

        public static async Task<PlayerEchoResponse> CallPlayerEchoAsync(string playerId)
        {
            var request = new PlayerEchoRequest { playerId = playerId };
            return await Backend.CallEndpointAsync<PlayerEchoResponse>("PlayerEcho", request);
        }

        public static async Task<ServerConfigResponse> CallServerConfigAsync()
        {
            return await Backend.CallEndpointAsync<ServerConfigResponse>("ServerConfig", null);
        }

        // --- Legacy v1 endpoints (kept for backward compatibility) ---

        public static async Task<GetMailboxResponse> GetMailboxAsync()
        {
            return await Backend.CallEndpointAsync<GetMailboxResponse>("GetMailbox", null);
        }

        public static async Task<MarkMailReadResponse> MarkMailReadAsync(string mailId)
        {
            var request = new MarkMailReadRequest { mailId = mailId };
            return await Backend.CallEndpointAsync<MarkMailReadResponse>("MarkMailRead", request);
        }

        public static async Task<ClaimAttachmentResponse> ClaimAttachmentAsync(string mailId)
        {
            var request = new ClaimAttachmentRequest { mailId = mailId };
            return await Backend.CallEndpointAsync<ClaimAttachmentResponse>("ClaimAttachment", request);
        }

        // --- Mailbox API v2 ---

        public static async Task<GetMailboxPageResponse> CallGetMailboxAsync(int page = 0, int pageSize = 20)
        {
            var request = new GetMailboxPageRequest { page = page, pageSize = pageSize };
            return await Backend.CallEndpointAsync<GetMailboxPageResponse>("GetUserMails", request);
        }

        public static async Task<GetMailboxPageResponse> CallGetGlobalMailsAsync(int page = 0, int pageSize = 20)
        {
            var request = new GetMailboxPageRequest { page = page, pageSize = pageSize };
            return await Backend.CallEndpointAsync<GetMailboxPageResponse>("GetGlobalMails", request);
        }

        public static async Task<MarkMailReadResponse> CallMarkMailReadAsync(string mailId, string mailType)
        {
            var request = new MarkMailReadRequest { mailId = mailId, mailType = mailType };
            return await Backend.CallEndpointAsync<MarkMailReadResponse>("MarkMailRead", request);
        }

        public static async Task<MarkAllReadResponse> CallMarkAllReadAsync()
        {
            return await Backend.CallEndpointAsync<MarkAllReadResponse>("MarkAllRead", null);
        }

        public static async Task<ClaimAttachmentResponse> CallClaimAttachmentAsync(
            string mailId, string mailType, string requestId = null)
        {
            var request = new ClaimAttachmentRequest
            {
                mailId = mailId,
                mailType = mailType,
                requestId = requestId
            };
            return await Backend.CallEndpointAsync<ClaimAttachmentResponse>("ClaimAttachment", request);
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
            string operatorId = null)
        {
            var request = new SendGlobalMailRequest
            {
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
            return await Backend.CallEndpointAsync<SendGlobalMailResponse>("SendGlobalMail", request);
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
            var request = new SendUserMailRequest
            {
                targetPlayerId = targetPlayerId,
                userId = targetPlayerId,
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
            return await Backend.CallEndpointAsync<SendUserMailResponse>("SendUserMail", request);
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
            return await Backend.CallEndpointAsync<GiftMailResponse>("GiftMail", request);
        }

        public static async Task<DeleteMailResponse> CallDeleteMailAsync(string mailId)
        {
            var request = new DeleteMailRequest { mailId = mailId };
            return await Backend.CallEndpointAsync<DeleteMailResponse>("DeleteMail", request);
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
            return await Backend.CallEndpointAsync<ExpireMailResponse>("ExpireMail", request);
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
            return await Backend.CallEndpointAsync<PurgeExpiredResponse>("PurgeExpired", request);
        }
    }
}
