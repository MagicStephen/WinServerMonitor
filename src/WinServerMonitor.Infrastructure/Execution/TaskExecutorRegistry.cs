using WinServerMonitor.Core.Execution;

namespace WinServerMonitor.Infrastructure.Execution;

public sealed class TaskExecutorRegistry(IEnumerable<ITaskExecutor> executors)
{
    private readonly Dictionary<string, ITaskExecutor> _executors =
        executors.ToDictionary(e => e.TaskType, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<ITaskExecutor> All => _executors.Values;

    public ITaskExecutor? Find(string taskType) =>
        _executors.TryGetValue(taskType, out var executor) ? executor : null;
}
