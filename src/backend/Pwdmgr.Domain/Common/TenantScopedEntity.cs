namespace Pwdmgr.Domain.Common;

public abstract class TenantScopedEntity
{
    public required Guid Id { get; init; }

    public required Guid TenantId { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
}

