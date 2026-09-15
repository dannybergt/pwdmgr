using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pwdmgr.Infrastructure.Persistence;

namespace Pwdmgr.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPwdmgrInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");

        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));

        services.AddDbContext<PwdmgrDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());

        services.AddHealthChecks()
            .AddDbContextCheck<PwdmgrDbContext>("postgres", tags: [HealthTags.Ready]);

        return services;
    }

    /// <summary>Applies pending migrations when <see cref="DatabaseOptions.MigrateOnStartup"/> is set. Idempotent.</summary>
    public static async Task MigrateDatabaseIfConfiguredAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>().Value;
        if (!options.MigrateOnStartup)
        {
            return;
        }

        var dbContext = scope.ServiceProvider.GetRequiredService<PwdmgrDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
