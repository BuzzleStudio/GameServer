using System;
using System.Collections.Generic;
using System.Threading;
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
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TIMEOUT_SECONDS));
            try
            {
                var result = await CloudCodeService.Instance.CallModuleEndpointAsync<HealthCheckResponse>(
                    MODULE_NAME, "HealthCheck", null);
                Debug.Log($"[CloudCode] HealthCheck: success={result.success}, message={result.message}, timestamp={result.timestamp}");
                return result;
            }
            catch (OperationCanceledException)
            {
                Debug.LogError("[CloudCode] HealthCheck timed out after " + TIMEOUT_SECONDS + "s");
                throw;
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
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TIMEOUT_SECONDS));
            try
            {
                var args = new Dictionary<string, object> { { "playerId", playerId } };
                var result = await CloudCodeService.Instance.CallModuleEndpointAsync<PlayerEchoResponse>(
                    MODULE_NAME, "PlayerEcho", args);
                Debug.Log($"[CloudCode] PlayerEcho: success={result.success}, playerId={result.playerId}, serverTime={result.serverTime}");
                return result;
            }
            catch (OperationCanceledException)
            {
                Debug.LogError("[CloudCode] PlayerEcho timed out after " + TIMEOUT_SECONDS + "s");
                throw;
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
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TIMEOUT_SECONDS));
            try
            {
                var result = await CloudCodeService.Instance.CallModuleEndpointAsync<ServerConfigResponse>(
                    MODULE_NAME, "ServerConfig", null);
                Debug.Log($"[CloudCode] ServerConfig: environment={result.environment}, version={result.version}, deploymentTime={result.deploymentTime}");
                return result;
            }
            catch (OperationCanceledException)
            {
                Debug.LogError("[CloudCode] ServerConfig timed out after " + TIMEOUT_SECONDS + "s");
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError("[CloudCode] ServerConfig failed: " + ex.Message);
                throw;
            }
        }
    }
}
