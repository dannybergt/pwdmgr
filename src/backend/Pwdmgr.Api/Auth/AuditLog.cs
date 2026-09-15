using Pwdmgr.Infrastructure.Auth;

namespace Pwdmgr.Api.Auth;

/// <summary>
/// Security audit events (Constitution §6.4): outcome, tenant, ids and client address — never
/// the e-mail, password, token or cookie. Category <c>Pwdmgr.Audit.Auth</c> so operators can
/// route them separately.
/// </summary>
public static class AuditLog
{
    public static readonly EventId LoginEvent = new(1001, "Login");
    public static readonly EventId LogoutEvent = new(1002, "Logout");
    public static readonly EventId ResourceEvent = new(1010, "Resource");

    /// <summary>Keyring/vault/secret lifecycle: ids only, never names or contents.</summary>
    public static void Resource(ILogger logger, string action, Guid tenantId, Guid userId, Guid resourceId)
    {
        logger.LogInformation(ResourceEvent, "Resource action={Action} tenant={TenantId} user={UserId} resource={ResourceId}", action, tenantId, userId, resourceId);
    }

    public static void Login(ILogger logger, string outcome, string tenantSlug, LoginResult? result, string client)
    {
        if (result is null)
        {
            logger.LogWarning(LoginEvent, "Login outcome={Outcome} tenantSlug={TenantSlug} client={Client}", outcome, tenantSlug, client);
        }
        else
        {
            logger.LogInformation(LoginEvent, "Login outcome={Outcome} tenant={TenantId} user={UserId} session={SessionId} client={Client}",
                outcome, result.TenantId, result.UserId, result.SessionId, client);
        }
    }
}
