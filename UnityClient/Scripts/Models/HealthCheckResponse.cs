using System;

namespace BackpackAdventures.Client
{
    [Serializable]
    public class HealthCheckResponse
    {
        public bool success;
        public string message;
        public string timestamp;
    }
}
