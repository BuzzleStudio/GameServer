using System;

namespace BackpackAdventures.CloudCode.Client
{
    [Serializable]
    public class HealthCheckResponse
    {
        public bool success;
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
        public bool success;
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
}
