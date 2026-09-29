namespace WinServerMonitor.Core.Execution;

/// <summary>
/// Implements one kind of task (stored procedure call, process, HTTP call, ...).
/// Register implementations in DI; they are resolved by <see cref="TaskType"/>.
/// </summary>
public interface ITaskExecutor
{
    /// <summary>Unique key stored in <c>TaskDefinition.TaskType</c>.</summary>
    string TaskType { get; }

    string DisplayName { get; }

    string Description { get; }

    /// <summary>Parameters the UI should offer for this task type.</summary>
    IReadOnlyList<TaskParameterDescriptor> Parameters { get; }

    Task<TaskExecutionResult> ExecuteAsync(TaskExecutionContext context, CancellationToken cancellationToken);
}
