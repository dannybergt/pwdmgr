using Microsoft.AspNetCore.Http;

namespace Pwdmgr.Api.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public const string CookieName = "pwdmgr_session";

    /// <summary>Absolute session lifetime. Sliding renewal is deliberately not implemented (plan §28: short sessions, server-side records).</summary>
    public TimeSpan SessionTtl { get; set; } = TimeSpan.FromHours(8);

    /// <summary>Secure by default; the compose dev stack (plain http behind Traefik) sets <c>SameAsRequest</c>.</summary>
    public CookieSecurePolicy CookieSecurePolicy { get; set; } = CookieSecurePolicy.Always;

    /// <summary>Failed and successful login attempts allowed per client + e-mail within <see cref="LoginRateLimitWindow"/>.</summary>
    public int LoginRateLimitPermits { get; set; } = 10;

    public TimeSpan LoginRateLimitWindow { get; set; } = TimeSpan.FromMinutes(1);
}
