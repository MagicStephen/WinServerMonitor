using System.Collections.Concurrent;

namespace WinServerMonitor.Infrastructure.Execution;

/// <summary>Keeps cancellation handles of currently executing runs so they can be stopped from the UI.</summary>
public sealed class RunningTaskRegistry
{
    private readonly ConcurrentDictionary<long, Entry> _running = new();

    public void Register(long runId, CancellationTokenSource cts) => _running[runId] = new Entry(cts);

    public void Unregister(long runId) => _running.TryRemove(runId, out _);

    public bool IsRunning(long runId) => _running.ContainsKey(runId);

    public bool RequestCancel(long runId)
    {
        if (!_running.TryGetValue(runId, out var entry))
        {
            return false;
        }

        entry.CancelledByUser = true;
        entry.Cts.Cancel();
        return true;
    }

    public bool WasCancelledByUser(long runId) =>
        _running.TryGetValue(runId, out var entry) && entry.CancelledByUser;

    private sealed class Entry(CancellationTokenSource cts)
    {
        public CancellationTokenSource Cts { get; } = cts;

        public volatile bool CancelledByUser;
    }
}
