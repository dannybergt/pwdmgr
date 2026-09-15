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

        auth.MapPost("/logout", LogoutAsync).RequireAuthorization();

        auth.MapGet("/me", MeAsync).RequireAuthorization();

        return api;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext http,
        SessionService sessions,
        LoginThrottle throttle,
        IOptions<AuthOptions> options,
        CancellationToken cancellationToken)
    {
        if (!MiniValidation.TryValidate(request, out var errors))
        {
            return Results.ValidationProblem(errors);
        }

        if (!await throttle.TryAcquireAsync(http.Connection.RemoteIpAddress?.ToString() ?? "unknown", request.Email, cancellationToken))
        {
            return Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many login attempts");
        }

        var result = await sessions.LoginAsync(request.TenantSlug, request.Email, request.Password, options.Value.SessionTtl, cancellationToken);
        if (result is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid credentials");
        }

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

    private static async Task<IResult> LogoutAsync(HttpContext http, ICurrentUser user, SessionService sessions, CancellationToken cancellationToken)
    {
        await sessions.RevokeAsync(user.SessionId, cancellationToken);
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
