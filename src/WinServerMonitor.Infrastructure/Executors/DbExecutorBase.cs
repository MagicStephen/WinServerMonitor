using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Npgsql;
using WinServerMonitor.Core.Execution;

namespace WinServerMonitor.Infrastructure.Executors;

/// <summary>
/// Shared plumbing for database tasks (PostgreSQL and SQL Server): named connections,
/// capture of RAISE NOTICE / PRINT messages and logging of result sets.
/// </summary>
public abstract class DbExecutorBase(IOptions<MonitorOptions> options) : ITaskExecutor
{
    protected const string ConnectionParameter = "Connection";
    protected const string CommandTimeoutParameter = "CommandTimeoutSeconds";
    protected const string LogRowsParameter = "LogRows";
    protected const string SqlParametersParameter = "SqlParameters";

    public abstract string TaskType { get; }

    public abstract string DisplayName { get; }

    public abstract string Description { get; }

    public IReadOnlyList<TaskParameterDescriptor> Parameters =>
    [
        new(ConnectionParameter, "Připojení", TaskParameterKind.Choice, Required: true,
            Help: "Název připojení z konfigurace WinServerMonitor:Connections.",
            Choices: options.Value.Connections.Keys.Order().ToList()),
        .. SpecificParameters,
        new(SqlParametersParameter, "Parametry (JSON)", TaskParameterKind.MultilineText,
            Help: "JSON objekt, např. {\"p_datum\": \"2024-01-31\", \"p_limit\": 100}"),
        new(CommandTimeoutParameter, "Timeout příkazu (s)", TaskParameterKind.Number, DefaultValue: "0",
            Help: "0 = bez limitu (platí timeout tasku)."),
        new(LogRowsParameter, "Logovat řádků výsledku", TaskParameterKind.Number, DefaultValue: "10",
            Help: "Kolik prvních řádků z každé výsledkové sady zapsat do logu."),
    ];

    protected abstract IReadOnlyList<TaskParameterDescriptor> SpecificParameters { get; }

    public abstract Task<TaskExecutionResult> ExecuteAsync(TaskExecutionContext context, CancellationToken cancellationToken);

    protected async Task<(DbConnection Connection, DbProvider Provider)> OpenConnectionAsync(
        TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var name = context.GetRequiredParameter(ConnectionParameter);
        if (!options.Value.Connections.TryGetValue(name, out var settings) || string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            throw new TaskConfigurationException($"Připojení '{name}' není definováno v konfiguraci (WinServerMonitor:Connections).");
        }

        DbConnection connection;
        string target;
        switch (settings.Provider)
        {
            case DbProvider.PostgreSql:
                var pg = new NpgsqlConnection(settings.ConnectionString);
                pg.Notice += (_, e) => context.Log.Info($"[{e.Notice.Severity}] {e.Notice.MessageText}");
                var pgBuilder = new NpgsqlConnectionStringBuilder(settings.ConnectionString);
                target = $"{pgBuilder.Host} / {pgBuilder.Database}";
                connection = pg;
                break;

            case DbProvider.SqlServer:
                var sql = new SqlConnection(settings.ConnectionString);
                sql.InfoMessage += (_, e) =>
                {
                    foreach (SqlError error in e.Errors)
                    {
                        context.Log.Info($"[SQL] {error.Message}");
                    }
                };
                var sqlBuilder = new SqlConnectionStringBuilder(settings.ConnectionString);
                target = $"{sqlBuilder.DataSource} / {sqlBuilder.InitialCatalog}";
                connection = sql;
                break;

            default:
                throw new TaskConfigurationException($"Nepodporovaný provider '{settings.Provider}'.");
        }

        context.Log.Info($"Připojuji k {target} ({name}, {settings.Provider})");
        try
        {
            await connection.OpenAsync(cancellationToken);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        return (connection, settings.Provider);
    }

    protected static DbCommand CreateCommand(DbConnection connection, TaskExecutionContext context, string text, CommandType type)
    {
        var command = connection.CreateCommand();
        command.CommandText = text;
        command.CommandType = type;
        command.CommandTimeout = Math.Max(0, context.GetInt(CommandTimeoutParameter, 0));
        return command;
    }

    /// <summary>Reads all result sets, logging row counts and the first rows. Returns total rows read.</summary>
    protected static async Task<int> ReadResultsAsync(DbDataReader reader, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var logRows = Math.Max(0, context.GetInt(LogRowsParameter, 10));
        var total = 0;
        var resultSet = 0;
        do
        {
            if (reader.FieldCount == 0)
            {
                continue;
            }

            resultSet++;
            var rows = 0;
            while (await reader.ReadAsync(cancellationToken))
            {
                rows++;
                if (rows <= logRows)
                {
                    context.Log.Debug(FormatRow(reader));
                }
            }

            total += rows;
            context.Log.Info($"Výsledková sada {resultSet}: {rows} řádků");
        }
        while (await reader.NextResultAsync(cancellationToken));

        return total;
    }

    private static string FormatRow(DbDataReader reader)
    {
        var line = new StringBuilder();
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (i > 0)
            {
                line.Append(" | ");
            }

            line.Append(reader.GetName(i)).Append('=').Append(reader.IsDBNull(i) ? "NULL" : Convert.ToString(reader.GetValue(i)));
        }

        return line.ToString();
    }

    /// <summary>
    /// Parses a JSON object like {"p_from": "2024-01-01", "p_limit": 10, "p_flag": null} into
    /// parameter name/value pairs. A leading '@' or ':' in names is removed.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, object>> ParseJsonParameters(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new TaskConfigurationException($"Parametry nejsou platný JSON: {ex.Message}");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new TaskConfigurationException("Parametry musí být JSON objekt, např. {\"p_datum\": \"2024-01-01\"}.");
            }

            var result = new List<KeyValuePair<string, object>>();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                var name = property.Name.TrimStart('@', ':');
                if (name.Length == 0 || !name.All(c => char.IsLetterOrDigit(c) || c == '_'))
                {
                    throw new TaskConfigurationException($"Neplatný název parametru '{property.Name}'.");
                }

                object value = property.Value.ValueKind switch
                {
                    JsonValueKind.Null or JsonValueKind.Undefined => DBNull.Value,
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Number when property.Value.TryGetInt64(out var l) => l,
                    JsonValueKind.Number => property.Value.GetDecimal(),
                    JsonValueKind.String => property.Value.GetString()!,
                    _ => property.Value.GetRawText(),
                };
                result.Add(new(name, value));
            }

            return result;
        }
    }

    protected static void AddParameters(DbCommand command, IEnumerable<KeyValuePair<string, object>> parameters, DbProvider provider)
    {
        foreach (var (name, value) in parameters)
        {
            if (provider == DbProvider.PostgreSql)
            {
                // Sent as untyped literals so PostgreSQL infers the type from the procedure signature
                // (e.g. "2024-01-31" -> date, 5 -> integer/bigint/numeric) exactly like a quoted literal in SQL.
                command.Parameters.Add(new NpgsqlParameter(name, NpgsqlTypes.NpgsqlDbType.Unknown)
                {
                    Value = ToInvariantString(value),
                });
                continue;
            }

            var parameter = command.CreateParameter();
            parameter.ParameterName = "@" + name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
    }

    private static object ToInvariantString(object value) => value switch
    {
        DBNull => DBNull.Value,
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
