using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace BackpackAdventures.Client
{
    public class CloudCodeDebugTest : MonoBehaviour
    {
        [SerializeField] private string testPlayerId = "test-player-001";

        private void Start()
        {
            InitializeAndTest();
        }

        private async void InitializeAndTest()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            Debug.Log("[CloudCodeDebugTest] Signed in as: " + AuthenticationService.Instance.PlayerId);

            await RunAllTests();
        }

        private async Task RunAllTests()
        {
            try
            {
                var healthResult = await CloudCodeModuleService.HealthCheckAsync();
                Debug.Log("[CloudCodeDebugTest] HealthCheck: success=" + healthResult.success);

                var echoResult = await CloudCodeModuleService.PlayerEchoTestAsync(testPlayerId);
                Debug.Log("[CloudCodeDebugTest] PlayerEcho: playerId=" + echoResult.playerId);

                var configResult = await CloudCodeModuleService.ServerConfigTestAsync();
                Debug.Log("[CloudCodeDebugTest] ServerConfig: version=" + configResult.version
                    + " env=" + configResult.environment);

                Debug.Log("[CloudCodeDebugTest] All tests passed!");
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[CloudCodeDebugTest] Test failed: " + ex.Message);
            }
        }
    }
}
