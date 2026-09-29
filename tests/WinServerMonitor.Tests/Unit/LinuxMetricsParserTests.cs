using WinServerMonitor.Infrastructure.Metrics;

namespace WinServerMonitor.Tests.Unit;

public class LinuxMetricsParserTests
{
    [Fact]
    public void CpuUsage_IsComputedFromDelta()
    {
        var before = LinuxMetricsProvider.ParseCpu("cpu  100 0 100 800 0 0 0 0 0 0\ncpu0 1 2 3 4");
        var after = LinuxMetricsProvider.ParseCpu("cpu  150 0 150 900 0 0 0 0 0 0");

        Assert.Equal(50, LinuxMetricsProvider.CpuTimes.UsagePercent(before!, after!), 3);
    }

    [Fact]
    public void Memory_IsParsedInBytes()
    {
        var (total, available) = LinuxMetricsProvider.ParseMemory("MemTotal:       16000 kB\nMemFree:  100 kB\nMemAvailable:    4000 kB\n");

        Assert.Equal(16000 * 1024L, total);
        Assert.Equal(4000 * 1024L, available);
    }

    [Fact]
    public void DiskStats_SumsSelectedDevices()
    {
        const string stats = """
               8       0 sda 100 0 2000 0 50 0 1000 0 2 0 0
               8       1 sda1 90 0 1800 0 40 0 900 0 1 0 0
               7       0 loop0 5 0 10 0 0 0 0 0 0 0 0
             259       0 nvme0n1 10 0 200 0 5 0 100 0 1 0 0
            """;

        var counters = LinuxMetricsProvider.ParseDiskStats(stats, d => d is "sda" or "nvme0n1");

        Assert.Equal(new LinuxMetricsProvider.DiskCounters(110, 2200, 55, 1100, 3), counters);
    }
}
