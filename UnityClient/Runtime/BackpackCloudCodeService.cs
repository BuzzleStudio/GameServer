using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.CloudCode;
using Unity.Services.Core;
using UnityEngine;

namespace BackpackAdventures.CloudCode.Client
{
    public static class BackpackCloudCodeService
    {
        private const string MODULE_NAME = "BackpackAdventuresModule";
        private const int TIMEOUT_SECONDS = 10;

        public static async Task InitializeAsync()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
                await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        public static async Task<HealthCheckResponse> CallHealthCheckAsync()
        {
            Debug.Log("[CloudCode] Calling HealthCheck...");
            try
            {
                var callTask = CloudCodeService.Instance.CallModuleEndpointAsync<HealthCheckResponse>(
                    MODULE_NAME, "HealthCheck", null);
                var result = await WithTimeout(callTask, "HealthCheck");
                Debug.Log($"[CloudCode] HealthCheck: success={result.success}, message={result.message}, timestamp={result.timestamp}");
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError("[CloudCode] HealthCheck failed: " + ex.Message);
                throw;
            }
        }

        public static async Task<PlayerEchoResponse> CallPlayerEchoAsync(string playerId)
        {
            Debug.Log($"[CloudCode] Calling PlayerEcho with playerId={playerId}...");
            try
            {
                var args = new Dictionary<string, object> { { "request", new { playerId = playerId } } };
                var callTask = CloudCodeService.Instance.CallModuleEndpointAsync<PlayerEchoResponse>(
                    MODULE_NAME, "PlayerEcho", args);
                var result = await WithTimeout(callTask, "PlayerEcho");
                Debug.Log($"[CloudCode] PlayerEcho: success={result.success}, playerId={result.playerId}, serverTime={result.serverTime}");
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError("[CloudCode] PlayerEcho failed: " + ex.Message);
                throw;
            }
        }

        public static async Task<ServerConfigResponse> CallServerConfigAsync()
        {
            Debug.Log("[CloudCode] Calling ServerConfig...");
            try
            {
                var callTask = CloudCodeService.Instance.CallModuleEndpointAsync<ServerConfigResponse>(
                    MODULE_NAME, "ServerConfig", null);
                var result = await WithTimeout(callTask, "ServerConfig");
                Debug.Log($"[CloudCode] ServerConfig: environment={result.environment}, version={result.version}, deploymentTime={result.deploymentTime}");
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError("[CloudCode] ServerConfig failed: " + ex.Message);
                throw;
            }
        }

        // --- Mailbox API (legacy — kept for backward compatibility) ---

        public static async Task<SendGlobalMailResponse> SendGlobalMailAsync(
            string subject, string body, string expiresAt = null,
            List<MailAttachment> attachments = null)
        {
            Debug.Log($"[CloudCode] Calling SendGlobalMail subject={subject}");
            try
            {
                var request = new SendGlobalMailRequest
                {
                    subject = subject,
                    body = body,
                    expiresAt = expiresAt,
                    attachments = attachments
                };
                var args = new Dictionary<string, object> { { "request", request } };
                var callTask = CloudCodeService.Instance.CallModuleEndpointAsync<SendGlobalMailResponse>(
                    MODULE_NAME, "SendGlobalMail", args);
                return await WithTimeout(callTask, "SendGlobalMail");
            }
            catch (Exception ex)
            {
                Debug.LogError("[CloudCode] SendGlobalMail failed: " + ex.Message);
                throw;
            }
        }

        public static async Task<SendUserMailResponse> SendUserMailAsync(
            string userId, string subject, string body, string expiresAt = null,
            List<MailAttachment> attachments = null)
        {
            Debug.Log($"[CloudCode] Calling SendUserMail userId={userId} subject={subject}");
            try
            {
                var request = new SendUserMailRequest
                {
                    userId = userId,
                    targetPlayerId = userId,
                    subject = subject,
                    body = body,
                    expiresAt = expiresAt,
                    attachments = attachments
                };
                var args = new Dictionary<string, object> { { "request", request } };
                var callTask = CloudCodeService.Instance.CallModuleEndpointAsync<SendUserMailResponse>(
                    MODULE_NAME, "SendUserMail", args);
                return await WithTimeout(callTask, "SendUserMail");
            }
            catch (Exception ex)
            {
                Debug.LogError("[CloudCode] SendUserMail failed: " + ex.Message);
                throw;
            }
        }

