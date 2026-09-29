using Microsoft.EntityFrameworkCore;
using Npgsql;
using WinServerMonitor.Infrastructure.Persistence;

namespace WinServerMonitor.Tests.Support;

/// <summary>
/// Creates a throw-away PostgreSQL database for integration tests.
/// Server is taken from the WSM_TEST_POSTGRES environment variable
/// (default: Host=localhost;Username=postgres;Password=postgres). Tests are skipped when it is not reachable.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly string _serverConnectionString =
        Environment.GetEnvironmentVariable("WSM_TEST_POSTGRES") ?? "Host=localhost;Username=postgres;Password=postgres";

    private readonly string _databaseName = "wsm_test_" + Guid.NewGuid().ToString("N")[..12];

    public bool IsAvailable { get; private set; }

    public string? UnavailableReason { get; private set; }

    public string ConnectionString { get; private set; } = string.Empty;

    public IDbContextFactory<MonitorDbContext> DbFactory { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        try
        {
            await using (var connection = new NpgsqlConnection(_serverConnectionString))
            {
                await connection.OpenAsync();
                await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", connection);
                await create.ExecuteNonQueryAsync();
            }

            ConnectionString = new NpgsqlConnectionStringBuilder(_serverConnectionString) { Database = _databaseName }.ConnectionString;
            var options = new DbContextOptionsBuilder<MonitorDbContext>()
                .UseNpgsql(ConnectionString, o => o.MigrationsHistoryTable("__EFMigrationsHistory", MonitorDbContext.Schema))
                .Options;
            DbFactory = new TestDbContextFactory(options);

            await using var db = await DbFactory.CreateDbContextAsync();
            await db.Database.MigrateAsync();
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            UnavailableReason = $"PostgreSQL is not available ({ex.Message}). Set WSM_TEST_POSTGRES to run integration tests.";
        }
    }

    public async Task ResetAsync()
    {
        await using var db = await DbFactory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync(
            $"TRUNCATE {MonitorDbContext.Schema}.\"TaskLogs\", {MonitorDbContext.Schema}.\"TaskRuns\", {MonitorDbContext.Schema}.\"TaskDefinitions\" RESTART IDENTITY CASCADE");
    }

    public async Task DisposeAsync()
    {
        if (!IsAvailable)
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(_serverConnectionString);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }

    private sealed class TestDbContextFactory(DbContextOptions<MonitorDbContext> options) : IDbContextFactory<MonitorDbContext>
    {
        public MonitorDbContext CreateDbContext() => new(options);
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
