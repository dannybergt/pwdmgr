using Microsoft.EntityFrameworkCore;
using Pwdmgr.Application.Auth;
using Pwdmgr.Domain.Common;
using Pwdmgr.Domain.Crypto;
using Pwdmgr.Domain.Identity;
using Pwdmgr.Domain.Sessions;
using Pwdmgr.Domain.Tenants;
using Pwdmgr.Domain.Vaults;

namespace Pwdmgr.Infrastructure.Persistence;

public sealed class PwdmgrDbContext(DbContextOptions<PwdmgrDbContext> options, ICurrentTenant currentTenant) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<User> Users => Set<User>();

    public DbSet<LocalCredential> LocalCredentials => Set<LocalCredential>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<UserKeyring> UserKeyrings => Set<UserKeyring>();

    public DbSet<Vault> Vaults => Set<Vault>();

    public DbSet<WrappedKey> WrappedKeys => Set<WrappedKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("citext");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PwdmgrDbContext).Assembly);

        // Every tenant-scoped entity is filtered to the current request's tenant. Before
        // authentication TenantId is Guid.Empty, so nothing matches; code that legitimately
        // runs without a tenant (login, seeding, migrations) uses IgnoreQueryFilters explicitly.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(TenantScopedEntity).IsAssignableFrom(entityType.ClrType))
            {
                var method = typeof(PwdmgrDbContext).GetMethod(nameof(ApplyTenantFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .MakeGenericMethod(entityType.ClrType);
                method.Invoke(this, [modelBuilder]);
            }
        }
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : TenantScopedEntity
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.TenantId == currentTenant.TenantId);
    }
}