        public static async Task<GetMailboxResponse> GetMailboxAsync()
        {
            Debug.Log("[CloudCode] Calling GetMailbox");
            try
            {
                var callTask = CloudCodeService.Instance.CallModuleEndpointAsync<GetMailboxResponse>(
                    MODULE_NAME, "GetMailbox", null);
                return await WithTimeout(callTask, "GetMailbox");
            }
            catch (Exception ex)
            {
                Debug.LogError("[CloudCode] GetMailbox failed: " + ex.Message);
                throw;
            }
        }

        public static async Task<MarkMailReadResponse> MarkMailReadAsync(string mailId)
        {
            Debug.Log($"[CloudCode] Calling MarkMailRead mailId={mailId}");
            try
            {
                var request = new MarkMailReadRequest { mailId = mailId };
                var args = new Dictionary<string, object> { { "request", request } };
                var callTask = CloudCodeService.Instance.CallModuleEndpointAsync<MarkMailReadResponse>(
                    MODULE_NAME, "MarkMailRead", args);
                return await WithTimeout(callTask, "MarkMailRead");
            }
            catch (Exception ex)
            {
                Debug.LogError("[CloudCode] MarkMailRead failed: " + ex.Message);
                throw;
            }
        }

        public static async Task<ClaimAttachmentResponse> ClaimAttachmentAsync(string mailId)
        {
            Debug.Log($"[CloudCode] Calling ClaimAttachment mailId={mailId}");
            try
            {
                var request = new ClaimAttachmentRequest { mailId = mailId };
                var args = new Dictionary<string, object> { { "request", request } };
                var callTask = CloudCodeService.Instance.CallModuleEndpointAsync<ClaimAttachmentResponse>(
                    MODULE_NAME, "ClaimAttachment", args);
                return await WithTimeout(callTask, "ClaimAttachment");
            }
            catch (Exception ex)
            {
                Debug.LogError("[CloudCode] ClaimAttachment failed: " + ex.Message);
                throw;
            }
        }

        // --- Mailbox API v2 (paginated, new endpoints per §5.11) ---

        /// <summary>GetUserMails — replaces GetMailbox; supports pagination (§5.6).</summary>
        public static async Task<GetMailboxPageResponse> CallGetMailboxAsync(int page = 0, int pageSize = 20)
        {
            Debug.Log($"[CloudCode] Calling GetUserMails page={page} pageSize={pageSize}");
            try
            {
                var request = new GetMailboxPageRequest { page = page, pageSize = pageSize };
                var args = new Dictionary<string, object> { { "request", request } };
                var callTask = CloudCodeService.Instance.CallModuleEndpointAsync<GetMailboxPageResponse>(
                    MODULE_NAME, "GetUserMails", args);
                var result = await WithTimeout(callTask, "GetUserMails");
                Debug.Log($"[CloudCode] GetUserMails: success={result.success} totalCount={result.totalCount} hasMore={result.hasMore}");
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError("[CloudCode] GetUserMails failed: " + ex.Message);
                throw;
            }
        }

        /// <summary>GetGlobalMails — new paginated endpoint for global mail (§5.6).</summary>
        public static async Task<GetMailboxPageResponse> CallGetGlobalMailsAsync(int page = 0, int pageSize = 20)
        {
            Debug.Log($"[CloudCode] Calling GetGlobalMails page={page} pageSize={pageSize}");
            try
            {
                var request = new GetMailboxPageRequest { page = page, pageSize = pageSize };
                var args = new Dictionary<string, object> { { "request", request } };
                var callTask = CloudCodeService.Instance.CallModuleEndpointAsync<GetMailboxPageResponse>(
                    MODULE_NAME, "GetGlobalMails", args);
                var result = await WithTimeout(callTask, "GetGlobalMails");
                Debug.Log($"[CloudCode] GetGlobalMails: success={result.success} totalCount={result.totalCount} hasMore={result.hasMore}");
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError("[CloudCode] GetGlobalMails failed: " + ex.Message);
                throw;
            }
        }

