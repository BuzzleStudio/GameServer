// GATED: requires cc-index merge (ApiResponse<T>.ServerExecutionMs property).
// Remove the corresponding <Compile Remove> in the .csproj once cc-index is green.
//
// cc-index contract assumed:
//   public class ApiResponse<T>
//   {
//       public int StatusCode { get; set; }
//       public string Message { get; set; }
//       public T? Data { get; set; }
//       public long ServerExecutionMs { get; set; }  // NEW — added by cc-index
//   }

using System.Text.Json;
using Xunit;

namespace BackpackAdventures.CloudCode.Tests;

/// <summary>
/// Verifies the ServerExecutionMs field on ApiResponse&lt;T&gt;:
///   • Property exists and is non-negative when set.
///   • JSON round-trips correctly.
///   • Old responses without the field still deserialize (backward compat).
///   • Client-side contract: server field flows through to the client model.
/// </summary>
public class ServerExecutionMsTests
{
    private static readonly JsonSerializerOptions _opts = new() { PropertyNameCaseInsensitive = true };

    // ── Property presence and default ─────────────────────────────────────────

    [Fact]
    public void ServerExecutionMs_Property_ExistsOnApiResponse()
    {
        var prop = typeof(ApiResponse<ClaimAttachmentResponse>)
            .GetProperty("ServerExecutionMs");

        Assert.NotNull(prop);
        Assert.Equal(typeof(long), prop!.PropertyType);
    }

    [Fact]
    public void ServerExecutionMs_Default_IsZeroOrGreater()
    {
        var response = ApiResponse<ClaimAttachmentResponse>.Ok(
            new ClaimAttachmentResponse { MailId = "gm_test" });

        Assert.True(response.ServerExecutionMs >= 0,
            $"Default ServerExecutionMs must be >= 0, was {response.ServerExecutionMs}");
    }

    [Fact]
    public void ServerExecutionMs_WhenSet_IsReflectedInInstance()
    {
        var response = new ApiResponse<ClaimAttachmentResponse>
        {
            StatusCode = 200,
            Message = "OK",
            Data = new ClaimAttachmentResponse { MailId = "gm_x" },
            ServerExecutionMs = 42
        };

        Assert.Equal(42, response.ServerExecutionMs);
    }

    // ── JSON round-trip ───────────────────────────────────────────────────────

    [Fact]
    public void JsonSerialize_IncludesServerExecutionMs()
    {
        var response = new ApiResponse<ClaimAttachmentResponse>
        {
            StatusCode = 200,
            Message = "OK",
            Data = new ClaimAttachmentResponse { MailId = "gm_x" },
            ServerExecutionMs = 99
        };

        var json = JsonSerializer.Serialize(response);

        Assert.Contains("serverExecutionMs", json, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void JsonDeserialize_WithServerExecutionMs_ParsesCorrectly()
    {
        const string json = @"{
            ""statusCode"": 200,
            ""message"": ""OK"",
            ""serverExecutionMs"": 123,
            ""data"": { ""mailId"": ""gm_round_trip"", ""alreadyClaimed"": false }
        }";

        var response = JsonSerializer.Deserialize<ApiResponse<ClaimAttachmentResponse>>(json, _opts);

        Assert.NotNull(response);
        Assert.Equal(123, response!.ServerExecutionMs);
        Assert.Equal("gm_round_trip", response.Data?.MailId);
    }

    [Fact]
    public void JsonDeserialize_WithoutServerExecutionMs_BackwardCompatible()
    {
        // Old server responses that don't include serverExecutionMs must still parse.
        const string json = @"{
            ""statusCode"": 200,
            ""message"": ""OK"",
            ""data"": { ""mailId"": ""gm_old_format"", ""alreadyClaimed"": true }
        }";

        var response = JsonSerializer.Deserialize<ApiResponse<ClaimAttachmentResponse>>(json, _opts);

        Assert.NotNull(response);
        Assert.Equal(0L, response!.ServerExecutionMs); // default when absent
        Assert.Equal("gm_old_format", response.Data?.MailId);
    }

    [Fact]
    public void JsonDeserialize_ClaimAllResponse_ServerExecutionMsPresentAndNonNegative()
    {
        const string json = @"{
            ""statusCode"": 200,
            ""message"": ""OK"",
            ""serverExecutionMs"": 250,
            ""data"": {
                ""claimedCount"": 3,
                ""alreadyClaimedCount"": 0,
                ""skippedCount"": 0,
                ""results"": [],
                ""grantedAttachments"": []
            }
        }";

        var response = JsonSerializer.Deserialize<ApiResponse<ClaimAllAttachmentsResponse>>(json, _opts);

        Assert.NotNull(response);
        Assert.True(response!.ServerExecutionMs >= 0);
        Assert.Equal(250, response.ServerExecutionMs);
        Assert.Equal(3, response.Data?.ClaimedCount);
    }
}
