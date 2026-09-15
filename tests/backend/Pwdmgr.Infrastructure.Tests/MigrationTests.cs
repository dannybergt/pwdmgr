using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Pwdmgr.Infrastructure.Tests;

public sealed class MigrationTests(PostgresDatabase db) : IClassFixture<PostgresDatabase>
{
    [Fact]
    public async Task Migrate_creates_identity_tables_and_is_idempotent()
    {
        PostgresDatabase.SkipUnlessConfigured();
        await using var context = db.CreateContext();

        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        var tables = await db.ListTablesAsync();

        Assert.Equal(["__EFMigrationsHistory", "local_credentials", "secret_versions", "secrets", "sessions", "tenants", "user_keyrings", "users", "vaults", "wrapped_keys"], tables);

        // Second run must be a no-op (Constitution §9 idempotence).
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.Equal(tables, await db.ListTablesAsync());
    }

    [Fact]
    public async Task Migration_rolls_back_to_empty_schema()
    {
        PostgresDatabase.SkipUnlessConfigured();
        await using var context = db.CreateContext();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("0", TestContext.Current.CancellationToken);

        Assert.Equal(["__EFMigrationsHistory"], await db.ListTablesAsync());
        Assert.Empty(await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));

        // Forward again after rollback must succeed.
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        Assert.Contains("users", await db.ListTablesAsync());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
    }
}
