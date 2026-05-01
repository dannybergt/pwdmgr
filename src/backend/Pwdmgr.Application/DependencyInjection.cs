using Microsoft.Extensions.DependencyInjection;

namespace Pwdmgr.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddPwdmgrApplication(this IServiceCollection services)
    {
        return services;
    }
}

