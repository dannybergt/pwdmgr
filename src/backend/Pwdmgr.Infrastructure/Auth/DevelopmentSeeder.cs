using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pwdmgr.Application.Auth;
using Pwdmgr.Domain.Identity;
using Pwdmgr.Domain.Tenants;
using Pwdmgr.Infrastructure.Persistence;

namespace Pwdmgr.Infrastructure.Auth;

/// <summary>Creates the dev tenant and admin user once. Idempotent; never overwrites an existing password.</summary>
public sealed class DevelopmentSeeder(PwdmgrDbContext db, IPasswordHasher hasher, IOptions<SeedOptions> options, ILogger<DevelopmentSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var seed = options.Value;
        if (string.IsNullOrEmpty(seed.AdminPassword))
        {
            throw new InvalidOperationException("Seed:Enabled is true but Seed:AdminPassword is not configured.");
        }

        var tenant = await db.Tenants.SingleOrDefaultAsync(t => t.Slug == seed.TenantSlug, cancellationToken);
        if (tenant is null)
        {
            tenant = new Tenant { Id = Guid.NewGuid(), Slug = seed.TenantSlug, DisplayName = seed.TenantDisplayName };
            db.Tenants.Add(tenant);
        }

        var user = await db.Users.IgnoreQueryFilters()
            .SingleOrDefaultAsync(u => u.TenantId == tenant.Id && u.Email == seed.AdminEmail, cancellationToken);
        if (user is not null)
        {
            logger.LogInformation("Seed: tenant {Slug} and admin user already present", seed.TenantSlug);
            return;
        }

        user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Source = UserSource.Local,
            Email = seed.AdminEmail,
            DisplayName = seed.AdminDisplayName
        };
        db.Users.Add(user);
        db.LocalCredentials.Add(new LocalCredential
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            UserId = user.Id,
            PasswordHash = hasher.Hash(seed.AdminPassword)
        });
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seed: created tenant {Slug} with one local admin user", seed.TenantSlug);
    }
}
