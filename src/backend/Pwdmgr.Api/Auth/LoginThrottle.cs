using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace Pwdmgr.Api.Auth;

/// <summary>
/// Four guards in front of the Argon2 verifier, which costs ~64 MiB and ~0.5 s per call:
/// a fixed window per client address + tenant + e-mail (targeted guessing from one place),
/// a wider fixed window per client address alone (spraying many accounts from one client),
/// a sliding window of <em>failed</em> attempts per tenant + e-mail regardless of client
/// (distributed guessing against one account; counting failures only means a legitimate
/// user cannot be locked out by attempts that never touch the verifier), and a global
/// concurrency gate sized to the CPU count (remote memory exhaustion via parallel requests).
/// </summary>
public sealed class LoginThrottle(IOptions<AuthOptions> options) : IDisposable
{
    private readonly PartitionedRateLimiter<string> perAccountPerClient = PartitionedRateLimiter.Create<string, string>(key =>
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

    private readonly PartitionedRateLimiter<string> failuresPerAccount = PartitionedRateLimiter.Create<string, string>(key =>
        RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = options.Value.LoginFailuresPerAccount,
            Window = options.Value.LoginFailureWindow,
            SegmentsPerWindow = 15,
            QueueLimit = 0
        }));

    private readonly SemaphoreSlim verifierGate = new(Math.Max(1, options.Value.MaxConcurrentVerifications));

    public TimeSpan Window => options.Value.LoginRateLimitWindow;

    public TimeSpan FailureWindow => options.Value.LoginFailureWindow;

    private static string AccountKey(string tenantSlug, string email) => $"{tenantSlug.ToLowerInvariant()}|{email.ToLowerInvariant()}";

    public async ValueTask<bool> TryAcquireAsync(string clientAddress, string tenantSlug, string email, CancellationToken cancellationToken)
    {
        using var client = await perClient.AcquireAsync(clientAddress, 1, cancellationToken);
        if (!client.IsAcquired)
        {
            return false;
        }

        using var account = await perAccountPerClient.AcquireAsync($"{clientAddress}|{AccountKey(tenantSlug, email)}", 1, cancellationToken);
        return account.IsAcquired;
    }

    /// <summary>True while the account is under its failure budget; a probe with zero permits, so success paths do not consume it.</summary>
    public bool IsAccountOpen(string tenantSlug, string email)
    {
        using var probe = failuresPerAccount.AttemptAcquire(AccountKey(tenantSlug, email), 0);
        return probe.IsAcquired;
    }

    /// <summary>Records one failed attempt against the account; returns false when the budget is now exhausted.</summary>
    public async ValueTask<bool> RecordFailureAsync(string tenantSlug, string email, CancellationToken cancellationToken)
    {
        using var lease = await failuresPerAccount.AcquireAsync(AccountKey(tenantSlug, email), 1, cancellationToken);
        return lease.IsAcquired;
    }

    /// <summary>Non-blocking: when every slot is busy the caller answers 503 instead of queueing another 64 MiB KDF run.</summary>
    public IDisposable? TryEnterVerifierGate()
    {
        return verifierGate.Wait(0) ? new Releaser(verifierGate) : null;
    }

    public void Dispose()
    {
        perAccountPerClient.Dispose();
        perClient.Dispose();
        failuresPerAccount.Dispose();
        verifierGate.Dispose();
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}
