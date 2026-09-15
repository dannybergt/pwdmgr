using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Pwdmgr.Application.Auth;
using Pwdmgr.Infrastructure.Auth;

namespace Pwdmgr.Api.Auth;

/// <summary>Reads the session cookie, resolves the server-side session and populates the request context.</summary>
public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    SessionService sessions,
    RequestContext requestContext)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Session";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Cookies.TryGetValue(AuthOptions.CookieName, out var token) || string.IsNullOrEmpty(token))
        {
            return AuthenticateResult.NoResult();
        }

        var principal = await sessions.ResolveAsync(token, Context.RequestAborted);
        if (principal is null)
        {
            return AuthenticateResult.Fail("invalid or expired session");
        }

        requestContext.Authenticate(principal.TenantId, principal.UserId, principal.SessionId);
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, principal.UserId.ToString()),
            new Claim("tenant_id", principal.TenantId.ToString()),
            new Claim("session_id", principal.SessionId.ToString())
        ], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        // API clients get a bare 401; no redirect, no WWW-Authenticate realm to enumerate.
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }
}
