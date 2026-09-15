using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace Pwdmgr.Api.Auth;

/// <summary>
/// Fixed-window limiter per client address + e-mail (lower-cased). Keyed on the account too,
/// so a distributed guesser is throttled per target and a single bad client cannot lock out
/// everybody behind the same NAT.
/// </summary>
public sealed class LoginThrottle(IOptions<AuthOptions> options) : IDisposable
{
    private readonly PartitionedRateLimiter<string> limiter = PartitionedRateLimiter.Create<string, string>(key =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = options.Value.LoginRateLimitPermits,
            Window = options.Value.LoginRateLimitWindow,
            QueueLimit = 0
        }));

    public async ValueTask<bool> TryAcquireAsync(string clientAddress, string email, CancellationToken cancellationToken)
    {
        using var lease = await limiter.AcquireAsync($"{clientAddress}|{email.ToLowerInvariant()}", 1, cancellationToken);
        return lease.IsAcquired;
    }

    public void Dispose() => limiter.Dispose();
}
