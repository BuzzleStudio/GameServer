using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

/// <summary>
/// Admin authentication for Cloud Code module endpoints.
///
/// Every admin call must come through a Unity service-account REST request.
/// The service account must be scoped to the target project; player-authenticated
/// calls are rejected even if they include admin-looking request fields.
/// </summary>
public static class AdminAuth
{
    /// <summary>
    /// Throws <see cref="UnauthorizedAccessException"/> when the caller is not a
    /// service account or when audit metadata is missing. Secret values are never
    /// passed through the request body.
    /// </summary>
    public static Task RequireAdminToolAsync(
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

        if (!IsServiceAccountCall(context))
        {
            logger?.LogWarning(
                "Admin call rejected: caller is not a service account (operatorId={OperatorId}, contextNull={ContextNull}, playerIdEmpty={PlayerIdEmpty}, accessTokenLen={AccessTokenLen}, serviceTokenLen={ServiceTokenLen}, userId={UserId}, issuer={Issuer}).",
                operatorId,
                context == null,
                string.IsNullOrEmpty(context?.PlayerId),
                context?.AccessToken?.Length ?? 0,
                context?.ServiceToken?.Length ?? 0,
                context?.UserId,
                context?.Issuer);
            throw new UnauthorizedAccessException(MailboxError.Unauthorized);
        }

        logger?.LogInformation(
            "Admin call authorised by project-scoped service account (operatorId={OperatorId}, userId={UserId}, issuer={Issuer}).",
            operatorId,
            context.UserId,
            context.Issuer);
        return Task.CompletedTask;
    }

    private static bool IsServiceAccountCall(IExecutionContext context)
    {
        if (context == null || !string.IsNullOrEmpty(context.PlayerId))
            return false;

        return !string.IsNullOrEmpty(context.ServiceToken) ||
               !string.IsNullOrEmpty(context.AccessToken);
    }
}
