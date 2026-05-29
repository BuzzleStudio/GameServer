using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.CloudCode;
using UnityEngine;

namespace BackpackAdventures.CloudCode.Client
{
    public sealed class UnityCloudCodeBackend : ICloudCodeBackend
    {
        private const string ModuleName = "BackpackAdventuresModule";
        private const int TimeoutSeconds = 10;

        public async Task<T> CallEndpointAsync<T>(string endpoint, object request)
        {
            Debug.Log($"[CloudCode] Calling {endpoint}...");
            try
            {
                var args = request != null
                    ? new Dictionary<string, object> { { "request", request } }
                    : null;
                var callTask = CloudCodeService.Instance.CallModuleEndpointAsync<T>(
                    ModuleName, endpoint, args);
                var result = await this.WithTimeout(callTask, endpoint);
                Debug.Log($"[CloudCode] {endpoint} succeeded.");
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CloudCode] {endpoint} failed: " + ex.Message);
                throw;
            }
        }

        private async Task<T> WithTimeout<T>(Task<T> task, string operationName)
        {
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(TimeoutSeconds));
            var completed = await Task.WhenAny(task, timeoutTask);
            if (completed == timeoutTask)
                throw new TimeoutException(
                    $"[CloudCode] {operationName} timed out after {TimeoutSeconds}s");
            return await task;
        }
    }
}
