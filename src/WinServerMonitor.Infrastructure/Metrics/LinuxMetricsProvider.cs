using System.Globalization;
using Microsoft.Extensions.Options;
using WinServerMonitor.Core.Metrics;

namespace WinServerMonitor.Infrastructure.Metrics;

/// <summary>
/// Linux implementation reading /proc. Lets the application run in development containers
/// or on Linux hosts; production target is Windows.
/// </summary>
public sealed class LinuxMetricsProvider(IOptions<MonitorOptions> options) : IServerMetricsProvider
{
    private const int SectorSize = 512;

    private readonly ProcessSampler _processes = new();
    private CpuTimes? _previousCpu;
    private DiskCounters? _previousDisk;
    private DateTime _previousDiskAtUtc;

    public ServerMetricsSnapshot Capture()
    {
        var now = DateTime.UtcNow;

        var cpu = ParseCpu(SafeRead("/proc/stat"));
        var cpuPercent = cpu is not null && _previousCpu is not null ? CpuTimes.UsagePercent(_previousCpu, cpu) : 0;
        _previousCpu = cpu;

        var (total, available) = ParseMemory(SafeRead("/proc/meminfo"));

        var disk = ParseDiskStats(SafeRead("/proc/diskstats"), IsPhysicalDisk);
        double readBps = 0, writeBps = 0, readsPs = 0, writesPs = 0;
        if (_previousDisk is not null)
        {
            var seconds = (now - _previousDiskAtUtc).TotalSeconds;
            if (seconds > 0)
            {
                readBps = Math.Max(0, (disk.SectorsRead - _previousDisk.SectorsRead) * SectorSize / seconds);
                writeBps = Math.Max(0, (disk.SectorsWritten - _previousDisk.SectorsWritten) * SectorSize / seconds);
                readsPs = Math.Max(0, (disk.ReadsCompleted - _previousDisk.ReadsCompleted) / seconds);
                writesPs = Math.Max(0, (disk.WritesCompleted - _previousDisk.WritesCompleted) / seconds);
            }
        }

        _previousDisk = disk;
        _previousDiskAtUtc = now;

        return new ServerMetricsSnapshot
        {
            TimestampUtc = now,
            CpuPercent = cpuPercent,
            MemoryTotalBytes = total,
            MemoryUsedBytes = total - available,
            DiskReadBytesPerSec = readBps,
            DiskWriteBytesPerSec = writeBps,
            DiskReadsPerSec = readsPs,
            DiskWritesPerSec = writesPs,
            DiskQueueLength = disk.IosInProgress,
            Drives = DriveSampler.Sample(),
            TopProcesses = _processes.Sample(options.Value.TopProcessCount),
            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
        };
    }

    public sealed record CpuTimes(long Idle, long Total)
    {
        public static double UsagePercent(CpuTimes previous, CpuTimes current)
        {
            var total = current.Total - previous.Total;
            var idle = current.Idle - previous.Idle;
            return total <= 0 ? 0 : Math.Clamp((total - idle) * 100d / total, 0, 100);
        }
    }

    public sealed record DiskCounters(long ReadsCompleted, long SectorsRead, long WritesCompleted, long SectorsWritten, long IosInProgress);

    /// <summary>Parses the aggregate "cpu" line of /proc/stat.</summary>
    public static CpuTimes? ParseCpu(string? stat)
    {
        var line = stat?.Split('\n').FirstOrDefault(l => l.StartsWith("cpu ", StringComparison.Ordinal));
        if (line is null)
        {
            return null;
        }

        var values = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(long.Parse).ToArray();
        // user nice system idle iowait irq softirq steal ...; idle time = idle + iowait
        var idle = values[3] + (values.Length > 4 ? values[4] : 0);
        var total = values.Take(8).Sum();
        return new CpuTimes(idle, total);
    }

    /// <summary>Returns (MemTotal, MemAvailable) in bytes from /proc/meminfo.</summary>
    public static (long Total, long Available) ParseMemory(string? meminfo)
    {
        long total = 0, available = 0;
        foreach (var line in (meminfo ?? string.Empty).Split('\n'))
        {
            var parts = line.Split(':', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            var kb = long.TryParse(parts[1].Trim().Split(' ')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
            switch (parts[0])
            {
                case "MemTotal":
                    total = kb * 1024;
                    break;
                case "MemAvailable":
                    available = kb * 1024;
                    break;
            }
        }

        return (total, available);
    }

    /// <summary>Sums counters of whole physical disks from /proc/diskstats.</summary>
    public static DiskCounters ParseDiskStats(string? diskstats, Func<string, bool> includeDevice)
    {
        long reads = 0, sectorsRead = 0, writes = 0, sectorsWritten = 0, inProgress = 0;
        foreach (var line in (diskstats ?? string.Empty).Split('\n'))
        {
            var f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 12 || !includeDevice(f[2]))
            {
                continue;
            }

            reads += long.Parse(f[3], CultureInfo.InvariantCulture);
            sectorsRead += long.Parse(f[5], CultureInfo.InvariantCulture);
            writes += long.Parse(f[7], CultureInfo.InvariantCulture);
            sectorsWritten += long.Parse(f[9], CultureInfo.InvariantCulture);
            inProgress += long.Parse(f[11], CultureInfo.InvariantCulture);
        }

        return new DiskCounters(reads, sectorsRead, writes, sectorsWritten, inProgress);
    }

    // Whole disks have a /sys/block entry; partitions, loop and ram devices are skipped.
    private static bool IsPhysicalDisk(string device) =>
        !device.StartsWith("loop", StringComparison.Ordinal) &&
        !device.StartsWith("ram", StringComparison.Ordinal) &&
        Directory.Exists($"/sys/block/{device}");

    private static string? SafeRead(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch
        {
            return null;
        }
    }
}
