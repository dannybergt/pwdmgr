using Pwdmgr.Domain.Common;

namespace Pwdmgr.Domain.Tenants;

public sealed class Tenant : Entity
{
    public const int SlugMaxLength = 64;
    public const int DisplayNameMaxLength = 200;

    public required string Slug { get; init; }

    public required string DisplayName { get; set; }

    public TenantStatus Status { get; set; } = TenantStatus.Active;
}

public enum TenantStatus
{
    Active = 1,
    Disabled = 2,
    PendingDeletion = 3,
    SoftDeleted = 4
}
