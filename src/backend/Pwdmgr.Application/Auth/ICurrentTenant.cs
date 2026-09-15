namespace Pwdmgr.Application.Auth;

/// <summary>
/// Tenant of the current request. <see cref="TenantId"/> is <see cref="Guid.Empty"/> until an
/// authenticated session sets it; the EF global query filter then matches nothing, so an
/// unauthenticated code path cannot accidentally read tenant data.
/// </summary>
public interface ICurrentTenant
{
    Guid TenantId { get; }
}

public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid UserId { get; }

    Guid SessionId { get; }
}
