using Microsoft.Extensions.Options;
using Npgsql;
using WinServerMonitor.Core.Domain;
using WinServerMonitor.Core.Execution;
using WinServerMonitor.Infrastructure;
using WinServerMonitor.Infrastructure.Executors;
using WinServerMonitor.Tests.Support;

namespace WinServerMonitor.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class PostgresExecutorTests(PostgresFixture fixture) : IAsyncLifetime
{
    private IOptions<MonitorOptions> _options = default!;

    public async Task InitializeAsync()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.UnavailableReason);
        _options = Options.Create(new MonitorOptions
        {
            Connections = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Test"] = new DbConnectionOptions { Provider = DbProvider.PostgreSql, ConnectionString = fixture.ConnectionString },
            },
        });

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            CREATE OR REPLACE PROCEDURE public.wsm_test_proc(p_count integer, p_label text)
            LANGUAGE plpgsql AS $$
            BEGIN
                RAISE NOTICE 'processing % items for %', p_count, p_label;
            END $$;

            CREATE OR REPLACE FUNCTION public.wsm_test_fn(p_n integer)
            RETURNS TABLE(n integer) LANGUAGE sql AS $$ SELECT generate_series(1, p_n) $$;

            CREATE OR REPLACE PROCEDURE public.wsm_test_fail()
            LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'business rule violated'; END $$;
            """,
            connection);
        await command.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static (TaskExecutionContext Context, CollectingLogger Log) Context(Dictionary<string, string> parameters)
    {
        var log = new CollectingLogger();
        var definition = new TaskDefinition { Name = "pg", TaskType = "DbProcedure" };
        return (new TaskExecutionContext(definition, new TaskRun(), parameters, log), log);
    }

    [SkippableFact]
    public async Task Procedure_IsCalled_WithNamedParameters_AndNoticesAreLogged()
    {
        var (context, log) = Context(new()
        {
            ["Connection"] = "Test",
            ["Procedure"] = "public.wsm_test_proc",
            ["SqlParameters"] = """{"p_count": 5, "p_label": "sklad"}""",
        });

        var result = await new DbProcedureExecutor(_options).ExecuteAsync(context, default);

        Assert.True(result.Success);
        Assert.Contains(log.Entries, e => e.Message.Contains("processing 5 items for sklad"));
    }

    [SkippableFact]
    public async Task Function_ResultRowsAreRead()
    {
        var (context, log) = Context(new()
        {
            ["Connection"] = "Test",
            ["Procedure"] = "public.wsm_test_fn",
            ["Mode"] = DbProcedureExecutor.ModeFunction,
            ["SqlParameters"] = """{"p_n": 3}""",
            ["LogRows"] = "2",
        });

        var result = await new DbProcedureExecutor(_options).ExecuteAsync(context, default);

        Assert.True(result.Success);
        Assert.Equal("Načteno 3 řádků", result.Summary);
        Assert.Equal(2, log.Entries.Count(e => e.Level == TaskLogLevel.Debug && e.Message.StartsWith("n=")));
    }

    [SkippableFact]
    public async Task Procedure_RaisingException_Throws()
    {
        var (context, _) = Context(new() { ["Connection"] = "Test", ["Procedure"] = "public.wsm_test_fail" });

        var ex = await Assert.ThrowsAsync<PostgresException>(() => new DbProcedureExecutor(_options).ExecuteAsync(context, default));
        Assert.Contains("business rule violated", ex.MessageText);
    }

    [SkippableFact]
    public async Task Script_RunsMultipleStatements_WithParameters()
    {
        var (context, _) = Context(new()
        {
            ["Connection"] = "Test",
            ["Script"] = """
                CREATE TEMP TABLE t(v integer);
                INSERT INTO t SELECT generate_series(1, @count);
                SELECT count(*) AS c FROM t;
                """,
            ["SqlParameters"] = """{"count": 4}""",
        });

        var result = await new DbScriptExecutor(_options).ExecuteAsync(context, default);

        Assert.True(result.Success);
        Assert.Contains("Načteno 1 řádků", result.Summary);
    }

    [SkippableFact]
    public async Task UnknownConnection_IsConfigurationError()
    {
        var (context, _) = Context(new() { ["Connection"] = "Missing", ["Procedure"] = "public.wsm_test_proc" });

        await Assert.ThrowsAsync<TaskConfigurationException>(() => new DbProcedureExecutor(_options).ExecuteAsync(context, default));
    }
}
