using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pwdmgr.Domain.Tenants;
using Pwdmgr.Domain.Vaults;

namespace Pwdmgr.Infrastructure.Persistence.Configurations;

internal sealed class VaultConfiguration : IEntityTypeConfiguration<Vault>
{
    public void Configure(EntityTypeBuilder<Vault> builder)
    {
        builder.HasKey(v => v.Id);
        builder.HasAlternateKey(v => new { v.TenantId, v.Id });
        builder.Property(v => v.Type).HasConversion<int>();
        builder.Property(v => v.Status).HasConversion<int>();
        builder.Property(v => v.NameCiphertext).HasMaxLength(Vault.NameCiphertextMaxLength).IsRequired();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(v => v.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(v => new { v.TenantId, v.Status });
    }
}
