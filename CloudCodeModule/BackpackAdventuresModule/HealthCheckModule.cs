using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures;

public class HealthCheckModule
{
    private readonly ILogger<HealthCheckModule> _logger;

    public HealthCheckModule(ILogger<HealthCheckModule> logger)
    {
        _logger = logger;
    }

    [CloudCodeFunction("HealthCheck")]
    public HealthCheckResponse HealthCheck()
    {
        try
        {
            _logger.LogInformation("HealthCheck called");

            return new HealthCheckResponse(
                success: true,
                message: "Cloud Code module online",
                timestamp: DateTime.UtcNow.ToString("o")
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HealthCheck failed");
            throw;
        }
    }
}

public record HealthCheckResponse(bool success, string message, string timestamp);
