using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WinServerMonitor.Core.Metrics;

namespace WinServerMonitor.Infrastructure.Metrics;

/// <summary>Windows implementation based on performance counters and GlobalMemoryStatusEx.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsMetricsProvider : IServerMetricsProvider, IDisposable
{
    private readonly PerformanceCounter? _cpu;
    private readonly PerformanceCounter? _diskReadBytes;
    private readonly PerformanceCounter? _diskWriteBytes;
    private readonly PerformanceCounter? _diskReads;
    private readonly PerformanceCounter? _diskWrites;
    private readonly PerformanceCounter? _diskQueue;
    private readonly ProcessSampler _processes = new();
    private readonly int _topProcesses;

    public WindowsMetricsProvider(IOptions<MonitorOptions> options, ILogger<WindowsMetricsProvider> logger)
    {
        _topProcesses = options.Value.TopProcessCount;

        PerformanceCounter? Create(string category, string counter, string instance)
        {
            try
            {
                var pc = new PerformanceCounter(category, counter, instance, readOnly: true);
                pc.NextValue(); // first value of rate counters is always 0
                return pc;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Performance counter {Category}\\{Counter} is not available", category, counter);
                return null;
            }
        }

        _cpu = Create("Processor Information", "% Processor Utility", "_Total")
               ?? Create("Processor", "% Processor Time", "_Total");
        _diskReadBytes = Create("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
        _diskWriteBytes = Create("PhysicalDisk", "Disk Write Bytes/sec", "_Total");
        _diskReads = Create("PhysicalDisk", "Disk Reads/sec", "_Total");
        _diskWrites = Create("PhysicalDisk", "Disk Writes/sec", "_Total");
        _diskQueue = Create("PhysicalDisk", "Avg. Disk Queue Length", "_Total");
    }

    public ServerMetricsSnapshot Capture()
    {
        var memory = new MemoryStatusEx();
        var hasMemory = GlobalMemoryStatusEx(memory);

        return new ServerMetricsSnapshot
        {
            TimestampUtc = DateTime.UtcNow,
            CpuPercent = Math.Clamp(Read(_cpu), 0, 100),
            MemoryTotalBytes = hasMemory ? (long)memory.TotalPhys : 0,
            MemoryUsedBytes = hasMemory ? (long)(memory.TotalPhys - memory.AvailPhys) : 0,
            DiskReadBytesPerSec = Read(_diskReadBytes),
            DiskWriteBytesPerSec = Read(_diskWriteBytes),
            DiskReadsPerSec = Read(_diskReads),
            DiskWritesPerSec = Read(_diskWrites),
            DiskQueueLength = Read(_diskQueue),
            Drives = DriveSampler.Sample(),
            TopProcesses = _processes.Sample(_topProcesses),
            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
        };
    }

    private static double Read(PerformanceCounter? counter)
    {
        try
        {
            return counter?.NextValue() ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    public void Dispose()
    {
        _cpu?.Dispose();
        _diskReadBytes?.Dispose();
        _diskWriteBytes?.Dispose();
        _diskReads?.Dispose();
        _diskWrites?.Dispose();
        _diskQueue?.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);
}
