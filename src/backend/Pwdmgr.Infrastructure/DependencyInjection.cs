using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Pwdmgr.Application.Auth;
using Pwdmgr.Infrastructure.Auth;
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
        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));

        services.AddDbContext<PwdmgrDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());

        services.AddHealthChecks()
            .AddDbContextCheck<PwdmgrDbContext>("postgres", tags: [HealthTags.Ready]);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddScoped<SessionService>();
        services.AddScoped<DevelopmentSeeder>();
        services.AddHostedService<SessionPurgeService>();

        return services;
    }

    /// <summary>Applies pending migrations when <see cref="DatabaseOptions.MigrateOnStartup"/> is set, then the dev seed when enabled in Development. Idempotent.</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        if (options.MigrateOnStartup)
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PwdmgrDbContext>();
            await dbContext.Database.MigrateAsync(cancellationToken);
        }

        var seed = scope.ServiceProvider.GetRequiredService<IOptions<SeedOptions>>().Value;
        var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        if (seed.Enabled && environment.IsDevelopment())
        {
            await scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync(cancellationToken);
        }
    }
}
