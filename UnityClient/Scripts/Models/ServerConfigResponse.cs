using System;

namespace BackpackAdventures.Client
{
    [Serializable]
    public class ServerConfigResponse
    {
        public string environment;
        public string version;
        public string deploymentTime;
    }
}
