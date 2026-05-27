using System.Threading.Tasks;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures;

[ModuleRegistry]
public class ModuleConfig : IModuleConfig
{
    public async Task<IModuleConfig> Bind(IModuleBuilder builder)
    {
        builder.CloudCodeModule<HealthCheckModule>();
        builder.CloudCodeModule<PlayerEchoModule>();
        builder.CloudCodeModule<ServerConfigModule>();
        return this;
    }
}
