using Pwdmgr.Domain.Common;

namespace Pwdmgr.Domain.Identity;

public sealed class User : TenantScopedEntity
{
    public const int EmailMaxLength = 320;
    public const int DisplayNameMaxLength = 200;
    public const int ExternalIdMaxLength = 512;

    public required UserSource Source { get; init; }

    /// <summary>Identifier in the source directory (LDAP DN, OIDC subject); null for local users.</summary>
    public string? ExternalId { get; init; }

    public required string Email { get; init; }

    public required string DisplayName { get; set; }

    public UserStatus Status { get; set; } = UserStatus.Active;
}

public enum UserSource
{
    Local = 1,
    Directory = 2,
    Federated = 3
}

public enum UserStatus
{
    Active = 1,
    Disabled = 2,
    Locked = 3,
    PendingDeletion = 4
}
