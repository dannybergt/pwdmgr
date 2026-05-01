using Pwdmgr.Application;
using Pwdmgr.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPwdmgrApplication();
builder.Services.AddPwdmgrInfrastructure(builder.Configuration);

builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

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

app.Run();

