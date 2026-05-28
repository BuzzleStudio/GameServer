using System;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

public class HealthCheckModule
{
    private readonly ILogger<HealthCheckModule> _logger;

    public HealthCheckModule(ILogger<HealthCheckModule> logger)
    {
        _logger = logger;
    }

    [CloudCodeFunction("HealthCheck")]
    public HealthCheckResponse GetHealthCheck()
    {
        _logger.LogInformation("HealthCheck called");
        try
        {
            return new HealthCheckResponse
            {
                Success = true,
                Message = "Cloud Code module online",
                Timestamp = DateTime.UtcNow.ToString("o")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HealthCheck failed");
            throw;
        }
    }
}

public class HealthCheckResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
}
