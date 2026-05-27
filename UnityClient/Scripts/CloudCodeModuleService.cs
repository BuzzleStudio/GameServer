using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.CloudCode;
using UnityEngine;

namespace BackpackAdventures.Client
{
    public static class CloudCodeModuleService
    {
        private const string MODULE_NAME = "BackpackAdventures";

        public static async Task<HealthCheckResponse> HealthCheckAsync()
        {
            try
            {
                var args = new Dictionary<string, object>();
                var result = await CloudCodeService.Instance.CallModuleEndpointAsync<HealthCheckResponse>(
                    MODULE_NAME, "HealthCheck", args);

                Debug.Log("[CloudCodeModuleService] HealthCheck response: success=" + result.success
                    + " message=" + result.message + " timestamp=" + result.timestamp);

                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError("[CloudCodeModuleService] HealthCheck failed: " + ex.Message);
                throw;
            }
        }

        public static async Task<PlayerEchoResponse> PlayerEchoTestAsync(string playerId)
        {
            try
            {
                var args = new Dictionary<string, object>
                {
                    { "playerId", playerId }
                };
                var result = await CloudCodeService.Instance.CallModuleEndpointAsync<PlayerEchoResponse>(
                    MODULE_NAME, "PlayerEchoTest", args);

                Debug.Log("[CloudCodeModuleService] PlayerEchoTest response: success=" + result.success
                    + " playerId=" + result.playerId + " serverTime=" + result.serverTime);

                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError("[CloudCodeModuleService] PlayerEchoTest failed: " + ex.Message);
                throw;
            }
        }

        public static async Task<ServerConfigResponse> ServerConfigTestAsync()
        {
            try
            {
                var args = new Dictionary<string, object>();
                var result = await CloudCodeService.Instance.CallModuleEndpointAsync<ServerConfigResponse>(
                    MODULE_NAME, "ServerConfigTest", args);

                Debug.Log("[CloudCodeModuleService] ServerConfigTest response: environment=" + result.environment
                    + " version=" + result.version + " deploymentTime=" + result.deploymentTime);

                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError("[CloudCodeModuleService] ServerConfigTest failed: " + ex.Message);
                throw;
            }
        }
    }
}
