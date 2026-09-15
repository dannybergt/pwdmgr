namespace Pwdmgr.Domain.Common;

public abstract class TenantScopedEntity : Entity
{
    public required Guid TenantId { get; init; }
}
