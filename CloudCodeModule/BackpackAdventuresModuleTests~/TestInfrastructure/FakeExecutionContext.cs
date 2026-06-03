using System;

namespace BackpackAdventures.CloudCode.Tests;

/// <summary>Minimal IExecutionContext — CloudSaveHelper only uses ProjectId, ServiceToken, AccessToken.</summary>
internal sealed class FakeExecutionContext : Unity.Services.CloudCode.Core.IExecutionContext
{
    public FakeExecutionContext(string projectId = "proj-test", string playerId = "player-test",
        string serviceToken = "svc-token-test")
    {
        ProjectId    = projectId;
        PlayerId     = playerId;
        ServiceToken = serviceToken;
    }

    public string ProjectId           { get; }
    public string PlayerId            { get; }
    public string ServiceToken        { get; }
    public string AccessToken         => string.Empty;
    public string EnvironmentId       => "test-env";
    public string EnvironmentName     => "test";
    public string UserId              => PlayerId;
    public string Issuer              => string.Empty;
    public string AnalyticsUserId     => string.Empty;
    public string UnityInstallationId => string.Empty;
    public string CorrelationId       => Guid.NewGuid().ToString();
    public string ScopeId             => string.Empty;
    public int    CallDepth           => 0;
    public Unity.Services.CloudCode.Core.ISession? Session => null;
}
