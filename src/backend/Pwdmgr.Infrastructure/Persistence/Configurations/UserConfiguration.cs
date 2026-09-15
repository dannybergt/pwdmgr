using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pwdmgr.Domain.Identity;
using Pwdmgr.Domain.Tenants;

namespace Pwdmgr.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);

        // Composite alternate key so child tables can reference (tenant_id, id) and the
        // database itself guarantees a child row never points into another tenant.
        builder.HasAlternateKey(u => new { u.TenantId, u.Id });

        builder.Property(u => u.Source).HasConversion<int>();
        builder.Property(u => u.Status).HasConversion<int>();
        builder.Property(u => u.ExternalId).HasMaxLength(User.ExternalIdMaxLength);

        // citext: e-mail uniqueness and lookups are case-insensitive at the database level,
        // so "Alice@…" and "alice@…" cannot become two accounts in one tenant.
        builder.Property(u => u.Email).HasColumnType("citext").HasMaxLength(User.EmailMaxLength).IsRequired();
        builder.Property(u => u.DisplayName).HasMaxLength(User.DisplayNameMaxLength).IsRequired();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(u => u.TenantId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(u => new { u.TenantId, u.Email }).IsUnique();
        builder.HasIndex(u => new { u.TenantId, u.Source, u.ExternalId }).IsUnique();
    }
}
