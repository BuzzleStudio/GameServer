using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

public class ModuleConfig : ICloudCodeSetup
{
    public void Setup(ICloudCodeConfig config)
    {
        // No custom DI registrations. The Cloud Code runtime auto-provides
        // IExecutionContext, IGameApiClient, and ILogger<TModule> to module
        // classes (those with [CloudCodeFunction]) directly. It does NOT
        // resolve those scoped services for custom registered classes — that
        // was the root cause of the "Constructor error: type could not be
        // instantiated" 422 we hit on every admin endpoint.
        //
        // AdminAuth and RewardGrant are static helpers that take the
        // scoped services as method parameters from the module call sites.
    }
}
