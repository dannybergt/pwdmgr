using System.Reflection;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Pwdmgr.Api.Auth;
using Pwdmgr.Api.Secrets;
using Pwdmgr.Api.Vaults;
using Pwdmgr.Application;
using Pwdmgr.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Structured JSON logs with UTC ISO 8601 timestamps and request scopes (Constitution §8).
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
});

builder.Services.AddPwdmgrApplication();
builder.Services.AddPwdmgrInfrastructure(builder.Configuration);

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.AddAuthentication(SessionAuthenticationHandler.SchemeName)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorization();

builder.Services.AddSingleton<LoginThrottle>();

// Behind Traefik: trust the proxy for scheme and client address. Known networks are the
// compose-internal ranges; production must set Forwarded:KnownNetworks explicitly.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    foreach (var cidr in builder.Configuration.GetSection("Forwarded:KnownNetworks").Get<string[]>() ?? [])
    {
        options.KnownNetworks.Add(Microsoft.AspNetCore.HttpOverrides.IPNetwork.Parse(cidr));
    }
});

// Largest legitimate body: a 64 KiB secret payload as Base64 inside JSON (~90 KiB).
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Limits.MaxRequestBodySize = 256 * 1024;
    kestrel.AddServerHeader = false;
});

// Authenticated write routes: a token bucket per user so one script cannot flood the shared
// database (Constitution §6 rate limits). Reads are not limited.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(WriteRateLimit.PolicyName, http =>
    {
        var permits = http.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthOptions>>().Value.WriteRequestsPerMinute;
        return RateLimitPartition.GetTokenBucketLimiter(
            http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? http.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = permits,
                TokensPerPeriod = permits,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });
});

builder.Services.AddProblemDetails();

// A malformed body is the client's fault in every environment: 400 without an error log. The
// framework's Development default rethrows binding failures, which the exception handler
// turns into a logged 500 — reachable unauthenticated on /auth/login.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);

var app = builder.Build();

if (!app.Environment.IsDevelopment() && !builder.Configuration.GetSection("Forwarded:KnownNetworks").Exists())
{
    // Without a trusted proxy network every client shares Traefik's address: the login
    // throttle would then lock out everybody behind the proxy and cookies would never be Secure.
    app.Logger.LogWarning("Forwarded:KnownNetworks is empty; behind a reverse proxy set it to the proxy network CIDR");
}

app.UseForwardedHeaders();
app.UseExceptionHandler();

// Liveness runs no checks; readiness runs everything tagged "ready" (Postgres).
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(HealthTags.Ready)
});

app.UseMiddleware<SameOriginMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

var forwarded = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ForwardedHeadersOptions>>().Value;
app.Logger.LogInformation("Forwarded headers trusted from {Networks}", string.Join(", ", forwarded.KnownNetworks.Select(n => $"{n.Prefix}/{n.PrefixLength}")));

var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
var commit = Environment.GetEnvironmentVariable("PWDMGR_COMMIT") ?? "unknown";

var api = app.MapGroup("/api/v1");

// API responses are never cacheable and never sniffed; the web image sets the same on its own responses.
api.AddEndpointFilter(async (context, next) =>
{
    context.HttpContext.Response.Headers.CacheControl = "no-store";
    context.HttpContext.Response.Headers.XContentTypeOptions = "nosniff";
    return await next(context);
});

api.MapGet("/platform/info", () => Results.Ok(new
{
    name = "pwdmgr",
    product = "Privora",
    version,
    commit,
    zeroKnowledgeRequired = true
}));

api.MapAuth();
api.MapKeyring();
api.MapVaults();
api.MapSecrets();

await app.Services.InitializeDatabaseAsync();

await app.RunAsync();

// Exposed for WebApplicationFactory in the integration tests.
public partial class Program;
