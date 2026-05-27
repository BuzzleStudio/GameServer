using System;

namespace BackpackAdventures.Client
{
    [Serializable]
    public class PlayerEchoResponse
    {
        public bool success;
        public string playerId;
        public string serverTime;
    }
}
