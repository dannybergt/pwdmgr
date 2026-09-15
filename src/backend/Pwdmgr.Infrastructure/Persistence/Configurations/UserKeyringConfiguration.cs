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

        // bytea has no length in Postgres; the API limits are mirrored as check constraints so no
        // other writer can store oversized or malformed blobs.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_user_keyrings_public_key_len", $"octet_length(public_key) = {UserKeyring.PublicKeyLength}");
            t.HasCheckConstraint("ck_user_keyrings_kdf_salt_len", $"octet_length(kdf_salt) BETWEEN {UserKeyring.KdfSaltMinLength} AND {UserKeyring.KdfSaltMaxLength}");
            t.HasCheckConstraint("ck_user_keyrings_private_key_len", $"octet_length(encrypted_private_key) BETWEEN 28 AND {UserKeyring.EncryptedPrivateKeyMaxLength}");
            t.HasCheckConstraint("ck_user_keyrings_kdf_range", $"kdf_memory_kib BETWEEN {KdfLimits.MinMemoryKib} AND {KdfLimits.MaxMemoryKib} AND kdf_iterations BETWEEN {KdfLimits.MinIterations} AND {KdfLimits.MaxIterations} AND kdf_parallelism BETWEEN {KdfLimits.MinParallelism} AND {KdfLimits.MaxParallelism}");
        });
    }
}
