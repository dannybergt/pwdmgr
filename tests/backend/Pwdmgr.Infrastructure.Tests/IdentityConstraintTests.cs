using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pwdmgr.Domain.Identity;
using Pwdmgr.Domain.Tenants;

namespace Pwdmgr.Infrastructure.Tests;

public sealed class IdentityConstraintTests(PostgresDatabase db) : IClassFixture<PostgresDatabase>, IAsyncLifetime
{
    private const string UniqueViolation = "23505";
    private const string ForeignKeyViolation = "23503";

    public async ValueTask InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(db.ConnectionString))
        {
            return;
        }

        await using var context = db.CreateContext();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Same_email_twice_in_one_tenant_is_rejected()
    {
        PostgresDatabase.SkipUnlessConfigured();
        var tenant = NewTenant();
        await using (var context = db.CreateContext())
        {
            context.Tenants.Add(tenant);
            context.Users.Add(NewUser(tenant, "alice@example.test"));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var context = db.CreateContext())
        {
            context.Users.Add(NewUser(tenant, "alice@example.test"));
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
            var pg = Assert.IsType<PostgresException>(ex.InnerException);
            Assert.Equal(UniqueViolation, pg.SqlState);
        }
    }

    [Fact]
    public async Task Email_uniqueness_is_case_insensitive()
    {
        PostgresDatabase.SkipUnlessConfigured();
        var tenant = NewTenant();
        await using var context = db.CreateContext();
        context.Tenants.Add(tenant);
        context.Users.Add(NewUser(tenant, "Dave@Example.test"));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.True(await context.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == "dave@example.test", TestContext.Current.CancellationToken));

        context.Users.Add(NewUser(tenant, "dave@example.test"));
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(UniqueViolation, Assert.IsType<PostgresException>(ex.InnerException).SqlState);
    }

    [Fact]
    public async Task Credential_cannot_reference_a_user_of_another_tenant()
    {
        PostgresDatabase.SkipUnlessConfigured();
        var a = NewTenant();
        var b = NewTenant();
        var user = NewUser(a, "erin@example.test");
        await using var context = db.CreateContext();
        context.Tenants.AddRange(a, b);
        context.Users.Add(user);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.LocalCredentials.Add(new LocalCredential
        {
            Id = Guid.NewGuid(),
            TenantId = b.Id,
            UserId = user.Id,
            PasswordHash = NewCredential(user).PasswordHash
        });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ForeignKeyViolation, Assert.IsType<PostgresException>(ex.InnerException).SqlState);
    }

    [Fact]
    public async Task Same_email_in_two_tenants_is_allowed()
    {
        PostgresDatabase.SkipUnlessConfigured();
        var a = NewTenant();
        var b = NewTenant();
        await using var context = db.CreateContext();
        context.Tenants.AddRange(a, b);
        context.Users.AddRange(NewUser(a, "bob@example.test"), NewUser(b, "bob@example.test"));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, await context.Users.IgnoreQueryFilters().CountAsync(u => u.Email == "bob@example.test", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Duplicate_tenant_slug_is_rejected()
    {
        PostgresDatabase.SkipUnlessConfigured();
        var slug = $"t-{Guid.NewGuid():N}";
        await using var context = db.CreateContext();
        context.Tenants.Add(new Tenant { Id = Guid.NewGuid(), Slug = slug, DisplayName = "one" });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.Tenants.Add(new Tenant { Id = Guid.NewGuid(), Slug = slug, DisplayName = "two" });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(UniqueViolation, Assert.IsType<PostgresException>(ex.InnerException).SqlState);
    }

    [Fact]
    public async Task Local_credential_is_one_per_user_and_cascades_on_user_delete()
    {
        PostgresDatabase.SkipUnlessConfigured();
        var tenant = NewTenant();
        var user = NewUser(tenant, "carol@example.test");
        await using (var context = db.CreateContext())
        {
            context.Tenants.Add(tenant);
            context.Users.Add(user);
            context.LocalCredentials.Add(NewCredential(user));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var context = db.CreateContext())
        {
            context.LocalCredentials.Add(NewCredential(user));
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
            Assert.Equal(UniqueViolation, Assert.IsType<PostgresException>(ex.InnerException).SqlState);
        }

        await using (var context = db.CreateContext(tenant.Id))
        {
            await context.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync(TestContext.Current.CancellationToken);
            Assert.False(await context.LocalCredentials.AnyAsync(c => c.UserId == user.Id, TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Tenant_query_filter_hides_other_tenants_and_everything_without_context()
    {
        PostgresDatabase.SkipUnlessConfigured();
        var a = NewTenant();
        var b = NewTenant();
        await using (var context = db.CreateContext())
        {
            context.Tenants.AddRange(a, b);
            context.Users.AddRange(NewUser(a, "frank@example.test"), NewUser(b, "grace@example.test"));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var scoped = db.CreateContext(a.Id))
        {
            var emails = await scoped.Users.Select(u => u.Email).ToListAsync(TestContext.Current.CancellationToken);
            Assert.Equal(["frank@example.test"], emails);
        }

        await using (var unscoped = db.CreateContext())
        {
            Assert.Empty(await unscoped.Users.ToListAsync(TestContext.Current.CancellationToken));
            Assert.Equal(2, await unscoped.Users.IgnoreQueryFilters().CountAsync(u => u.TenantId == a.Id || u.TenantId == b.Id, TestContext.Current.CancellationToken));
        }
    }

    private static Tenant NewTenant() => new()
    {
        Id = Guid.NewGuid(),
        Slug = $"t-{Guid.NewGuid():N}",
        DisplayName = "Test tenant"
    };

    private static User NewUser(Tenant tenant, string email) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenant.Id,
        Source = UserSource.Local,
        Email = email,
        DisplayName = email
    };

    private static LocalCredential NewCredential(User user) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = user.TenantId,
        UserId = user.Id,
        PasswordHash = "$argon2id$v=19$m=65536,t=3,p=4$c29tZXNhbHQ$placeholder-not-a-real-verifier"
    };
}
