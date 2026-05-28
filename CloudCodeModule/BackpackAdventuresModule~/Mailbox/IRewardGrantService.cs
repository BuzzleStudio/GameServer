namespace BackpackAdventures.CloudCode;

/// <summary>
/// Thrown by <see cref="RewardGrant.GrantRewardsAsync"/> when the grant service is temporarily
/// unavailable. Callers must NOT set claimed=true and should surface GrantUnavailable to the client.
/// </summary>
public sealed class RetryableGrantException : System.Exception
{
    public RetryableGrantException(string message) : base(message) { }
    public RetryableGrantException(string message, System.Exception inner) : base(message, inner) { }
}
