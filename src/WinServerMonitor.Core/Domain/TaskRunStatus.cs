namespace WinServerMonitor.Core.Domain;

/// <summary>Lifecycle of a single task run. Each value is one column on the Kanban board.</summary>
public enum TaskRunStatus
{
    Scheduled = 0,
    Queued = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
}

public static class TaskRunStatusExtensions
{
    public static bool IsFinished(this TaskRunStatus status) =>
        status is TaskRunStatus.Succeeded or TaskRunStatus.Failed or TaskRunStatus.Cancelled;

    public static bool IsActive(this TaskRunStatus status) =>
        status is TaskRunStatus.Scheduled or TaskRunStatus.Queued or TaskRunStatus.Running;
}
