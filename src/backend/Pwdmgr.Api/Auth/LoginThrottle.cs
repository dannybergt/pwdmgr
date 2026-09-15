using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace Pwdmgr.Api.Auth;

/// <summary>
/// Three guards in front of the Argon2 verifier, which costs ~64 MiB and ~0.5 s per call:
/// a fixed window per client address + tenant + e-mail (targeted guessing), a wider fixed
/// window per client address alone (spraying many accounts from one client), and a global
/// concurrency gate sized to the CPU count (remote memory exhaustion via parallel requests).
/// </summary>
public sealed class LoginThrottle(IOptions<AuthOptions> options) : IDisposable
{
    private readonly PartitionedRateLimiter<string> perAccount = PartitionedRateLimiter.Create<string, string>(key =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = options.Value.LoginRateLimitPermits,
            Window = options.Value.LoginRateLimitWindow,
            QueueLimit = 0
        }));

    private readonly PartitionedRateLimiter<string> perClient = PartitionedRateLimiter.Create<string, string>(key =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = options.Value.LoginRateLimitPermitsPerClient,
            Window = options.Value.LoginRateLimitWindow,
            QueueLimit = 0
        }));

    private readonly SemaphoreSlim verifierGate = new(Math.Max(1, options.Value.MaxConcurrentVerifications));

    public TimeSpan Window => options.Value.LoginRateLimitWindow;

    public async ValueTask<bool> TryAcquireAsync(string clientAddress, string tenantSlug, string email, CancellationToken cancellationToken)
    {
        using var client = await perClient.AcquireAsync(clientAddress, 1, cancellationToken);
        if (!client.IsAcquired)
        {
            return false;
        }

        using var account = await perAccount.AcquireAsync($"{clientAddress}|{tenantSlug.ToLowerInvariant()}|{email.ToLowerInvariant()}", 1, cancellationToken);
        return account.IsAcquired;
    }

    /// <summary>Non-blocking: when every slot is busy the caller answers 503 instead of queueing another 64 MiB KDF run.</summary>
    public IDisposable? TryEnterVerifierGate()
    {
        return verifierGate.Wait(0) ? new Releaser(verifierGate) : null;
    }

    public void Dispose()
    {
        perAccount.Dispose();
        perClient.Dispose();
        verifierGate.Dispose();
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}
