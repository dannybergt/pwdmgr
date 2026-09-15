using System.Reflection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Pwdmgr.Api.Auth;
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

builder.Services.AddProblemDetails();

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

var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
var commit = Environment.GetEnvironmentVariable("PWDMGR_COMMIT") ?? "unknown";

var api = app.MapGroup("/api/v1");

api.MapGet("/platform/info", () => Results.Ok(new
{
    name = "pwdmgr",
    product = "Privora",
    version,
    commit,
    zeroKnowledgeRequired = true
}));

api.MapAuth();

await app.Services.InitializeDatabaseAsync();

await app.RunAsync();

// Exposed for WebApplicationFactory in the integration tests.
public partial class Program;
