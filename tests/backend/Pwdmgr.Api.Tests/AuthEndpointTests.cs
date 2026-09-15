using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Pwdmgr.Api.Auth;
using Pwdmgr.Infrastructure.Tests;

namespace Pwdmgr.Api.Tests;

public sealed class AuthEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly Uri Login = new("/api/v1/auth/login", UriKind.Relative);
    private static readonly Uri Me = new("/api/v1/auth/me", UriKind.Relative);
    private static readonly Uri Logout = new("/api/v1/auth/logout", UriKind.Relative);

    [Fact]
    public async Task Login_sets_hardened_cookie_and_me_returns_the_user()
    {
        PostgresDatabase.SkipUnlessConfigured();
        using var client = factory.CreateApiClient();

        var response = await client.PostAsJsonAsync(Login, new LoginRequest(ApiFactory.TenantSlug, ApiFactory.Email, ApiFactory.Password), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith($"{AuthOptions.CookieName}=", setCookie, StringComparison.Ordinal);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);

        using var me = new HttpRequestMessage(HttpMethod.Get, Me);
        me.Headers.Add("Cookie", CookiePair(setCookie));
        var meResponse = await client.SendAsync(me, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var body = await meResponse.Content.ReadFromJsonAsync<MeResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(factory.UserId, body.UserId);
        Assert.Equal(factory.TenantId, body.TenantId);
        Assert.Equal(ApiFactory.Email, body.Email);
        Assert.Equal(ApiFactory.TenantSlug, body.TenantSlug);
    }

    [Fact]
    public async Task Login_is_case_insensitive_on_email()
    {
        PostgresDatabase.SkipUnlessConfigured();
        using var client = factory.CreateApiClient();
        var response = await client.PostAsJsonAsync(Login, new LoginRequest(ApiFactory.TenantSlug, "ALICE@Example.TEST", ApiFactory.Password), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_user_both_give_401_in_the_same_latency_class()
    {
        PostgresDatabase.SkipUnlessConfigured();
        using var client = factory.CreateApiClient();

        // Warm-up so JIT and connection setup do not skew the comparison.
        await client.PostAsJsonAsync(Login, new LoginRequest(ApiFactory.TenantSlug, ApiFactory.Email, "wrong"), TestContext.Current.CancellationToken);

        var wrong = await Time(() => client.PostAsJsonAsync(Login, new LoginRequest(ApiFactory.TenantSlug, ApiFactory.Email, "wrong"), TestContext.Current.CancellationToken));
        var unknown = await Time(() => client.PostAsJsonAsync(Login, new LoginRequest(ApiFactory.TenantSlug, "nobody@example.test", "wrong"), TestContext.Current.CancellationToken));
        var badTenant = await Time(() => client.PostAsJsonAsync(Login, new LoginRequest("no-such-tenant", ApiFactory.Email, ApiFactory.Password), TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.Response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.Response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, badTenant.Response.StatusCode);
        Assert.DoesNotContain("Set-Cookie", wrong.Response.Headers.Select(h => h.Key));

        // Both paths run the Argon2 verifier (~0.3-1 s); an unknown user must not be an order of magnitude faster.
        Assert.True(unknown.Elapsed > wrong.Elapsed / 3, $"unknown user answered in {unknown.Elapsed.TotalMilliseconds} ms vs {wrong.Elapsed.TotalMilliseconds} ms for a wrong password");
        Assert.True(badTenant.Elapsed > wrong.Elapsed / 3, $"unknown tenant answered in {badTenant.Elapsed.TotalMilliseconds} ms vs {wrong.Elapsed.TotalMilliseconds} ms");
    }

    [Fact]
    public async Task Me_without_cookie_and_with_garbage_cookie_is_401()
    {
        PostgresDatabase.SkipUnlessConfigured();
        using var client = factory.CreateApiClient();
        var none = await client.GetAsync(Me, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);

        using var garbage = new HttpRequestMessage(HttpMethod.Get, Me);
        garbage.Headers.Add("Cookie", $"{AuthOptions.CookieName}={Convert.ToBase64String(new byte[32])}");
        var response = await client.SendAsync(garbage, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_session_server_side()
    {
        PostgresDatabase.SkipUnlessConfigured();
        using var client = factory.CreateApiClient();
        var cookie = await LoginAsync(client);

        using var logout = new HttpRequestMessage(HttpMethod.Post, Logout);
        logout.Headers.Add("Cookie", cookie);
        var response = await client.SendAsync(logout, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var me = new HttpRequestMessage(HttpMethod.Get, Me);
        me.Headers.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(me, TestContext.Current.CancellationToken)).StatusCode);

        var tokenHash = System.Security.Cryptography.SHA256.HashData(Convert.FromBase64String(Uri.UnescapeDataString(cookie.Split('=', 2)[1])));
        await using var db = factory.CreateDbContext(factory.TenantId);
        var session = await db.Sessions.SingleAsync(s => s.TokenHash == tokenHash, TestContext.Current.CancellationToken);
        Assert.NotNull(session.RevokedAt);
    }

    [Fact]
    public async Task Cross_origin_post_is_rejected_same_origin_passes()
    {
        PostgresDatabase.SkipUnlessConfigured();
        using var client = factory.CreateApiClient();

        using var evil = new HttpRequestMessage(HttpMethod.Post, Login) { Content = JsonContent.Create(new LoginRequest(ApiFactory.TenantSlug, ApiFactory.Email, ApiFactory.Password)) };
        evil.Headers.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(evil, TestContext.Current.CancellationToken)).StatusCode);

        using var same = new HttpRequestMessage(HttpMethod.Post, Login) { Content = JsonContent.Create(new LoginRequest(ApiFactory.TenantSlug, ApiFactory.Email, ApiFactory.Password)) };
        same.Headers.Add("Origin", "https://localhost");
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(same, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Sessions_store_only_token_hashes_and_credentials_only_phc_strings()
    {
        PostgresDatabase.SkipUnlessConfigured();
        using var client = factory.CreateApiClient();
        var cookie = await LoginAsync(client);
        var token = cookie[(cookie.IndexOf('=', StringComparison.Ordinal) + 1)..];

        await using var db = factory.CreateDbContext(factory.TenantId);
        var hashes = await db.Sessions.Select(s => s.TokenHash).ToListAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(hashes);
        Assert.All(hashes, h => Assert.Equal(32, h.Length));
        Assert.DoesNotContain(Convert.FromBase64String(Uri.UnescapeDataString(token)), hashes);

        var credential = await db.LocalCredentials.SingleAsync(TestContext.Current.CancellationToken);
        Assert.StartsWith("$argon2id$v=19$m=65536,t=3,p=4$", credential.PasswordHash, StringComparison.Ordinal);
    }

    private async Task<string> LoginAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(Login, new LoginRequest(ApiFactory.TenantSlug, ApiFactory.Email, ApiFactory.Password), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return CookiePair(response.Headers.GetValues("Set-Cookie").Single());
    }

    private static string CookiePair(string setCookie) => setCookie.Split(';', 2)[0];

    private static async Task<(HttpResponseMessage Response, TimeSpan Elapsed)> Time(Func<Task<HttpResponseMessage>> call)
    {
        var sw = Stopwatch.StartNew();
        var response = await call();
        return (response, sw.Elapsed);
    }
}

/// <summary>Own fixture with a 3-second TTL so expiry is observed by really waiting (nex-im lesson).</summary>
public sealed class SessionExpiryTests(ShortTtlApiFactory factory) : IClassFixture<ShortTtlApiFactory>
{
    private static readonly Uri Login = new("/api/v1/auth/login", UriKind.Relative);
    private static readonly Uri Me = new("/api/v1/auth/me", UriKind.Relative);

    [Fact]
    public async Task Expired_session_is_rejected_after_the_ttl_really_passes()
    {
        PostgresDatabase.SkipUnlessConfigured();
        using var client = factory.CreateApiClient();
        var response = await client.PostAsJsonAsync(Login, new LoginRequest(ApiFactory.TenantSlug, ApiFactory.Email, ApiFactory.Password), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie").Single().Split(';', 2)[0];

        using var before = new HttpRequestMessage(HttpMethod.Get, Me);
        before.Headers.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(before, TestContext.Current.CancellationToken)).StatusCode);

        await Task.Delay(factory.SessionTtl + TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        using var after = new HttpRequestMessage(HttpMethod.Get, Me);
        after.Headers.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(after, TestContext.Current.CancellationToken)).StatusCode);
    }
}
