using Microsoft.AspNetCore.Diagnostics.HealthChecks;
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

builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

// Liveness runs no checks; readiness runs everything tagged "ready" (Postgres).
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(HealthTags.Ready)
});

var api = app.MapGroup("/api/v1");

api.MapGet("/platform/info", () => Results.Ok(new
{
    name = "pwdmgr",
    product = "Privora",
    status = "bootstrap",
    zeroKnowledgeRequired = true
}));

api.MapGet("/tenants/{tenantId}/vaults", (string tenantId) =>
{
    return Results.Ok(new
    {
        tenantId,
        vaults = Array.Empty<object>(),
        note = "Ciphertext-only vault API placeholder"
    });
});

await app.Services.MigrateDatabaseIfConfiguredAsync();

await app.RunAsync();