        /// <summary>MarkMailRead v2 — requires mailType (§5.11).</summary>
        public static async Task<MarkMailReadResponse> CallMarkMailReadAsync(string mailId, string mailType)
        {
            Debug.Log($"[CloudCode] Calling MarkMailRead mailId={mailId} mailType={mailType}");
            try
            {
                var request = new MarkMailReadRequest { mailId = mailId, mailType = mailType };
                var args = new Dictionary<string, object> { { "request", request } };
                var callTask = CloudCodeService.Instance.CallModuleEndpointAsync<MarkMailReadResponse>(
                    MODULE_NAME, "MarkMailRead", args);
                var result = await WithTimeout(callTask, "MarkMailRead");
                Debug.Log($"[CloudCode] MarkMailRead: success={result.success} isRead={result.isRead}");
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError("[CloudCode] MarkMailRead failed: " + ex.Message);
                throw;
            }
        }

        /// <summary>MarkAllRead — marks every mail as read for the signed-in player (§5.11).</summary>
        public static async Task<MarkAllReadResponse> CallMarkAllReadAsync()
        {
            Debug.Log("[CloudCode] Calling MarkAllRead");
            try
            {
                var callTask = CloudCodeService.Instance.CallModuleEndpointAsync<MarkAllReadResponse>(
                    MODULE_NAME, "MarkAllRead", null);
                var result = await WithTimeout(callTask, "MarkAllRead");
                Debug.Log($"[CloudCode] MarkAllRead: success={result.success} lastReadAt={result.lastReadAt}");
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError("[CloudCode] MarkAllRead failed: " + ex.Message);
                throw;
            }
        }

        /// <summary>ClaimAttachment v2 — with mailType and optional requestId idempotency key (§5.8).</summary>
        public static async Task<ClaimAttachmentResponse> CallClaimAttachmentAsync(
            string mailId, string mailType, string requestId = null)
        {
            Debug.Log($"[CloudCode] Calling ClaimAttachment mailId={mailId} mailType={mailType} requestId={requestId}");
            try
            {
                var request = new ClaimAttachmentRequest
                {
                    mailId = mailId,
                    mailType = mailType,
                    requestId = requestId
                };
                var args = new Dictionary<string, object> { { "request", request } };
                var callTask = CloudCodeService.Instance.CallModuleEndpointAsync<ClaimAttachmentResponse>(
                    MODULE_NAME, "ClaimAttachment", args);
                var result = await WithTimeout(callTask, "ClaimAttachment");
                Debug.Log($"[CloudCode] ClaimAttachment: success={result.success} alreadyClaimed={result.alreadyClaimed}");
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError("[CloudCode] ClaimAttachment failed: " + ex.Message);
                throw;
            }
        }

        /// <summary>SendGlobalMail v2 — admin-gated, with metadata (§5.3).</summary>
        public static async Task<SendGlobalMailResponse> CallAdminSendGlobalMailAsync(
            string subject,
            string body,
            string expiresAt = null,
            string mailCategory = null,
            string senderName = null,
            string dedupKey = null,
            List<MailAttachment> attachments = null)
        {
            Debug.Log($"[CloudCode] Calling SendGlobalMail (v2) subject={subject}");
            try
            {
                var request = new SendGlobalMailRequest
                {
                    subject = subject,
                    body = body,
                    expiresAt = expiresAt,
                    mailCategory = mailCategory,
                    senderName = senderName,
                    dedupKey = dedupKey,
                    attachments = attachments
                };
                var args = new Dictionary<string, object> { { "request", request } };
                var callTask = CloudCodeService.Instance.CallModuleEndpointAsync<SendGlobalMailResponse>(
                    MODULE_NAME, "SendGlobalMail", args);
                var result = await WithTimeout(callTask, "SendGlobalMail");
                Debug.Log($"[CloudCode] SendGlobalMail: success={result.success} mailId={result.mailId ?? result.globalMailId}");
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError("[CloudCode] SendGlobalMail failed: " + ex.Message);
                throw;
            }
        }

        /// <summary>SendUserMail v2 — admin-gated, with metadata (§5.3).</summary>
        public static async Task<SendUserMailResponse> CallAdminSendUserMailAsync(
            string targetPlayerId,
            string subject,
            string body,
            string expiresAt = null,
            string mailCategory = null,
            string senderName = null,
            string dedupKey = null,
            List<MailAttachment> attachments = null)
        {
            Debug.Log($"[CloudCode] Calling SendUserMail (v2) targetPlayerId={targetPlayerId} subject={subject}");
            try
            {
                var request = new SendUserMailRequest
                {
                    targetPlayerId = targetPlayerId,
                    userId = targetPlayerId,
                    subject = subject,
                    body = body,
                    expiresAt = expiresAt,
                    mailCategory = mailCategory,
                    senderName = senderName,
                    dedupKey = dedupKey,
                    attachments = attachments
                };
                var args = new Dictionary<string, object> { { "request", request } };
                var callTask = CloudCodeService.Instance.CallModuleEndpointAsync<SendUserMailResponse>(
                    MODULE_NAME, "SendUserMail", args);
                var result = await WithTimeout(callTask, "SendUserMail");
                Debug.Log($"[CloudCode] SendUserMail: success={result.success} mailId={result.mailId}");
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError("[CloudCode] SendUserMail failed: " + ex.Message);
                throw;
            }
        }

