using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pwdmgr.Domain.Crypto;
using Pwdmgr.Domain.Identity;
using Pwdmgr.Domain.Tenants;

namespace Pwdmgr.Infrastructure.Persistence.Configurations;

internal sealed class UserKeyringConfiguration : IEntityTypeConfiguration<UserKeyring>
{
    public void Configure(EntityTypeBuilder<UserKeyring> builder)
    {
        builder.HasKey(k => k.Id);
        builder.Property(k => k.KdfAlgorithm).HasMaxLength(32).IsRequired();
        builder.Property(k => k.KdfSalt).HasMaxLength(UserKeyring.KdfSaltMaxLength).IsRequired();
        builder.Property(k => k.PublicKey).HasMaxLength(UserKeyring.PublicKeyLength).IsRequired();
        builder.Property(k => k.EncryptedPrivateKey).HasMaxLength(UserKeyring.EncryptedPrivateKeyMaxLength).IsRequired();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(k => k.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithOne()
            .HasForeignKey<UserKeyring>(k => new { k.TenantId, k.UserId })
            .HasPrincipalKey<User>(u => new { u.TenantId, u.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(k => k.UserId).IsUnique();
    }
}
