using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Pwdmgr.Infrastructure.Tests;

namespace Pwdmgr.Api.Tests;

/// <summary>
/// Runs in the Development environment (like the compose stack), where ASP.NET's default would
/// rethrow body-binding failures and the exception handler would answer 500 with a logged stack trace.
/// </summary>
public sealed class MalformedBodyTests(DevelopmentApiFactory factory) : IClassFixture<DevelopmentApiFactory>
{
    private static readonly Uri Login = new("/api/v1/auth/login", UriKind.Relative);
    private static readonly Uri Keyring = new("/api/v1/me/keyring", UriKind.Relative);

    public static TheoryData<string> Bodies => new(["7", "\"str\"", "[]", "null", "not json", ""]);

    [Theory]
    [MemberData(nameof(Bodies))]
    public async Task Malformed_login_body_is_400_not_500_even_unauthenticated(string body)
    {
        PostgresDatabase.SkipUnlessConfigured();
        using var client = factory.CreateApiClient();
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await client.PostAsync(Login, content, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.DoesNotContain(factory.Logs, entry => entry.Level >= LogLevel.Error);
        var problem = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("traceId", problem);
        Assert.DoesNotContain("not json", problem);
    }

    [Fact]
    public async Task Malformed_write_body_is_400_when_authenticated()
    {
        PostgresDatabase.SkipUnlessConfigured();
        using var client = await factory.CreateAuthenticatedClientAsync();
        using var content = new StringContent("[]", Encoding.UTF8, "application/json");
        var response = await client.PutAsync(Keyring, content, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
