using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Pwdmgr.Application.Auth;
using Pwdmgr.Domain.Identity;
using Pwdmgr.Domain.Sessions;
using Pwdmgr.Domain.Tenants;
using Pwdmgr.Infrastructure.Persistence;

namespace Pwdmgr.Infrastructure.Auth;

public sealed record LoginResult(Guid TenantId, Guid UserId, Guid SessionId, string Token);

public sealed record SessionPrincipal(Guid TenantId, Guid UserId, Guid SessionId);

/// <summary>
/// Local login and server-side session records. Runs the Argon2 verifier for unknown users
/// and disabled accounts too, so the response latency does not reveal whether an account
/// exists.
/// </summary>
public sealed class SessionService(PwdmgrDbContext db, IPasswordHasher hasher, TimeProvider clock)
{
    private const int TokenLength = 32;
    private static readonly TimeSpan LastSeenGranularity = TimeSpan.FromMinutes(1);

    public async Task<LoginResult?> LoginAsync(string tenantSlug, string email, string password, TimeSpan ttl, CancellationToken cancellationToken)
    {
        // No tenant context yet: bypass the tenant query filter for this single lookup.
        var candidate = await db.Users.IgnoreQueryFilters()
            .Where(u => u.Email == email && u.Source == UserSource.Local)
            .Join(db.Tenants.Where(t => t.Slug == tenantSlug && t.Status == TenantStatus.Active), u => u.TenantId, t => t.Id, (u, t) => u)
            .Join(db.LocalCredentials.IgnoreQueryFilters(), u => u.Id, c => c.UserId, (u, c) => new { User = u, Credential = c })
            .SingleOrDefaultAsync(cancellationToken);

        var storedHash = candidate?.Credential.PasswordHash ?? hasher.DecoyHash;
        var verified = hasher.Verify(storedHash, password);
        if (candidate is null || !verified || candidate.User.Status != UserStatus.Active)
        {
            return null;
        }

        var token = RandomNumberGenerator.GetBytes(TokenLength);
        var now = clock.GetUtcNow();
        var session = new Session
        {
            Id = Guid.NewGuid(),
            TenantId = candidate.User.TenantId,
            UserId = candidate.User.Id,
            TokenHash = SHA256.HashData(token),
            CreatedAt = now,
            ExpiresAt = now + ttl,
            LastSeenAt = now
        };
        db.Sessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        return new LoginResult(session.TenantId, session.UserId, session.Id, Convert.ToBase64String(token));
    }

    public async Task<SessionPrincipal?> ResolveAsync(string token, CancellationToken cancellationToken)
    {
        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(token);
        }
        catch (FormatException)
        {
            return null;
        }

        if (raw.Length != TokenLength)
        {
            return null;
        }

        var hash = SHA256.HashData(raw);
        var now = clock.GetUtcNow();
        // Revocation must be immediate: a disabled user or tenant invalidates every session
        // on its next request, not only at the next login.
        var session = await db.Sessions.IgnoreQueryFilters()
            .Where(s => s.TokenHash == hash)
            .Join(db.Users.IgnoreQueryFilters().Where(u => u.Status == UserStatus.Active), s => s.UserId, u => u.Id, (s, _) => s)
            .Join(db.Tenants.Where(t => t.Status == TenantStatus.Active), s => s.TenantId, t => t.Id, (s, _) => s)
            .SingleOrDefaultAsync(cancellationToken);
        if (session is null || !session.IsActive(now))
        {
            return null;
        }

        // Coarse last-seen: one write per minute per session instead of one per request.
        if (now - session.LastSeenAt >= LastSeenGranularity)
        {
            session.LastSeenAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        return new SessionPrincipal(session.TenantId, session.UserId, session.Id);
    }

    public async Task RevokeAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await db.Sessions
            .Where(s => s.Id == sessionId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), cancellationToken);
    }
}
