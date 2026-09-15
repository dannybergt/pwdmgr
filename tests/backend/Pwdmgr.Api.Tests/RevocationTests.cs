using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pwdmgr.Api.Auth;
using Pwdmgr.Domain.Identity;
using Pwdmgr.Infrastructure.Auth;
using Pwdmgr.Infrastructure.Tests;

namespace Pwdmgr.Api.Tests;

public sealed class RevocationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly Uri Login = new("/api/v1/auth/login", UriKind.Relative);
    private static readonly Uri Me = new("/api/v1/auth/me", UriKind.Relative);
    private static readonly Uri Logout = new("/api/v1/auth/logout", UriKind.Relative);

    [Fact]
    public async Task Disabling_a_user_kills_existing_sessions_and_blocks_login()
    {
        PostgresDatabase.SkipUnlessConfigured();
        var email = $"disabled-{Guid.NewGuid():N}@example.test";
        using var client = await factory.CreateAuthenticatedClientAsync(email);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Me, TestContext.Current.CancellationToken)).StatusCode);

        await using (var db = factory.CreateDbContext(factory.TenantId))
        {
            await db.Users.Where(u => u.Email == email).ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, UserStatus.Disabled), TestContext.Current.CancellationToken);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Me, TestContext.Current.CancellationToken)).StatusCode);
        using var fresh = factory.CreateApiClient();
        var login = await fresh.PostAsJsonAsync(Login, new LoginRequest(ApiFactory.TenantSlug, email, ApiFactory.Password), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Logout_is_idempotent_without_a_session()
    {
        PostgresDatabase.SkipUnlessConfigured();
        using var client = factory.CreateApiClient();
        var response = await client.PostAsync(Logout, null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains("pwdmgr_session=;", response.Headers.GetValues("Set-Cookie").Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Purge_removes_sessions_expired_longer_than_the_retention()
    {
        PostgresDatabase.SkipUnlessConfigured();
        // The hosted service purges once at startup; start the host first so the row below is not
        // swept away before the explicit run we are measuring.
        var purge = factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<SessionPurgeService>().Single();

        var old = Guid.NewGuid();
        var recent = Guid.NewGuid();
        await using (var db = factory.CreateDbContext(factory.TenantId))
        {
            db.Sessions.AddRange(
                NewSession(old, DateTimeOffset.UtcNow.AddDays(-20)),
                NewSession(recent, DateTimeOffset.UtcNow.AddDays(-1)));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(1, await purge.PurgeOnceAsync(TestContext.Current.CancellationToken));

        await using var check = factory.CreateDbContext(factory.TenantId);
        Assert.False(await check.Sessions.AnyAsync(s => s.Id == old, TestContext.Current.CancellationToken));
        Assert.True(await check.Sessions.AnyAsync(s => s.Id == recent, TestContext.Current.CancellationToken), "expired less than the retention ago must be kept for audit");
    }

    private Domain.Sessions.Session NewSession(Guid id, DateTimeOffset expiresAt) => new()
    {
        Id = id,
        TenantId = factory.TenantId,
        UserId = factory.UserId,
        TokenHash = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32),
        CreatedAt = expiresAt.AddHours(-8),
        ExpiresAt = expiresAt,
        LastSeenAt = expiresAt
    };
}

/// <summary>Own fixture: the per-client window (8 attempts) and the single verifier slot must not be consumed by other tests.</summary>
public sealed class ThrottleTests(StrictRateLimitApiFactory factory) : IClassFixture<StrictRateLimitApiFactory>
{
    private static readonly Uri Login = new("/api/v1/auth/login", UriKind.Relative);

    [Fact]
    public async Task Per_client_window_stops_spraying_across_accounts()
    {
        PostgresDatabase.SkipUnlessConfigured();
        using var client = factory.CreateApiClient();
        // Strict fixture: 5 per account, 8 per client. Nine different accounts from one client → the 9th is throttled.
        HttpStatusCode last = default;
        for (var i = 0; i < 9; i += 1)
        {
            var response = await client.PostAsJsonAsync(Login, new LoginRequest(ApiFactory.TenantSlug, $"spray-{i}@example.test", "wrong"), TestContext.Current.CancellationToken);
            last = response.StatusCode;
            if (i < 8)
            {
                Assert.Equal(HttpStatusCode.Unauthorized, last);
            }
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

}

/// <summary>Own fixture: one verifier slot, generous windows, so the 503 path is isolated from the window tests.</summary>
public sealed class VerifierGateTests(SingleSlotApiFactory factory) : IClassFixture<SingleSlotApiFactory>
{
    private static readonly Uri Login = new("/api/v1/auth/login", UriKind.Relative);

    [Fact]
    public async Task Verifier_gate_answers_503_when_every_slot_is_busy()
    {
        PostgresDatabase.SkipUnlessConfigured();
        var throttle = factory.Services.GetRequiredService<LoginThrottle>();
        // Occupy the single slot of the strict fixture, then a real login must not queue.
        using var held = throttle.TryEnterVerifierGate();
        Assert.NotNull(held);
        using var client = factory.CreateApiClient();
        var response = await client.PostAsJsonAsync(Login, new LoginRequest(ApiFactory.TenantSlug, $"busy-{Guid.NewGuid():N}@example.test", "wrong"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
    }
}
