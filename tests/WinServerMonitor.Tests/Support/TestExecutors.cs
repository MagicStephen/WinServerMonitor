using WinServerMonitor.Core.Domain;
using WinServerMonitor.Core.Execution;

namespace WinServerMonitor.Tests.Support;

internal sealed class LambdaExecutor(string taskType, Func<TaskExecutionContext, CancellationToken, Task<TaskExecutionResult>> body) : ITaskExecutor
{
    public string TaskType => taskType;

    public string DisplayName => taskType;

    public string Description => "test";

    public IReadOnlyList<TaskParameterDescriptor> Parameters => [];

    public Task<TaskExecutionResult> ExecuteAsync(TaskExecutionContext context, CancellationToken cancellationToken) =>
        body(context, cancellationToken);
}

internal sealed class CollectingLogger : ITaskRunLogger
{
    private readonly List<(TaskLogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(TaskLogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public void Write(TaskLogLevel level, string message)
    {
        lock (_entries)
        {
            _entries.Add((level, message));
        }
    }
}
