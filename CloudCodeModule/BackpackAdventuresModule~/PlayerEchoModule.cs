using System;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

public class PlayerEchoModule
{
    private readonly ILogger<PlayerEchoModule> _logger;

    public PlayerEchoModule(ILogger<PlayerEchoModule> logger)
    {
        _logger = logger;
    }

    [CloudCodeFunction("PlayerEcho")]
    public ApiResponse<PlayerEchoResponse> Echo(PlayerEchoRequest request)
    {
        _logger.LogInformation("PlayerEcho called for playerId: {PlayerId}", request.PlayerId);
        try
        {
            return ApiResponse<PlayerEchoResponse>.Ok(new PlayerEchoResponse
            {
                PlayerId = request.PlayerId,
                ServerTime = DateTime.UtcNow.ToString("o")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlayerEcho failed for playerId: {PlayerId}", request.PlayerId);
            throw;
        }
    }
}

public class PlayerEchoRequest
{
    public string PlayerId { get; set; } = string.Empty;
}

public class PlayerEchoResponse
{
    public string PlayerId { get; set; } = string.Empty;
    public string ServerTime { get; set; } = string.Empty;
}
