using System.Net;
using System.Net.Http.Json;
using Pwdmgr.Api.Auth;
using Pwdmgr.Infrastructure.Tests;

namespace Pwdmgr.Api.Tests;

/// <summary>Own fixture instance: the fixed window is per client address and must not be shared with the other tests.</summary>
public sealed class RateLimitTests(StrictRateLimitApiFactory factory) : IClassFixture<StrictRateLimitApiFactory>
{
    [Fact]
    public async Task Sixth_login_attempt_in_the_window_is_429()
    {
        PostgresDatabase.SkipUnlessConfigured();
        using var client = factory.CreateApiClient();
        var login = new Uri("/api/v1/auth/login", UriKind.Relative);

        for (var i = 0; i < 5; i += 1)
        {
            var response = await client.PostAsJsonAsync(login, new LoginRequest(ApiFactory.TenantSlug, ApiFactory.Email, "wrong"), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var limited = await client.PostAsJsonAsync(login, new LoginRequest(ApiFactory.TenantSlug, ApiFactory.Email, ApiFactory.Password), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);

        // Another account from the same client is not affected (per address + e-mail).
        var other = await client.PostAsJsonAsync(login, new LoginRequest(ApiFactory.TenantSlug, "someone-else@example.test", "wrong"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, other.StatusCode);
    }
}
