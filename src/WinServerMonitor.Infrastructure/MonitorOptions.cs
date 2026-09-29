namespace WinServerMonitor.Infrastructure;

public sealed class MonitorOptions
{
    public const string SectionName = "WinServerMonitor";

    /// <summary>How many task runs may execute in parallel.</summary>
    public int MaxConcurrentRuns { get; set; } = 4;

    /// <summary>How often the scheduler looks for due runs.</summary>
    public int SchedulerIntervalSeconds { get; set; } = 5;

    /// <summary>Sampling interval of server metrics.</summary>
    public int MetricsIntervalSeconds { get; set; } = 2;

    /// <summary>How much metrics history is kept in memory for charts.</summary>
    public int MetricsHistoryMinutes { get; set; } = 15;

    /// <summary>How many processes are listed in the "top processes" table.</summary>
    public int TopProcessCount { get; set; } = 10;

    /// <summary>Finished runs (and their logs) older than this are deleted. 0 = keep forever.</summary>
    public int RunRetentionDays { get; set; } = 30;

    /// <summary>Insert demo task definitions into an empty database.</summary>
    public bool SeedDemoTasks { get; set; } = true;

    /// <summary>
    /// Named database connections usable by database tasks. Tasks reference only the name,
    /// so credentials never end up in the task database or UI.
    /// </summary>
    public Dictionary<string, DbConnectionOptions> Connections { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public enum DbProvider
{
    PostgreSql,
    SqlServer,
}

public sealed class DbConnectionOptions
{
    public DbProvider Provider { get; set; } = DbProvider.PostgreSql;

    public string ConnectionString { get; set; } = string.Empty;
}
