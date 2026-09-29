using System.Data;
using Microsoft.Extensions.Options;
using WinServerMonitor.Core.Execution;

namespace WinServerMonitor.Infrastructure.Executors;

/// <summary>
/// Calls a stored procedure or function.
/// PostgreSQL: <c>CALL proc(p => @p)</c> for procedures, <c>SELECT * FROM func(p => @p)</c> for functions.
/// SQL Server: <c>EXEC proc @p</c> with the return value captured.
/// </summary>
public sealed class DbProcedureExecutor(IOptions<MonitorOptions> options) : DbExecutorBase(options)
{
    public const string ModeProcedure = "Procedure";
    public const string ModeFunction = "Function";

    public override string TaskType => "DbProcedure";

    public override string DisplayName => "DB procedura / funkce";

    public override string Description =>
        "Zavolá uloženou proceduru nebo funkci (PostgreSQL, SQL Server). RAISE NOTICE / PRINT a výsledky jdou do logu.";

    protected override IReadOnlyList<TaskParameterDescriptor> SpecificParameters =>
    [
        new("Procedure", "Procedura / funkce", Required: true, Help: "Např. public.daily_close nebo dbo.usp_DailyClose"),
        new("Mode", "Typ objektu", TaskParameterKind.Choice, DefaultValue: ModeProcedure, Choices: [ModeProcedure, ModeFunction],
            Help: "PostgreSQL: Procedure = CALL, Function = SELECT * FROM. SQL Server: vždy EXEC."),
        new("FailOnNonZeroReturn", "Chyba při návratové hodnotě ≠ 0 (SQL Server)", TaskParameterKind.Boolean, DefaultValue: "false"),
    ];

    public override async Task<TaskExecutionResult> ExecuteAsync(TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var procedure = context.GetRequiredParameter("Procedure");
        if (!IsValidObjectName(procedure))
        {
            throw new TaskConfigurationException($"Neplatný název procedury '{procedure}'.");
        }

        var mode = context.GetParameter("Mode") ?? ModeProcedure;
        var parameters = ParseJsonParameters(context.GetParameter(SqlParametersParameter));

        var (connection, provider) = await OpenConnectionAsync(context, cancellationToken);
        await using (connection)
        {
            return provider == DbProvider.SqlServer
                ? await ExecuteSqlServerAsync(connection, context, procedure, parameters, cancellationToken)
                : await ExecutePostgresAsync(connection, context, procedure, mode, parameters, cancellationToken);
        }
    }

    public static string BuildPostgresCommand(string procedure, string mode, IEnumerable<string> parameterNames)
    {
        var arguments = string.Join(", ", parameterNames.Select(p => $"{p} => @{p}"));
        return mode.Equals(ModeFunction, StringComparison.OrdinalIgnoreCase)
            ? $"SELECT * FROM {procedure}({arguments})"
            : $"CALL {procedure}({arguments})";
    }

    private static async Task<TaskExecutionResult> ExecutePostgresAsync(
        System.Data.Common.DbConnection connection,
        TaskExecutionContext context,
        string procedure,
        string mode,
        IReadOnlyList<KeyValuePair<string, object>> parameters,
        CancellationToken cancellationToken)
    {
        var text = BuildPostgresCommand(procedure, mode, parameters.Select(p => p.Key));
        await using var command = CreateCommand(connection, context, text, CommandType.Text);
        AddParameters(command, parameters, DbProvider.PostgreSql);

        context.Log.Info(text);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = await ReadResultsAsync(reader, context, cancellationToken);
        return TaskExecutionResult.Ok($"Načteno {rows} řádků");
    }

    private static async Task<TaskExecutionResult> ExecuteSqlServerAsync(
        System.Data.Common.DbConnection connection,
        TaskExecutionContext context,
        string procedure,
        IReadOnlyList<KeyValuePair<string, object>> parameters,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, context, procedure, CommandType.StoredProcedure);
        AddParameters(command, parameters, DbProvider.SqlServer);
        var returnValue = command.CreateParameter();
        returnValue.ParameterName = "@RETURN_VALUE";
        returnValue.DbType = DbType.Int32;
        returnValue.Direction = ParameterDirection.ReturnValue;
        command.Parameters.Add(returnValue);

        context.Log.Info($"EXEC {procedure} ({parameters.Count} parametrů)");
        int rows;
        int affected;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            rows = await ReadResultsAsync(reader, context, cancellationToken);
            affected = Math.Max(0, reader.RecordsAffected);
        }

        var code = returnValue.Value is int value ? value : 0;
        var summary = $"Návratová hodnota {code}, načteno {rows} řádků, ovlivněno {affected} řádků";
        return code != 0 && context.GetBool("FailOnNonZeroReturn")
            ? TaskExecutionResult.Fail(summary)
            : TaskExecutionResult.Ok(summary);
    }

    /// <summary>Allows schema-qualified identifiers, optionally quoted ("x" or [x]); rejects anything else to prevent injection.</summary>
    public static bool IsValidObjectName(string name) =>
        name.Split('.').All(part =>
            part.Length > 0 &&
            (part.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '$') ||
             (part.Length > 2 && part[0] == '"' && part[^1] == '"' && !part[1..^1].Contains('"')) ||
             (part.Length > 2 && part[0] == '[' && part[^1] == ']' && !part[1..^1].Contains(']'))));
}
