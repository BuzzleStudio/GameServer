using UnityEngine;

namespace BackpackAdventures.CloudCode.Client
{
    public static class CloudCodeValidator
    {
        public static bool ValidateHealthCheck(HealthCheckResponse response)
        {
            if (response == null)
            {
                Debug.LogError("[CloudCodeValidator] HealthCheck: response is null");
                return false;
            }
            if (!response.success)
            {
                Debug.LogError("[CloudCodeValidator] HealthCheck: success=false, message=" + response.message);
                return false;
            }
            if (string.IsNullOrEmpty(response.timestamp))
            {
                Debug.LogError("[CloudCodeValidator] HealthCheck: timestamp is missing");
                return false;
            }
            return true;
        }

        public static bool ValidatePlayerEcho(PlayerEchoResponse response, string expectedPlayerId)
        {
            if (response == null)
            {
                Debug.LogError("[CloudCodeValidator] PlayerEcho: response is null");
                return false;
            }
            if (!response.success)
            {
                Debug.LogError("[CloudCodeValidator] PlayerEcho: success=false");
                return false;
            }
            if (response.playerId != expectedPlayerId)
            {
                Debug.LogError($"[CloudCodeValidator] PlayerEcho: playerId mismatch — expected={expectedPlayerId}, got={response.playerId}");
                return false;
            }
            if (string.IsNullOrEmpty(response.serverTime))
            {
                Debug.LogError("[CloudCodeValidator] PlayerEcho: serverTime is missing");
                return false;
            }
            return true;
        }

        public static bool ValidateServerConfig(ServerConfigResponse response)
        {
            if (response == null)
            {
                Debug.LogError("[CloudCodeValidator] ServerConfig: response is null");
                return false;
            }
            if (string.IsNullOrEmpty(response.environment))
            {
                Debug.LogError("[CloudCodeValidator] ServerConfig: environment is missing");
                return false;
            }
            if (string.IsNullOrEmpty(response.version))
            {
                Debug.LogError("[CloudCodeValidator] ServerConfig: version is missing");
                return false;
            }
            if (string.IsNullOrEmpty(response.deploymentTime))
            {
                Debug.LogError("[CloudCodeValidator] ServerConfig: deploymentTime is missing");
                return false;
            }
            return true;
        }
    }
}
