namespace WinServerMonitor.Core.Metrics;

/// <summary>Reads current utilization of the machine the application runs on.</summary>
public interface IServerMetricsProvider
{
    /// <summary>
    /// Captures a snapshot. Rate based counters (CPU, disk IO) are computed from the
    /// difference to the previous call, so the first call may return zeros.
    /// </summary>
    ServerMetricsSnapshot Capture();
}
