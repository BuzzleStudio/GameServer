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
