using Microsoft.Extensions.Logging;
using WinServerMonitor.Core.Domain;
using WinServerMonitor.Core.Events;

namespace WinServerMonitor.Infrastructure.Events;

public sealed class MonitorEvents(ILogger<MonitorEvents> logger) : IMonitorEvents
{
    public event Action<TaskRunChanged>? RunChanged;

    public event Action<TaskLogEntry>? LogAdded;

    public event Action? DefinitionsChanged;

    public void PublishRunChanged(TaskRunChanged change) => Raise(RunChanged, h => h(change));

    public void PublishLog(TaskLogEntry entry) => Raise(LogAdded, h => h(entry));

    public void PublishDefinitionsChanged() => Raise(DefinitionsChanged, h => h());

    // A failing subscriber (e.g. a disconnected Blazor circuit) must not break the publisher.
    private void Raise<T>(T? handlers, Action<T> invoke) where T : Delegate
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<T>())
        {
            try
            {
                invoke(handler);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Event subscriber failed");
            }
        }
    }
}
