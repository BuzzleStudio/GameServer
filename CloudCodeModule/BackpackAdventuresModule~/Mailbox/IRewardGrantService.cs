using System.Collections.Generic;
using System.Threading.Tasks;

namespace BackpackAdventures.CloudCode;

/// <summary>
/// Abstraction for granting mail attachment rewards to a player.
/// The concrete implementation is swapped depending on available UGS SDK surface.
/// </summary>
public interface IRewardGrantService
{
    /// <summary>
    /// Grants <paramref name="attachments"/> to <paramref name="playerId"/>.
    /// Returns true on success.
    /// Throws <see cref="RetryableGrantException"/> on transient failure — caller must NOT mark mail claimed.
    /// </summary>
    Task<bool> GrantRewardsAsync(string playerId, IReadOnlyList<MailAttachment> attachments, string idempotencyKey);
}

/// <summary>
/// Thrown by <see cref="IRewardGrantService.GrantRewardsAsync"/> when the grant service is temporarily
/// unavailable. Callers should NOT set claimed=true and should surface GrantUnavailable to the client.
/// </summary>
public sealed class RetryableGrantException : System.Exception
{
    public RetryableGrantException(string message) : base(message) { }
    public RetryableGrantException(string message, System.Exception inner) : base(message, inner) { }
}
