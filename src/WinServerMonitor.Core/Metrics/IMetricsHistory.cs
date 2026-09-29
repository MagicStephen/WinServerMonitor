namespace WinServerMonitor.Core.Metrics;

/// <summary>In-memory rolling window of recent snapshots.</summary>
public interface IMetricsHistory
{
    ServerMetricsSnapshot? Latest { get; }

    IReadOnlyList<ServerMetricsSnapshot> GetHistory();

    void Add(ServerMetricsSnapshot snapshot);

    event Action<ServerMetricsSnapshot>? SnapshotAdded;
}
