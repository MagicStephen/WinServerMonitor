namespace WinServerMonitor.Core.Domain;

/// <summary>One execution (planned, running or finished) of a <see cref="TaskDefinition"/>.</summary>
public class TaskRun
{
    public long Id { get; set; }

    public int TaskDefinitionId { get; set; }

    public TaskDefinition? TaskDefinition { get; set; }

    public TaskRunStatus Status { get; set; }

    public TaskTrigger Trigger { get; set; }

    /// <summary>Run this one re-runs or retries (if any).</summary>
    public long? ParentRunId { get; set; }

    /// <summary>1 for the first attempt, incremented by automatic retries.</summary>
    public int Attempt { get; set; } = 1;

    public string? RequestedBy { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>When the run should start. For manual runs equal to <see cref="CreatedAtUtc"/>.</summary>
    public DateTime ScheduledForUtc { get; set; } = DateTime.UtcNow;

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? FinishedAtUtc { get; set; }

    public string? ResultSummary { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>Machine that executed the run.</summary>
    public string? MachineName { get; set; }

    public List<TaskLogEntry> Logs { get; set; } = [];

    public TimeSpan? Duration =>
        StartedAtUtc is { } start ? (FinishedAtUtc ?? DateTime.UtcNow) - start : null;
}
