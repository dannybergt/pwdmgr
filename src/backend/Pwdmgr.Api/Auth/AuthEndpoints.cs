using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pwdmgr.Application.Auth;
using Pwdmgr.Infrastructure.Auth;
using Pwdmgr.Infrastructure.Persistence;

namespace Pwdmgr.Api.Auth;

public sealed record LoginRequest(
    [property: Required, StringLength(64, MinimumLength = 1)] string TenantSlug,
    [property: Required, StringLength(320, MinimumLength = 3)] string Email,
    [property: Required, StringLength(1024, MinimumLength = 1)] string Password);

public sealed record MeResponse(Guid UserId, Guid TenantId, string TenantSlug, string Email, string DisplayName);

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuth(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth");

        auth.MapPost("/login", LoginAsync).AllowAnonymous();

        // Idempotent: an expired or missing cookie still gets the cookie cleared and a 204.
        auth.MapPost("/logout", LogoutAsync).AllowAnonymous();

        auth.MapGet("/me", MeAsync).RequireAuthorization();

        return api;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext http,
        SessionService sessions,
        LoginThrottle throttle,
        IOptions<AuthOptions> options,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Pwdmgr.Audit.Auth");
        if (!MiniValidation.TryValidate(request, out var errors))
        {
            return Results.ValidationProblem(errors);
        }

        var client = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!await throttle.TryAcquireAsync(client, request.TenantSlug, request.Email, cancellationToken))
        {
            AuditLog.Login(logger, "rate_limited", request.TenantSlug, null, client);
            http.Response.Headers.RetryAfter = ((int)throttle.Window.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many login attempts");
        }

        LoginResult? result;
        using (var slot = throttle.TryEnterVerifierGate())
        {
            if (slot is null)
            {
                AuditLog.Login(logger, "busy", request.TenantSlug, null, client);
                http.Response.Headers.RetryAfter = "2";
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Login temporarily unavailable, retry shortly");
            }

            result = await sessions.LoginAsync(request.TenantSlug, request.Email, request.Password, options.Value.SessionTtl, cancellationToken);
        }

        if (result is null)
        {
            AuditLog.Login(logger, "invalid_credentials", request.TenantSlug, null, client);
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid credentials");
        }

        AuditLog.Login(logger, "ok", request.TenantSlug, result, client);

        http.Response.Cookies.Append(AuthOptions.CookieName, result.Token, new CookieOptions
        {
            HttpOnly = true,
            Secure = options.Value.CookieSecurePolicy switch
            {
                CookieSecurePolicy.Always => true,
                CookieSecurePolicy.None => false,
                _ => http.Request.IsHttps
            },
            SameSite = SameSiteMode.Strict,
            Path = "/",
            MaxAge = options.Value.SessionTtl,
            IsEssential = true
        });
        return Results.NoContent();
    }

    private static async Task<IResult> LogoutAsync(HttpContext http, ICurrentUser user, ICurrentTenant tenant, SessionService sessions, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        if (user.IsAuthenticated)
        {
            await sessions.RevokeAsync(user.SessionId, cancellationToken);
            loggerFactory.CreateLogger("Pwdmgr.Audit.Auth").LogInformation(
                AuditLog.LogoutEvent, "Logout tenant={TenantId} user={UserId} session={SessionId}", tenant.TenantId, user.UserId, user.SessionId);
        }

        http.Response.Cookies.Delete(AuthOptions.CookieName, new CookieOptions { Path = "/", HttpOnly = true, SameSite = SameSiteMode.Strict });
        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(ICurrentUser user, PwdmgrDbContext db, CancellationToken cancellationToken)
    {
        var me = await db.Users
            .Where(u => u.Id == user.UserId)
            .Join(db.Tenants, u => u.TenantId, t => t.Id, (u, t) => new MeResponse(u.Id, u.TenantId, t.Slug, u.Email, u.DisplayName))
            .SingleAsync(cancellationToken);
        return Results.Ok(me);
    }
}