        /// <summary>GiftMail — player-to-player gift (notification only, §5.3).</summary>
        public static async Task<GiftMailResponse> CallUserSendGiftMailAsync(
            string targetPlayerId, string subject, string body)
        {
            Debug.Log($"[CloudCode] Calling GiftMail targetPlayerId={targetPlayerId} subject={subject}");
            try
            {
                var request = new GiftMailRequest
                {
                    targetPlayerId = targetPlayerId,
                    subject = subject,
                    body = body
                };
                var args = new Dictionary<string, object> { { "request", request } };
                var callTask = CloudCodeService.Instance.CallModuleEndpointAsync<GiftMailResponse>(
                    MODULE_NAME, "GiftMail", args);
                var result = await WithTimeout(callTask, "GiftMail");
                Debug.Log($"[CloudCode] GiftMail: success={result.success} mailId={result.mailId}");
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError("[CloudCode] GiftMail failed: " + ex.Message);
                throw;
            }
        }

        /// <summary>DeleteMail — player deletes their own user mail (§5.11).</summary>
        public static async Task<DeleteMailResponse> CallDeleteMailAsync(string mailId)
        {
            Debug.Log($"[CloudCode] Calling DeleteMail mailId={mailId}");
            try
            {
                var request = new DeleteMailRequest { mailId = mailId };
                var args = new Dictionary<string, object> { { "request", request } };
                var callTask = CloudCodeService.Instance.CallModuleEndpointAsync<DeleteMailResponse>(
                    MODULE_NAME, "DeleteMail", args);
                var result = await WithTimeout(callTask, "DeleteMail");
                Debug.Log($"[CloudCode] DeleteMail: success={result.success} mailId={result.mailId}");
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError("[CloudCode] DeleteMail failed: " + ex.Message);
                throw;
            }
        }

        /// <summary>ExpireMail — admin forces expiry of a specific mail (§5.11).</summary>
        public static async Task<ExpireMailResponse> CallExpireMailAsync(string mailId)
        {
            Debug.Log($"[CloudCode] Calling ExpireMail mailId={mailId}");
            try
            {
                var request = new ExpireMailRequest { mailId = mailId };
                var args = new Dictionary<string, object> { { "request", request } };
                var callTask = CloudCodeService.Instance.CallModuleEndpointAsync<ExpireMailResponse>(
                    MODULE_NAME, "ExpireMail", args);
                var result = await WithTimeout(callTask, "ExpireMail");
                Debug.Log($"[CloudCode] ExpireMail: success={result.success} mailId={result.mailId}");
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError("[CloudCode] ExpireMail failed: " + ex.Message);
                throw;
            }
        }

        /// <summary>PurgeExpired — admin removes all expired global mail refs (§5.9).</summary>
        public static async Task<PurgeExpiredResponse> CallPurgeExpiredAsync()
        {
            Debug.Log("[CloudCode] Calling PurgeExpired");
            try
            {
                var callTask = CloudCodeService.Instance.CallModuleEndpointAsync<PurgeExpiredResponse>(
                    MODULE_NAME, "PurgeExpired", null);
                var result = await WithTimeout(callTask, "PurgeExpired");
                Debug.Log($"[CloudCode] PurgeExpired: success={result.success} purgedCount={result.purgedCount}");
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError("[CloudCode] PurgeExpired failed: " + ex.Message);
                throw;
            }
        }

        private static async Task<T> WithTimeout<T>(Task<T> task, string operationName)
        {
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(TIMEOUT_SECONDS));
            var completed = await Task.WhenAny(task, timeoutTask);
            if (completed == timeoutTask)
                throw new TimeoutException($"[CloudCode] {operationName} timed out after {TIMEOUT_SECONDS}s");
            return await task;
        }
    }
}
