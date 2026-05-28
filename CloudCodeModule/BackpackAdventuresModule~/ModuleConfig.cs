using Microsoft.Extensions.DependencyInjection;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

public class ModuleConfig : ICloudCodeSetup
{
    public void Setup(ICloudCodeConfig config)
    {
        // Transient (not Singleton): AdminAuthService and CloudSaveRewardGrantService
        // capture IExecutionContext, which is per-invocation. Singleton would leak
        // the first request's context into every subsequent call and breaks the
        // CC DI container's lifetime validation on module load.
        config.Dependencies.AddTransient<AdminAuthService>();
        config.Dependencies.AddTransient<IRewardGrantService, CloudSaveRewardGrantService>();
    }
}
