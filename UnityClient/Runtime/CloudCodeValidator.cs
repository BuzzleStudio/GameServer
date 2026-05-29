using System;
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
            if (string.IsNullOrEmpty(response.message))
            {
                Debug.LogError("[CloudCodeValidator] HealthCheck: message is missing");
                return false;
            }
            if (!IsValidUtcTimestamp(response.timestamp))
            {
                Debug.LogError("[CloudCodeValidator] HealthCheck: timestamp is missing or not a valid ISO-8601 UTC string: " + response.timestamp);
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
            if (string.IsNullOrEmpty(response.playerId))
            {
                Debug.LogError("[CloudCodeValidator] PlayerEcho: playerId is missing");
                return false;
            }
            if (!string.Equals(response.playerId, expectedPlayerId, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogError($"[CloudCodeValidator] PlayerEcho: playerId mismatch - expected={expectedPlayerId}, got={response.playerId}");
                return false;
            }
            if (!IsValidUtcTimestamp(response.serverTime))
            {
                Debug.LogError("[CloudCodeValidator] PlayerEcho: serverTime is missing or not a valid ISO-8601 UTC string: " + response.serverTime);
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
            if (!IsValidUtcTimestamp(response.deploymentTime))
            {
                Debug.LogError("[CloudCodeValidator] ServerConfig: deploymentTime is missing or not a valid ISO-8601 UTC string: " + response.deploymentTime);
                return false;
            }
            return true;
        }

        private static bool IsValidUtcTimestamp(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;
            return DateTimeOffset.TryParse(value, out var parsed) && parsed.Offset == TimeSpan.Zero;
        }
    }
}
