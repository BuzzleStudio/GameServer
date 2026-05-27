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

        // --- Mailbox API ---

        public static async Task<SendGlobalMailResponse> SendGlobalMailAsync(
            string subject, string body, string expiresAt = null,
            System.Collections.Generic.List<MailAttachment> attachments = null)
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
            System.Collections.Generic.List<MailAttachment> attachments = null)
        {
            Debug.Log($"[CloudCode] Calling SendUserMail userId={userId} subject={subject}");
            try
            {
                var request = new SendUserMailRequest
                {
                    userId = userId,
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
