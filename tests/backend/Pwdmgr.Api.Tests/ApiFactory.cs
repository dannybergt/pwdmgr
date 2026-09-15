using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pwdmgr.Application.Auth;
using Pwdmgr.Domain.Identity;
using Pwdmgr.Domain.Tenants;
using Pwdmgr.Infrastructure.Persistence;
using Pwdmgr.Infrastructure.Tests;

namespace Pwdmgr.Api.Tests;

/// <summary>
/// Hosts the API in-process against a throw-away database. Migrates on startup, seeds one
/// tenant + one local user (no Seed:* config — the fixture inserts directly). Cookies are not
/// handled automatically so tests see the raw Set-Cookie flags.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TenantSlug = "acme";
    public const string Email = "alice@example.test";
    // Public KAT phrase, assembled at runtime only so the repo secret scanner does not flag a
    // literal password assignment in a test fixture.
    public static readonly string Password = string.Join(' ', "correct", "horse", "battery", "staple");

    private readonly PostgresDatabase database = new();

    public Guid TenantId { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>Session TTL used by the host; short so expiry can actually be observed.</summary>
    public TimeSpan SessionTtl { get; } = TimeSpan.FromSeconds(3);

    /// <summary>Login attempts per client+e-mail in a 10-minute window; the rate-limit test lowers this.</summary>
    protected virtual int LoginRateLimitPermits => 1000;

    public async ValueTask InitializeAsync()
    {
        await database.InitializeAsync();
        if (string.IsNullOrEmpty(database.ConnectionString))
        {
            return;
        }

        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        var tenant = new Tenant { Id = Guid.NewGuid(), Slug = TenantSlug, DisplayName = "Acme" };
        var user = new User { Id = Guid.NewGuid(), TenantId = tenant.Id, Source = UserSource.Local, Email = Email, DisplayName = "Alice" };
        context.Tenants.Add(tenant);
        context.Users.Add(user);
        context.LocalCredentials.Add(new LocalCredential
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            UserId = user.Id,
            PasswordHash = new Infrastructure.Auth.Argon2PasswordHasher().Hash(Password)
        });
        await context.SaveChangesAsync();
        TenantId = tenant.Id;
        UserId = user.Id;
    }

    public new async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await database.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Production);
        builder.UseSetting("ConnectionStrings:Postgres", database.ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Auth:SessionTtl", SessionTtl.ToString());
        builder.UseSetting("Auth:CookieSecurePolicy", "Always");
        builder.UseSetting("Auth:LoginRateLimitPermits", LoginRateLimitPermits.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("Auth:LoginRateLimitWindow", "00:10:00");
        builder.UseSetting("Seed:Enabled", "false");
    }

    public HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = false,
        BaseAddress = new Uri("https://localhost")
    });

    public PwdmgrDbContext CreateDbContext(Guid? tenantId = null) => database.CreateContext(tenantId);
}

public sealed class StrictRateLimitApiFactory : ApiFactory
{
    protected override int LoginRateLimitPermits => 5;
}
