using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures;

public class ServerConfigModule
{
    private readonly IExecutionContext _context;
    private readonly ILogger<ServerConfigModule> _logger;

    public ServerConfigModule(IExecutionContext context, ILogger<ServerConfigModule> logger)
    {
        _context = context;
        _logger = logger;
    }

    [CloudCodeFunction("ServerConfigTest")]
    public ServerConfigResponse ServerConfigTest()
    {
        try
        {
            _logger.LogInformation("ServerConfigTest called for environment: {EnvironmentId}", _context.EnvironmentId);

            return new ServerConfigResponse(
                environment: _context.EnvironmentId,
                version: "1.0.0",
                deploymentTime: DateTime.UtcNow.ToString("o")
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ServerConfigTest failed");
            throw;
        }
    }
}

public record ServerConfigResponse(string environment, string version, string deploymentTime);
