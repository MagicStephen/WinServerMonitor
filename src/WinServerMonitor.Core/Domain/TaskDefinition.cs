using System.Text.Json;

namespace WinServerMonitor.Core.Domain;

/// <summary>
/// A configured job: what to run (<see cref="TaskType"/> + <see cref="Parameters"/>),
/// which business domain it belongs to and when it should run.
/// </summary>
public class TaskDefinition
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Business domain / area the task belongs to (e.g. "Finance", "Warehouse"). Used for grouping and filtering.</summary>
    public string Domain { get; set; } = "General";

    public string? Description { get; set; }

    /// <summary>Key of the <c>ITaskExecutor</c> that runs this task (e.g. "SqlProcedure").</summary>
    public string TaskType { get; set; } = string.Empty;

    /// <summary>Executor specific parameters serialized as a JSON object of string values.</summary>
    public string ParametersJson { get; set; } = "{}";

    /// <summary>Cron expression (5 or 6 fields). Null means the task only runs manually.</summary>
    public string? CronExpression { get; set; }

    /// <summary>IANA or Windows time zone id used to evaluate <see cref="CronExpression"/>. Null = server local time.</summary>
    public string? TimeZoneId { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>Hard timeout of a single run. 0 = no timeout.</summary>
    public int TimeoutSeconds { get; set; } = 600;

    /// <summary>How many times a failed run is automatically retried.</summary>
    public int MaxRetries { get; set; }

    /// <summary>Delay before an automatic retry is queued.</summary>
    public int RetryDelaySeconds { get; set; } = 30;

    /// <summary>When false, a new run is not started while another run of the same task is running.</summary>
    public bool AllowConcurrentRuns { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<TaskRun> Runs { get; set; } = [];

    public IReadOnlyDictionary<string, string> GetParameters()
    {
        if (string.IsNullOrWhiteSpace(ParametersJson))
        {
            return new Dictionary<string, string>();
        }

        return JsonSerializer.Deserialize<Dictionary<string, string>>(ParametersJson)
               ?? new Dictionary<string, string>();
    }

    public void SetParameters(IReadOnlyDictionary<string, string> parameters)
    {
        var cleaned = parameters
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .ToDictionary(p => p.Key, p => p.Value);
        ParametersJson = JsonSerializer.Serialize(cleaned);
    }
}
