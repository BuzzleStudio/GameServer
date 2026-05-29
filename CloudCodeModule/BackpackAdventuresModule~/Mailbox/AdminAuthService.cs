using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

/// <summary>
/// Admin authentication for Cloud Code module endpoints.
///
/// Every admin call must provide an operator ID and an admin token matching the
/// ADMIN_SERVICE_TOKEN secret stored in Unity Secret Manager at the target
/// project/environment scope. Service-account REST calls are transport only;
/// they do not bypass this gate.
/// </summary>
public static class AdminAuth
{
    private const string AdminTokenSecretName = "ADMIN_SERVICE_TOKEN";

    /// <summary>
    /// Throws <see cref="UnauthorizedAccessException"/> on caller errors and
    /// <see cref="InvalidOperationException"/> when Secret Manager is not
    /// configured correctly. Token values are never logged.
    /// </summary>
    public static async Task RequireAdminToolAsync(
        IGameApiClient gameApiClient,
        IExecutionContext context,
        string adminToken,
        string operatorId,
        ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(operatorId))
        {
            logger?.LogWarning("Admin call rejected: operatorId is null or whitespace.");
            throw new UnauthorizedAccessException(MailboxError.Unauthorized);
        }

        var expectedToken = await GetAdminTokenAsync(gameApiClient, context, operatorId, logger);
        if (string.IsNullOrEmpty(expectedToken))
        {
            logger?.LogError(
                "Admin call rejected: {SecretName} secret missing/empty (operatorId={OperatorId}, contextNull={ContextNull}, playerIdEmpty={PlayerIdEmpty}, accessTokenLen={AccessTokenLen}, serviceTokenLen={ServiceTokenLen}, userId={UserId}, issuer={Issuer}).",
                AdminTokenSecretName,
                operatorId,
                context == null,
                string.IsNullOrEmpty(context?.PlayerId),
                context?.AccessToken?.Length ?? 0,
                context?.ServiceToken?.Length ?? 0,
                context?.UserId,
                context?.Issuer);
            throw new InvalidOperationException($"{AdminTokenSecretName} secret missing/empty.");
        }

        if (string.IsNullOrEmpty(adminToken))
        {
            logger?.LogWarning("Admin call rejected: adminToken missing (operatorId={OperatorId}).", operatorId);
            throw new UnauthorizedAccessException(MailboxError.Unauthorized);
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expectedToken);
        var actualBytes = Encoding.UTF8.GetBytes(adminToken);

        if (!CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes))
        {
            logger?.LogWarning("Admin call rejected: invalid token (operatorId={OperatorId}).", operatorId);
            throw new UnauthorizedAccessException(MailboxError.Unauthorized);
        }

        logger?.LogInformation("Admin call authorised for operatorId={OperatorId}.", operatorId);
    }

    private static async Task<string?> GetAdminTokenAsync(
        IGameApiClient gameApiClient,
        IExecutionContext context,
        string operatorId,
        ILogger logger)
    {
        if (gameApiClient == null)
        {
            logger?.LogError("Admin call rejected: IGameApiClient is null (operatorId={OperatorId}).", operatorId);
            return null;
        }

        try
        {
            var secret = await gameApiClient.SecretManager.GetSecret(context, AdminTokenSecretName);
            return secret?.Value;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex,
                "Admin call rejected: failed to read {SecretName} from Secret Manager (operatorId={OperatorId}).",
                AdminTokenSecretName,
                operatorId);
            return null;
        }
    }
}
