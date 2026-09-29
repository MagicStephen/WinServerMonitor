using Microsoft.Extensions.Options;
using WinServerMonitor.Core.Metrics;

namespace WinServerMonitor.Infrastructure.Metrics;

public sealed class MetricsHistory(IOptions<MonitorOptions> options) : IMetricsHistory
{
    private readonly object _lock = new();
    private readonly Queue<ServerMetricsSnapshot> _snapshots = new();
    private readonly int _capacity = Math.Max(
        10,
        options.Value.MetricsHistoryMinutes * 60 / Math.Max(1, options.Value.MetricsIntervalSeconds));

    public event Action<ServerMetricsSnapshot>? SnapshotAdded;

    public ServerMetricsSnapshot? Latest { get; private set; }

    public IReadOnlyList<ServerMetricsSnapshot> GetHistory()
    {
        lock (_lock)
        {
            return _snapshots.ToArray();
        }
    }

    public void Add(ServerMetricsSnapshot snapshot)
    {
        lock (_lock)
        {
            _snapshots.Enqueue(snapshot);
            while (_snapshots.Count > _capacity)
            {
                _snapshots.Dequeue();
            }

            Latest = snapshot;
        }

        foreach (var handler in SnapshotAdded?.GetInvocationList().Cast<Action<ServerMetricsSnapshot>>() ?? [])
        {
            try
            {
                handler(snapshot);
            }
            catch
            {
                // subscriber (UI circuit) failure must not stop sampling
            }
        }
    }
}
