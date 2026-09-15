using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pwdmgr.Domain.Secrets;
using Pwdmgr.Domain.Tenants;
using Pwdmgr.Domain.Vaults;

namespace Pwdmgr.Infrastructure.Persistence.Configurations;

internal sealed class SecretConfiguration : IEntityTypeConfiguration<Secret>
{
    public void Configure(EntityTypeBuilder<Secret> builder)
    {
        builder.HasKey(s => s.Id);
        builder.HasAlternateKey(s => new { s.TenantId, s.Id });
        builder.Property(s => s.Type).HasMaxLength(Secret.TypeMaxLength).IsRequired();
        builder.Property(s => s.NameCiphertext).HasMaxLength(Secret.NameCiphertextMaxLength).IsRequired();
        builder.Property(s => s.Status).HasConversion<int>();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Vault>().WithMany()
            .HasForeignKey(s => new { s.TenantId, s.VaultId })
            .HasPrincipalKey(v => new { v.TenantId, v.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => new { s.TenantId, s.VaultId, s.Status });
    }
}

internal sealed class SecretVersionConfiguration : IEntityTypeConfiguration<SecretVersion>
{
    public void Configure(EntityTypeBuilder<SecretVersion> builder)
    {
        builder.HasKey(v => v.Id);
        builder.Property(v => v.PayloadCiphertext).HasMaxLength(SecretVersion.PayloadMaxLength).IsRequired();
        builder.Property(v => v.WrappedDek).HasMaxLength(SecretVersion.WrappedDekMaxLength).IsRequired();
        builder.Property(v => v.AadHash).HasMaxLength(SecretVersion.AadHashLength).IsRequired();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(v => v.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Secret>().WithMany()
            .HasForeignKey(v => new { v.TenantId, v.SecretId })
            .HasPrincipalKey(s => new { s.TenantId, s.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(v => new { v.TenantId, v.SecretId, v.VersionNo }).IsUnique();
    }
}
