using WinServerMonitor.Core.Domain;

namespace WinServerMonitor.Core.Events;

public sealed record TaskRunChanged(long RunId, int TaskDefinitionId, TaskRunStatus Status);

/// <summary>In-process notifications consumed by the live UI (Kanban, log viewer).</summary>
public interface IMonitorEvents
{
    event Action<TaskRunChanged>? RunChanged;

    event Action<TaskLogEntry>? LogAdded;

    event Action? DefinitionsChanged;

    void PublishRunChanged(TaskRunChanged change);

    void PublishLog(TaskLogEntry entry);

    void PublishDefinitionsChanged();
}
