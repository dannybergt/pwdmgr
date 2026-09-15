using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pwdmgr.Domain.Tenants;

namespace Pwdmgr.Infrastructure.Persistence.Configurations;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Slug).HasMaxLength(Tenant.SlugMaxLength).IsRequired();
        builder.Property(t => t.DisplayName).HasMaxLength(Tenant.DisplayNameMaxLength).IsRequired();
        builder.Property(t => t.Status).HasConversion<int>();
        builder.HasIndex(t => t.Slug).IsUnique();
    }
}
