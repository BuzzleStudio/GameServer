using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures;

public class PlayerEchoModule
{
    private readonly ILogger<PlayerEchoModule> _logger;

    public PlayerEchoModule(ILogger<PlayerEchoModule> logger)
    {
        _logger = logger;
    }

    [CloudCodeFunction("PlayerEchoTest")]
    public PlayerEchoResponse PlayerEchoTest(string playerId)
    {
        try
        {
            _logger.LogInformation("PlayerEchoTest called for playerId: {PlayerId}", playerId);

            return new PlayerEchoResponse(
                success: true,
                playerId: playerId,
                serverTime: DateTime.UtcNow.ToString("o")
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlayerEchoTest failed for playerId: {PlayerId}", playerId);
            throw;
        }
    }
}

public record PlayerEchoResponse(bool success, string playerId, string serverTime);
