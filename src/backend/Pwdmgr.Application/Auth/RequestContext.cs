namespace Pwdmgr.Application.Auth;

/// <summary>Scoped per request; populated by the session authentication handler.</summary>
public sealed class RequestContext : ICurrentTenant, ICurrentUser
{
    public Guid TenantId { get; private set; }

    public Guid UserId { get; private set; }

    public Guid SessionId { get; private set; }

    public bool IsAuthenticated => UserId != Guid.Empty;

    public void Authenticate(Guid tenantId, Guid userId, Guid sessionId)
    {
        TenantId = tenantId;
        UserId = userId;
        SessionId = sessionId;
    }
}
