using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using WinServerMonitor.Core.Execution;

namespace WinServerMonitor.Infrastructure.Executors;

/// <summary>
/// Runs an ad-hoc SQL script. On SQL Server, batches separated by a line containing only GO are executed one by one;
/// on PostgreSQL the whole script is sent at once (multiple statements are allowed).
/// </summary>
public sealed partial class DbScriptExecutor(IOptions<MonitorOptions> options) : DbExecutorBase(options)
{
    public override string TaskType => "DbScript";

    public override string DisplayName => "SQL skript";

    public override string Description => "Spustí SQL skript (PostgreSQL, SQL Server). Parametry se v PostgreSQL zapisují jako @nazev.";

    protected override IReadOnlyList<TaskParameterDescriptor> SpecificParameters =>
    [
        new("Script", "Skript", TaskParameterKind.MultilineText, Required: true),
    ];

    public override async Task<TaskExecutionResult> ExecuteAsync(TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var script = context.GetRequiredParameter("Script");
        var parameters = ParseJsonParameters(context.GetParameter(SqlParametersParameter));

        var (connection, provider) = await OpenConnectionAsync(context, cancellationToken);
        await using (connection)
        {
            var batches = provider == DbProvider.SqlServer ? SplitBatches(script) : [script];
            var totalRows = 0;
            var totalAffected = 0;
            for (var i = 0; i < batches.Count; i++)
            {
                if (batches.Count > 1)
                {
                    context.Log.Info($"Dávka {i + 1}/{batches.Count}");
                }

                await using var command = CreateCommand(connection, context, batches[i], CommandType.Text);
                AddParameters(command, parameters, provider);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                totalRows += await ReadResultsAsync(reader, context, cancellationToken);
                totalAffected += Math.Max(0, reader.RecordsAffected);
            }

            return TaskExecutionResult.Ok($"Načteno {totalRows} řádků, ovlivněno {totalAffected} řádků");
        }
    }

    public static IReadOnlyList<string> SplitBatches(string script) =>
        GoSeparator().Split(script)
            .Select(b => b.Trim())
            .Where(b => b.Length > 0)
            .ToList();

    [GeneratedRegex(@"^\s*GO\s*;?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoSeparator();
}
