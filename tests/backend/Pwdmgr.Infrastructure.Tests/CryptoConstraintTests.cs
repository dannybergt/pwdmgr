using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pwdmgr.Domain.Crypto;
using Pwdmgr.Domain.Identity;
using Pwdmgr.Domain.Secrets;
using Pwdmgr.Domain.Sessions;
using Pwdmgr.Domain.Tenants;
using Pwdmgr.Domain.Vaults;

namespace Pwdmgr.Infrastructure.Tests;

/// <summary>Database-level guarantees behind the crypto tables: tenant-bound FKs, cascades, size checks, version uniqueness.</summary>
public sealed class CryptoConstraintTests(PostgresDatabase db) : IClassFixture<PostgresDatabase>, IAsyncLifetime
{
    private const string UniqueViolation = "23505";
    private const string ForeignKeyViolation = "23503";
    private const string CheckViolation = "23514";

    public async ValueTask InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(db.ConnectionString))
        {
            return;
        }

        await using var context = db.CreateContext();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static byte[] Bytes(int n, byte fill = 1) => Enumerable.Repeat(fill, n).ToArray();

    private static Tenant NewTenant() => new() { Id = Guid.NewGuid(), Slug = $"t-{Guid.NewGuid():N}", DisplayName = "t" };

    private static User NewUser(Tenant t) => new() { Id = Guid.NewGuid(), TenantId = t.Id, Source = UserSource.Local, Email = $"{Guid.NewGuid():N}@example.test", DisplayName = "u" };

    private static Vault NewVault(Tenant t) => new() { Id = Guid.NewGuid(), TenantId = t.Id, Type = VaultType.Personal, NameCiphertext = Bytes(40), CryptoVersion = 1 };

    private static WrappedKey NewWrapped(Tenant t, Vault v, User u) => new()
    {
        Id = Guid.NewGuid(), TenantId = t.Id, ResourceType = WrappedKeyResourceType.Vault, ResourceId = v.Id,
        RecipientType = WrappedKeyRecipientType.User, RecipientId = u.Id, KeyVersion = 1, CryptoVersion = 1, Ciphertext = Bytes(92)
    };

    private static Secret NewSecret(Tenant t, Vault v) => new() { Id = Guid.NewGuid(), TenantId = t.Id, VaultId = v.Id, Type = "password", NameCiphertext = Bytes(40) };

    private static SecretVersion NewVersion(Tenant t, Secret s, int no, User by) => new()
    {
        Id = Guid.NewGuid(), TenantId = t.Id, SecretId = s.Id, VersionNo = no, PayloadCiphertext = Bytes(100), WrappedDek = Bytes(60), AadHash = Bytes(32), CryptoVersion = 1, CreatedBy = by.Id
    };

    private static async Task<string> SqlStateOf(Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<DbUpdateException>(action);
        return Assert.IsType<PostgresException>(ex.InnerException).SqlState;
    }

    [Fact]
    public async Task Wrapped_key_cannot_point_at_a_vault_or_user_of_another_tenant()
    {
        PostgresDatabase.SkipUnlessConfigured();
        var a = NewTenant();
        var b = NewTenant();
        var userA = NewUser(a);
        var userB = NewUser(b);
        var vaultA = NewVault(a);
        await using var context = db.CreateContext();
        context.AddRange(a, b, userA, userB, vaultA);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.WrappedKeys.Add(NewWrapped(b, vaultA, userB));
        Assert.Equal(ForeignKeyViolation, await SqlStateOf(() => context.SaveChangesAsync(TestContext.Current.CancellationToken)));
        context.ChangeTracker.Clear();

        var crossUser = NewWrapped(a, vaultA, userB);
        context.WrappedKeys.Add(crossUser);
        Assert.Equal(ForeignKeyViolation, await SqlStateOf(() => context.SaveChangesAsync(TestContext.Current.CancellationToken)));
        context.ChangeTracker.Clear();

        context.WrappedKeys.Add(NewWrapped(a, vaultA, userA));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Deleting_a_user_cascades_sessions_keyring_and_wrapped_keys()
    {
        PostgresDatabase.SkipUnlessConfigured();
        var t = NewTenant();
        var u = NewUser(t);
        var v = NewVault(t);
        await using var context = db.CreateContext(t.Id);
        context.AddRange(t, u, v, NewWrapped(t, v, u));
        context.Sessions.Add(new Session { Id = Guid.NewGuid(), TenantId = t.Id, UserId = u.Id, TokenHash = Bytes(32), ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), LastSeenAt = DateTimeOffset.UtcNow });
        context.UserKeyrings.Add(new UserKeyring
        {
            Id = Guid.NewGuid(), TenantId = t.Id, UserId = u.Id, CryptoVersion = 1, KdfAlgorithm = "argon2id", KdfMemoryKib = 65536, KdfIterations = 3, KdfParallelism = 4,
            KdfSalt = Bytes(16), PublicKey = Bytes(32), EncryptedPrivateKey = Bytes(76)
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await context.Users.Where(x => x.Id == u.Id).ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        Assert.False(await context.Sessions.AnyAsync(s => s.UserId == u.Id, TestContext.Current.CancellationToken));
        Assert.False(await context.UserKeyrings.AnyAsync(k => k.UserId == u.Id, TestContext.Current.CancellationToken));
        Assert.False(await context.WrappedKeys.AnyAsync(w => w.RecipientId == u.Id, TestContext.Current.CancellationToken));
        Assert.True(await context.Vaults.AnyAsync(x => x.Id == v.Id, TestContext.Current.CancellationToken), "the vault itself survives");
    }

    [Fact]
    public async Task Vault_with_secrets_cannot_be_deleted_but_secret_delete_cascades_versions()
    {
        PostgresDatabase.SkipUnlessConfigured();
        var t = NewTenant();
        var u = NewUser(t);
        var v = NewVault(t);
        var s = NewSecret(t, v);
        await using var context = db.CreateContext(t.Id);
        context.AddRange(t, u, v, s, NewVersion(t, s, 1, u), NewVersion(t, s, 2, u));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // ExecuteDelete bypasses the change tracker, so the raw Postgres error surfaces directly.
        var restrict = await Assert.ThrowsAsync<PostgresException>(() => context.Vaults.Where(x => x.Id == v.Id).ExecuteDeleteAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ForeignKeyViolation, restrict.SqlState);

        await context.Secrets.Where(x => x.Id == s.Id).ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, await context.SecretVersions.CountAsync(x => x.SecretId == s.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Version_numbers_are_unique_per_secret_and_cross_tenant_secret_is_rejected()
    {
        PostgresDatabase.SkipUnlessConfigured();
        var a = NewTenant();
        var b = NewTenant();
        var u = NewUser(a);
        var v = NewVault(a);
        var s = NewSecret(a, v);
        await using var context = db.CreateContext();
        context.AddRange(a, b, u, v, s, NewVersion(a, s, 1, u));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.SecretVersions.Add(NewVersion(a, s, 1, u));
        Assert.Equal(UniqueViolation, await SqlStateOf(() => context.SaveChangesAsync(TestContext.Current.CancellationToken)));
        context.ChangeTracker.Clear();

        context.Secrets.Add(NewSecret(b, v));
        Assert.Equal(ForeignKeyViolation, await SqlStateOf(() => context.SaveChangesAsync(TestContext.Current.CancellationToken)));
    }

    [Theory]
    [InlineData("payload", 64 * 1024 + 1)]
    [InlineData("payload", 27)]
    [InlineData("aad", 31)]
    [InlineData("dek", 257)]
    public async Task Size_checks_reject_oversized_or_truncated_blobs(string field, int length)
    {
        PostgresDatabase.SkipUnlessConfigured();
        var t = NewTenant();
        var u = NewUser(t);
        var v = NewVault(t);
        var s = NewSecret(t, v);
        await using var context = db.CreateContext();
        context.AddRange(t, u, v, s);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var version = NewVersion(t, s, 1, u);
        version = field switch
        {
            "payload" => new SecretVersion { Id = version.Id, TenantId = t.Id, SecretId = s.Id, VersionNo = 1, PayloadCiphertext = Bytes(length), WrappedDek = version.WrappedDek, AadHash = version.AadHash, CryptoVersion = 1, CreatedBy = u.Id },
            "aad" => new SecretVersion { Id = version.Id, TenantId = t.Id, SecretId = s.Id, VersionNo = 1, PayloadCiphertext = version.PayloadCiphertext, WrappedDek = version.WrappedDek, AadHash = Bytes(length), CryptoVersion = 1, CreatedBy = u.Id },
            _ => new SecretVersion { Id = version.Id, TenantId = t.Id, SecretId = s.Id, VersionNo = 1, PayloadCiphertext = version.PayloadCiphertext, WrappedDek = Bytes(length), AadHash = version.AadHash, CryptoVersion = 1, CreatedBy = u.Id }
        };
        context.SecretVersions.Add(version);
        Assert.Equal(CheckViolation, await SqlStateOf(() => context.SaveChangesAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Keyring_kdf_parameters_below_the_floor_are_rejected_by_the_database()
    {
        PostgresDatabase.SkipUnlessConfigured();
        var t = NewTenant();
        var u = NewUser(t);
        await using var context = db.CreateContext();
        context.AddRange(t, u);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.UserKeyrings.Add(new UserKeyring
        {
            Id = Guid.NewGuid(), TenantId = t.Id, UserId = u.Id, CryptoVersion = 1, KdfAlgorithm = "argon2id", KdfMemoryKib = 1024, KdfIterations = 3, KdfParallelism = 4,
            KdfSalt = Bytes(16), PublicKey = Bytes(32), EncryptedPrivateKey = Bytes(76)
        });
        Assert.Equal(CheckViolation, await SqlStateOf(() => context.SaveChangesAsync(TestContext.Current.CancellationToken)));
    }
}
