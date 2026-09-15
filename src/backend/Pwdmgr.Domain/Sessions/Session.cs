using Pwdmgr.Domain.Common;

namespace Pwdmgr.Domain.Sessions;

/// <summary>
/// Server-side login session. The browser holds a random token in an HttpOnly cookie; only
/// its SHA-256 is stored here, so a database read never yields a usable token.
/// </summary>
public sealed class Session : TenantScopedEntity
{
    public const int TokenHashLength = 32;

    public required Guid UserId { get; init; }

    public required byte[] TokenHash { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset? RevokedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
