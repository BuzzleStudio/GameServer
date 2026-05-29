using System;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

public class ServerConfigModule
{
    private readonly IExecutionContext _context;
    private readonly ILogger<ServerConfigModule> _logger;

    public ServerConfigModule(IExecutionContext context, ILogger<ServerConfigModule> logger)
    {
        _context = context;
        _logger = logger;
    }

    [CloudCodeFunction("ServerConfig")]
    public ServerConfigResponse GetServerConfig()
    {
        _logger.LogInformation("ServerConfig called for environment: {EnvironmentId}", _context.EnvironmentId);
        try
        {
            return new ServerConfigResponse
            {
                Environment = _context.EnvironmentId ?? "production",
                Version = "1.0.0",
                DeploymentTime = DateTime.UtcNow.ToString("o")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ServerConfig failed");
            throw;
        }
    }
}

public class ServerConfigResponse
{
    public string Environment { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string DeploymentTime { get; set; } = string.Empty;
}
