namespace WinServerMonitor.Core.Metrics;

/// <summary>One sample of server utilization.</summary>
public sealed record ServerMetricsSnapshot
{
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;

    /// <summary>Total CPU utilization 0-100.</summary>
    public double CpuPercent { get; init; }

    public long MemoryTotalBytes { get; init; }

    public long MemoryUsedBytes { get; init; }

    public double MemoryPercent => MemoryTotalBytes > 0 ? MemoryUsedBytes * 100d / MemoryTotalBytes : 0;

    public double DiskReadBytesPerSec { get; init; }

    public double DiskWriteBytesPerSec { get; init; }

    public double DiskReadsPerSec { get; init; }

    public double DiskWritesPerSec { get; init; }

    /// <summary>Average disk queue length (Windows) or IOs in progress (Linux).</summary>
    public double DiskQueueLength { get; init; }

    public IReadOnlyList<DriveUsage> Drives { get; init; } = [];

    public IReadOnlyList<ProcessUsage> TopProcesses { get; init; } = [];

    public TimeSpan Uptime { get; init; }
}

public sealed record DriveUsage(string Name, string? Label, string Format, long TotalBytes, long FreeBytes)
{
    public long UsedBytes => TotalBytes - FreeBytes;

    public double UsedPercent => TotalBytes > 0 ? UsedBytes * 100d / TotalBytes : 0;
}

public sealed record ProcessUsage(int Id, string Name, long WorkingSetBytes, double CpuPercent);
