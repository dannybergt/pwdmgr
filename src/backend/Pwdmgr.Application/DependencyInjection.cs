using Microsoft.Extensions.DependencyInjection;
using Pwdmgr.Application.Auth;

namespace Pwdmgr.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddPwdmgrApplication(this IServiceCollection services)
    {
        services.AddScoped<RequestContext>();
        services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<RequestContext>());
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<RequestContext>());
        return services;
    }
}
