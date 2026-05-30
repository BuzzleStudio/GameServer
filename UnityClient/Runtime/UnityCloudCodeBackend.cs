using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
                // Use the string-returning overload so we can parse the response ourselves
                // and tolerate either shape the server returns:
                //   1. Bare data:        { "field1": ..., "field2": ... }
                //   2. ApiResponse wrap: { "statusCode": 200, "message": "OK", "data": { ... } }
                // Without this tolerance, a server-side change to wrap/unwrap the envelope
                // breaks every client until they're rebuilt.
                var rawTask = CloudCodeService.Instance.CallModuleEndpointAsync(
                    ModuleName, endpoint, args);
                string rawJson = await this.WithTimeout(rawTask, endpoint);
                Debug.Log($"[CloudCode] {endpoint} succeeded.");
                return DeserializeTolerant<T>(rawJson);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CloudCode] {endpoint} failed: " + ex.Message);
                throw CloudCodeApiException.From(endpoint, ex);
            }
        }

        // Unwraps an ApiResponse envelope if present, otherwise treats the JSON as bare data.
        // Detection: if the root has a "data" / "Data" property AND looks like an envelope
        // (has at least one of statusCode/message), unwrap; otherwise use as-is. This avoids
        // false positives where the bare data itself happens to have a "data" field.
        private static T DeserializeTolerant<T>(string rawJson)
        {
            if (string.IsNullOrEmpty(rawJson))
                return default;

            JToken token;
            try
            {
                token = JToken.Parse(rawJson);
            }
            catch (JsonException)
            {
                // Not JSON — treat as raw value (e.g. plain string)
                return JsonConvert.DeserializeObject<T>(rawJson);
            }

            if (token is JObject obj && LooksLikeApiResponseEnvelope(obj))
            {
                JToken data = obj["data"] ?? obj["Data"];
                if (data != null && data.Type != JTokenType.Null)
                    return data.ToObject<T>();
                return default;
            }

            return token.ToObject<T>();
        }

        private static bool LooksLikeApiResponseEnvelope(JObject obj)
        {
            bool hasData = obj["data"] != null || obj["Data"] != null;
            if (!hasData) return false;
            bool hasStatusCode = obj["statusCode"] != null || obj["StatusCode"] != null;
            bool hasMessage = obj["message"] != null || obj["Message"] != null;
            return hasStatusCode || hasMessage;
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

    public sealed class CloudCodeApiException : Exception
    {
        public string Endpoint { get; }
        public int StatusCode { get; }
        public string ErrorCode { get; }

        private CloudCodeApiException(string endpoint, int statusCode, string errorCode, string message, Exception inner)
            : base(message, inner)
        {
            Endpoint = endpoint;
            StatusCode = statusCode;
            ErrorCode = errorCode;
        }

        public static CloudCodeApiException From(string endpoint, Exception ex)
        {
            string message = ex.Message ?? "Cloud Code request failed.";
            string errorCode = ExtractMailboxErrorCode(message);
            int statusCode = MapStatusCode(errorCode, message);
            return new CloudCodeApiException(endpoint, statusCode, errorCode, $"HTTP {statusCode} {errorCode}: {message}", ex);
        }

        private static string ExtractMailboxErrorCode(string message)
        {
            string[] knownCodes =
            {
                "InvalidInput", "Unauthorized", "MailNotFound", "MailExpired", "AlreadyClaimed",
                "NoAttachment", "MailboxFull", "Conflict", "GrantUnavailable", "GiftQuotaExceeded",
                "CannotDeleteUnclaimedReward", "CannotDeleteGlobal", "CannotExpireUserMail", "TargetMailboxFull"
            };

            foreach (var code in knownCodes)
            {
                if (message.IndexOf(code, StringComparison.OrdinalIgnoreCase) >= 0)
                    return code;
            }

            return "InternalError";
        }

        private static int MapStatusCode(string errorCode, string message)
        {
            int explicitStatus = ExtractStatusCode(message);
            if (explicitStatus > 0)
                return explicitStatus;

            if (message.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0)
                return 504;

            switch (errorCode)
            {
                case "InvalidInput":
                    return 400;
                case "Unauthorized":
                    return 401;
                case "MailNotFound":
                    return 404;
                case "Conflict":
                case "AlreadyClaimed":
                    return 409;
                case "MailExpired":
                case "NoAttachment":
                case "MailboxFull":
                case "GiftQuotaExceeded":
                case "CannotDeleteUnclaimedReward":
                case "CannotDeleteGlobal":
                case "CannotExpireUserMail":
                case "TargetMailboxFull":
                    return 400;
                case "GrantUnavailable":
                    return 503;
                default:
                    return 500;
            }
        }

        private static int ExtractStatusCode(string message)
        {
            int[] knownStatuses = { 400, 401, 403, 404, 409, 429, 500, 503, 504 };
            foreach (int status in knownStatuses)
            {
                string code = status.ToString();
                if (message.IndexOf($"HTTP {code}", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf($"({code})", StringComparison.OrdinalIgnoreCase) >= 0)
                    return status;
            }
            return 0;
        }
    }
}
