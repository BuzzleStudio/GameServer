using Microsoft.Extensions.DependencyInjection;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

public class ModuleConfig : ICloudCodeSetup
{
    public void Setup(ICloudCodeConfig config)
    {
        config.Dependencies.AddSingleton<HealthCheckModule>();
        config.Dependencies.AddSingleton<PlayerEchoModule>();
        config.Dependencies.AddSingleton<ServerConfigModule>();
    }
}
