namespace Pwdmgr.Domain.Common;

public abstract class Entity
{
    public required Guid Id { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
}
