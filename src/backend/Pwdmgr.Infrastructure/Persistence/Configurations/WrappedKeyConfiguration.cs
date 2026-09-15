using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pwdmgr.Domain.Crypto;
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

        builder.HasIndex(w => new { w.TenantId, w.ResourceType, w.ResourceId, w.RecipientType, w.RecipientId, w.KeyVersion }).IsUnique();
        builder.HasIndex(w => new { w.TenantId, w.RecipientType, w.RecipientId });
    }
}
