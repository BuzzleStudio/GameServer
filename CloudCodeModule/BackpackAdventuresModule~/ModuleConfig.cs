using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

public class ModuleConfig : ICloudCodeSetup
{
    public void Setup(ICloudCodeConfig config)
    {
        // Keep DI empty. The Cloud Code runtime injects IExecutionContext,
        // IGameApiClient, and ILogger<TModule> into module classes directly.
        // Registering GameApiClient here previously caused module construction
        // failures in this project.
    }
}
