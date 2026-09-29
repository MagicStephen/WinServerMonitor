using System.Diagnostics;
using WinServerMonitor.Core.Metrics;

namespace WinServerMonitor.Infrastructure.Metrics;

/// <summary>Computes per-process CPU usage from the difference of consumed processor time between samples.</summary>
internal sealed class ProcessSampler
{
    private Dictionary<int, TimeSpan> _previousCpu = [];
    private DateTime _previousSampleUtc = DateTime.MinValue;

    public IReadOnlyList<ProcessUsage> Sample(int top)
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _previousSampleUtc).TotalMilliseconds * Environment.ProcessorCount;
        var currentCpu = new Dictionary<int, TimeSpan>();
        var usages = new List<ProcessUsage>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var cpuTime = process.TotalProcessorTime;
                    currentCpu[process.Id] = cpuTime;
                    var cpu = 0d;
                    if (elapsed > 0 && _previousCpu.TryGetValue(process.Id, out var previous))
                    {
                        cpu = Math.Clamp((cpuTime - previous).TotalMilliseconds / elapsed * 100, 0, 100);
                    }

                    var name = process.ProcessName;
                    usages.Add(new ProcessUsage(process.Id, name.Length > 80 ? name[..80] : name, process.WorkingSet64, cpu));
                }
                catch
                {
                    // Access denied or the process exited - skip it.
                }
            }
        }

        _previousCpu = currentCpu;
        _previousSampleUtc = now;

        return usages
            .OrderByDescending(p => p.CpuPercent)
            .ThenByDescending(p => p.WorkingSetBytes)
            .Take(top)
            .ToList();
    }
}
