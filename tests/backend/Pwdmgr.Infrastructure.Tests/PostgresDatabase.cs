using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pwdmgr.Application.Auth;
using Pwdmgr.Infrastructure.Persistence;

namespace Pwdmgr.Infrastructure.Tests;

/// <summary>
/// One throw-away database per test class, created from the admin connection in
/// <c>PWDMGR_TEST_PG</c> (see TESTING.md). Tests are skipped when the variable is unset.
/// </summary>
public sealed class PostgresDatabase : IAsyncLifetime
{
    private const string EnvVar = "PWDMGR_TEST_PG";

    private string? adminConnectionString;
    private string? databaseName;

    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Skips locally when no test database is configured; in CI (<c>CI=true</c>) a missing variable is a failure, not a silent pass.</summary>
    public static void SkipUnlessConfigured()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvVar)))
        {
            return;
        }

        if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail($"{EnvVar} must be set in CI; database tests would otherwise be skipped silently.");
        }

        Assert.Skip($"{EnvVar} is not set; see TESTING.md for the local Postgres container.");
    }

    public async ValueTask InitializeAsync()
    {
        adminConnectionString = Environment.GetEnvironmentVariable(EnvVar);
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        databaseName = $"pwdmgr_test_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(adminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        ConnectionString = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = databaseName }.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        if (adminConnectionString is null || databaseName is null)
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    /// <summary>Context scoped to <paramref name="tenantId"/> (tenant query filter active); without one nothing tenant-scoped is visible.</summary>
    public PwdmgrDbContext CreateContext(Guid? tenantId = null)
    {
        var options = new DbContextOptionsBuilder<PwdmgrDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        var request = new RequestContext();
        if (tenantId is { } id)
        {
            request.Authenticate(id, Guid.Empty, Guid.Empty);
        }

        return new PwdmgrDbContext(options, request);
    }

    public async Task<IReadOnlyList<string>> ListTablesAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        var tables = new List<string>();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        // Ordinal sort in C#, independent of the database collation.
        return tables.Order(StringComparer.Ordinal).ToList();
    }
}
