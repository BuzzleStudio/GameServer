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
        private const string MODULE_NAME = "BackpackAdventures";
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
                var args = new Dictionary<string, object> { { "playerId", playerId } };
                var callTask = CloudCodeService.Instance.CallModuleEndpointAsync<PlayerEchoResponse>(
                    MODULE_NAME, "PlayerEchoTest", args);
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
                    MODULE_NAME, "ServerConfigTest", null);
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
