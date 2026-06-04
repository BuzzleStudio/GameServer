using System.Reflection;
using NUnit.Framework;

namespace BackpackAdventures.CloudCode.Client.Tests
{
    [TestFixture]
    [Category("CloudCode")]
    [Category("ApiResponse")]
    public class ApiResponseContractTests
    {
        private const string ClaimAttachmentEnvelopeJson =
            "{\"statusCode\":200,\"message\":\"OK\",\"data\":{\"mailId\":\"gm_claim_contract\",\"alreadyClaimed\":false,\"grantedAttachments\":[]}}";

        [Test]
        [Description("Client can request the raw ApiResponse envelope without failing on Path 'data'.")]
        public void DeserializeEnvelope_AsApiResponse_KeepsDataMember()
        {
            var response = DeserializeTolerant<ApiResponse>(ClaimAttachmentEnvelopeJson);

            Assert.IsNotNull(response);
            Assert.AreEqual(200, response.StatusCode);
            Assert.AreEqual("OK", response.Message);

            object data = typeof(ApiResponse)
                .GetProperty("Data", BindingFlags.Public | BindingFlags.Instance)
                .GetValue(response);

            Assert.IsNotNull(data, "ApiResponse must expose the server 'data' member.");
            StringAssert.Contains("gm_claim_contract", data.ToString());
        }

        [Test]
        [Description("Client can request ApiResponse<T> and read typed ClaimAttachmentData from Data.")]
        public void DeserializeEnvelope_AsGenericApiResponse_ExposesTypedClaimAttachmentData()
        {
            var response = DeserializeTolerant<ApiResponse<ClaimAttachmentData>>(ClaimAttachmentEnvelopeJson);

            Assert.IsNotNull(response);
            Assert.AreEqual(200, response.StatusCode);
            Assert.AreEqual("OK", response.Message);
            Assert.IsNotNull(response.Data);
            Assert.AreEqual("gm_claim_contract", response.Data.mailId);
            Assert.IsFalse(response.Data.alreadyClaimed);
            Assert.IsNotNull(response.Data.grantedAttachments);
            Assert.AreEqual(0, response.Data.grantedAttachments.Count);
        }

        // ── serverExecutionMs contract (added by cc-index) ────────────────────────

        private const string ClaimAttachmentWithExecMs =
            "{\"statusCode\":200,\"message\":\"OK\",\"serverExecutionMs\":137," +
            "\"data\":{\"mailId\":\"gm_exec_ms\",\"alreadyClaimed\":false,\"grantedAttachments\":[]}}";

        [Test]
        [Description("Server payload with serverExecutionMs deserializes without error — field is optional/additive.")]
        public void DeserializeEnvelope_WithServerExecutionMs_DoesNotThrow()
        {
            Assert.DoesNotThrow(
                () => DeserializeTolerant<ApiResponse>(ClaimAttachmentWithExecMs),
                "Presence of serverExecutionMs must not break ApiResponse deserialization.");
        }

        [Test]
        [Description("Server payload with serverExecutionMs still exposes the correct data payload.")]
        public void DeserializeEnvelope_WithServerExecutionMs_DataPayloadIntact()
        {
            var response = DeserializeTolerant<ApiResponse<ClaimAttachmentData>>(ClaimAttachmentWithExecMs);

            Assert.IsNotNull(response);
            Assert.AreEqual(200, response.StatusCode);
            Assert.IsNotNull(response.Data, "Data must be accessible even when serverExecutionMs is present.");
            Assert.AreEqual("gm_exec_ms", response.Data.mailId);
            Assert.IsFalse(response.Data.alreadyClaimed);
        }

        [Test]
        [Description("Server payload without serverExecutionMs still deserializes normally — backward compat.")]
        public void DeserializeEnvelope_WithoutServerExecutionMs_BackwardCompatible()
        {
            var response = DeserializeTolerant<ApiResponse<ClaimAttachmentData>>(ClaimAttachmentEnvelopeJson);

            Assert.IsNotNull(response);
            Assert.AreEqual("gm_claim_contract", response.Data?.mailId,
                "Existing payloads without serverExecutionMs must continue to parse correctly.");
        }

        private static T DeserializeTolerant<T>(string rawJson)
        {
            MethodInfo method = typeof(UnityCloudCodeBackend).GetMethod(
                "DeserializeTolerant",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(method, "UnityCloudCodeBackend.DeserializeTolerant<T> must exist.");
            return (T)method.MakeGenericMethod(typeof(T)).Invoke(null, new object[] { rawJson });
        }
    }
}
