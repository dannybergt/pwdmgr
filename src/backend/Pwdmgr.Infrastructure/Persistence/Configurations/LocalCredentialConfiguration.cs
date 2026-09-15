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
        builder.HasOne<User>().WithOne().HasForeignKey<LocalCredential>(c => c.UserId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => new { c.TenantId, c.UserId }).IsUnique();
    }
}
