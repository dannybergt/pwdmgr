using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pwdmgr.Domain.Identity;
using Pwdmgr.Domain.Tenants;

namespace Pwdmgr.Infrastructure.Persistence.Configurations;

internal sealed class LocalCredentialConfiguration : IEntityTypeConfiguration<LocalCredential>
{
    public void Configure(EntityTypeBuilder<LocalCredential> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.PasswordHash).HasMaxLength(LocalCredential.PasswordHashMaxLength).IsRequired();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);

        // Composite FK onto users(tenant_id, id): a credential cannot reference a user of
        // another tenant. One credential per user.
        builder.HasOne<User>().WithOne()
            .HasForeignKey<LocalCredential>(c => new { c.TenantId, c.UserId })
            .HasPrincipalKey<User>(u => new { u.TenantId, u.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => c.UserId).IsUnique();
    }
}
