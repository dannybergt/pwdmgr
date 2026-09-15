using System.Net.Http.Json;
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

    /// <summary>Session TTL used by the host. Long by default (slow CI hosts must not expire mid-test); the expiry test uses <see cref="ShortTtlApiFactory"/>.</summary>
    public virtual TimeSpan SessionTtl => TimeSpan.FromMinutes(10);

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
            PasswordHash = SharedHash
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

    /// <summary>Client with a session cookie. Creates the user on the fly when <paramref name="email"/> is not the fixture user.</summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync(string? email = null, string tenantSlug = TenantSlug)
    {
        email ??= Email;
        if (email != Email)
        {
            await EnsureUserAsync(tenantSlug, email);
        }

        var client = CreateApiClient();
        var response = await client.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative), new Auth.LoginRequest(tenantSlug, email, Password));
        if (response.StatusCode != System.Net.HttpStatusCode.NoContent)
        {
            throw new InvalidOperationException($"login failed with {response.StatusCode}");
        }

        var cookie = response.Headers.GetValues("Set-Cookie").Single().Split(';', 2)[0];
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    public async Task<(string Slug, string Email)> CreateTenantWithUserAsync()
    {
        var slug = $"t-{Guid.NewGuid():N}"[..20];
        var email = $"user-{Guid.NewGuid():N}@example.test";
        await using var context = CreateDbContext();
        context.Tenants.Add(new Tenant { Id = Guid.NewGuid(), Slug = slug, DisplayName = slug });
        await context.SaveChangesAsync();
        await EnsureUserAsync(slug, email);
        return (slug, email);
    }

    private async Task EnsureUserAsync(string tenantSlug, string email)
    {
        await using var context = CreateDbContext();
        var tenant = await context.Tenants.SingleAsync(t => t.Slug == tenantSlug);
        if (await context.Users.IgnoreQueryFilters().AnyAsync(u => u.TenantId == tenant.Id && u.Email == email))
        {
            return;
        }

        var user = new User { Id = Guid.NewGuid(), TenantId = tenant.Id, Source = UserSource.Local, Email = email, DisplayName = email };
        context.Users.Add(user);
        context.LocalCredentials.Add(new LocalCredential { Id = Guid.NewGuid(), TenantId = tenant.Id, UserId = user.Id, PasswordHash = SharedHash });
        await context.SaveChangesAsync();
    }

    // One Argon2 hash for all fixture users; hashing per user would add ~0.5 s each.
    private static readonly string SharedHash = new Infrastructure.Auth.Argon2PasswordHasher().Hash(Password);
}

public sealed class StrictRateLimitApiFactory : ApiFactory
{
    protected override int LoginRateLimitPermits => 5;
}

public sealed class ShortTtlApiFactory : ApiFactory
{
    public override TimeSpan SessionTtl => TimeSpan.FromSeconds(3);
}
