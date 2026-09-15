using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pwdmgr.Domain.Crypto;
using Pwdmgr.Domain.Identity;
using Pwdmgr.Domain.Vaults;
using Pwdmgr.Domain.Tenants;

namespace Pwdmgr.Infrastructure.Persistence.Configurations;

internal sealed class WrappedKeyConfiguration : IEntityTypeConfiguration<WrappedKey>
{
    public void Configure(EntityTypeBuilder<WrappedKey> builder)
    {
        builder.HasKey(w => w.Id);
        builder.Property(w => w.ResourceType).HasConversion<int>();
        builder.Property(w => w.RecipientType).HasConversion<int>();
        builder.Property(w => w.Ciphertext).HasMaxLength(WrappedKey.CiphertextMaxLength).IsRequired();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(w => w.TenantId).OnDelete(DeleteBehavior.Restrict);

        // The resource/recipient columns are polymorphic by design (product plan §21), but with one
        // type each today they get real composite FKs (ADR-0007 convention) plus a check constraint
        // that pins the types; a second type arrives together with its own FK and a wider check.
        builder.HasOne<Vault>().WithMany()
            .HasForeignKey(w => new { w.TenantId, w.ResourceId })
            .HasPrincipalKey(v => new { v.TenantId, v.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(w => new { w.TenantId, w.RecipientId })
            .HasPrincipalKey(u => new { u.TenantId, u.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(w => new { w.TenantId, w.ResourceType, w.ResourceId, w.RecipientType, w.RecipientId, w.KeyVersion })
            .IsUnique()
            .HasDatabaseName("ux_wrapped_keys_resource_recipient_version");
        builder.HasIndex(w => new { w.TenantId, w.RecipientType, w.RecipientId });

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_wrapped_keys_types", "resource_type = 1 AND recipient_type = 1");
            t.HasCheckConstraint("ck_wrapped_keys_ciphertext_len", $"octet_length(ciphertext) BETWEEN 60 AND {WrappedKey.CiphertextMaxLength}");
        });
    }
}
