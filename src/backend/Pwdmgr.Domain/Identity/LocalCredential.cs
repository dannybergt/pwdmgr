using Pwdmgr.Domain.Common;

namespace Pwdmgr.Domain.Identity;

/// <summary>
/// Server-side login credential of a <see cref="UserSource.Local"/> user. Holds only the
/// password verifier, never a key that could decrypt vault data (ADR-0002).
/// </summary>
public sealed class LocalCredential : TenantScopedEntity
{
    public const int PasswordHashMaxLength = 512;

    public required Guid UserId { get; init; }

    /// <summary>PHC-formatted Argon2id string (<c>$argon2id$v=19$m=..,t=..,p=..$salt$hash</c>); self-describing, so no separate parameter column.</summary>
    public required string PasswordHash { get; set; }

    /// <summary>0 = no pepper applied. Incremented when a server-side pepper is introduced or rotated.</summary>
    public int PepperVersion { get; set; }
}
