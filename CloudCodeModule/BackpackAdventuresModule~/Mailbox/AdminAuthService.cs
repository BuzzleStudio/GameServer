using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

/// <summary>
/// Token-based admin authentication for Cloud Code module endpoints.
///
/// Strategy: shared-secret comparison using a constant-time byte comparison to
/// prevent timing attacks. The token value must be set as ADMIN_SERVICE_TOKEN
/// in the Cloud Code module environment variables. There is no
/// fallback — if the env var is absent the gate is closed.
///
/// Call <see cref="RequireAdminToolAsync"/> at the top of every admin endpoint.
/// It throws <see cref="UnauthorizedAccessException"/>(<see cref="MailboxError.Unauthorized"/>)
/// on any failure condition.
///
/// Static helper — Cloud Code's DI only auto-provides IExecutionContext +
/// IGameApiClient to module classes, NOT to custom registered services.
/// Callers pass their injected _gameApiClient + _context through.
/// </summary>
public static class AdminAuth
{
    /// <summary>
    /// Token-based admin gate. Reads the expected token from the
    /// <c>ADMIN_SERVICE_TOKEN</c> environment variable and compares it to
    /// <paramref name="adminToken"/> using a constant-time byte comparison to
    /// prevent timing attacks.
    ///
    /// Throws <see cref="UnauthorizedAccessException"/>(<see cref="MailboxError.Unauthorized"/>)
    /// when:
    ///   - env var returns null/empty (configuration error, fail-closed)
    ///   - <paramref name="adminToken"/> is null or empty
    ///   - <paramref name="operatorId"/> is null or whitespace
    ///   - token comparison fails
    ///
    /// SECURITY: The token value is NEVER logged. Only <paramref name="operatorId"/> is
    /// written to the audit log.
    /// </summary>
    /// <param name="gameApiClient">Injected game API client. Not used by this auth gate.</param>
    /// <param name="context">Execution context. Not used by this auth gate.</param>
    /// <param name="adminToken">Token supplied by the caller in the request body.</param>
    /// <param name="operatorId">Operator identifier for audit logging (e.g. email address).</param>
    /// <param name="logger">Logger for audit and rejection events.</param>
    public static Task RequireAdminToolAsync(IGameApiClient gameApiClient, IExecutionContext context, string adminToken, string operatorId, ILogger logger)
    {
        logger?.LogWarning("DEBUG auth step 1");
        logger?.LogWarning(
            "DEBUG gameApi null={IsNull}",
            gameApiClient == null);
        logger?.LogWarning(
            "DEBUG context null={IsNull}",
            context == null);

        var expectedToken =
            Environment.GetEnvironmentVariable(
                "ADMIN_SERVICE_TOKEN");

        logger?.LogWarning(
            "DEBUG expectedToken null={IsNull}, len={Len}",
            expectedToken == null,
            expectedToken?.Length ?? 0);
        logger?.LogWarning(
            "DEBUG provided len={Len}",
            adminToken?.Length ?? 0);

        // Reject if operatorId is missing — required for audit log integrity.
        if (string.IsNullOrWhiteSpace(operatorId))
        {
            logger?.LogWarning("Admin call rejected: operatorId is null or whitespace.");
            throw new UnauthorizedAccessException(MailboxError.Unauthorized);
        }

        if (string.IsNullOrEmpty(expectedToken))
        {
            logger?.LogError("Admin call rejected: ADMIN_SERVICE_TOKEN env var missing/empty (operatorId={OperatorId}).", operatorId);
            throw new InvalidOperationException("ADMIN_SERVICE_TOKEN env var missing/empty.");
        }

        // Reject if caller did not supply a token.
        if (string.IsNullOrEmpty(adminToken))
        {
            logger?.LogWarning("Admin call rejected: adminToken missing (operatorId={OperatorId}).", operatorId);
            throw new UnauthorizedAccessException(MailboxError.Unauthorized);
        }

        // Constant-time comparison on UTF-8 byte representations to prevent timing attacks.
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedToken);
        byte[] actualBytes   = Encoding.UTF8.GetBytes(adminToken);

        if (!CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes))
        {
            logger?.LogWarning("Admin call rejected: invalid token (operatorId={OperatorId}).", operatorId);
            throw new UnauthorizedAccessException(MailboxError.Unauthorized);
        }

        logger?.LogInformation("Admin call authorised for operatorId={OperatorId}.", operatorId);
        return Task.CompletedTask;
    }
}
