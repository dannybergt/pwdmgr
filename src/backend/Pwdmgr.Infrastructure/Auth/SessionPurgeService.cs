using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pwdmgr.Infrastructure.Persistence;

namespace Pwdmgr.Infrastructure.Auth;

/// <summary>Deletes sessions that expired or were revoked more than <see cref="Retention"/> ago, hourly. Keeps the table bounded; rows younger than that stay for audit.</summary>
public sealed class SessionPurgeService(IServiceScopeFactory scopes, TimeProvider clock, ILogger<SessionPurgeService> logger) : BackgroundService
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await PurgeOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Session purge failed; will retry next interval");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task<int> PurgeOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PwdmgrDbContext>();
        var cutoff = clock.GetUtcNow() - Retention;
        var deleted = await db.Sessions.IgnoreQueryFilters()
            .Where(s => s.ExpiresAt < cutoff || (s.RevokedAt != null && s.RevokedAt < cutoff))
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted > 0)
        {
            logger.LogInformation("Purged {Count} sessions older than {Retention}", deleted, Retention);
        }

        return deleted;
    }
}
