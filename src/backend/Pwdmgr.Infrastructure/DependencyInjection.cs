using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Pwdmgr.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPwdmgrInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        _ = configuration;
        return services;
    }
}

