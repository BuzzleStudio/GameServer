using Microsoft.Extensions.DependencyInjection;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

public class ModuleConfig : ICloudCodeSetup
{
    public void Setup(ICloudCodeConfig config)
    {
        config.Dependencies.AddSingleton<AdminAuthService>();
        config.Dependencies.AddSingleton<IRewardGrantService, CloudSaveRewardGrantService>();
    }
}
