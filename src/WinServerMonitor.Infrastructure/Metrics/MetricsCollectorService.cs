using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WinServerMonitor.Core.Metrics;

namespace WinServerMonitor.Infrastructure.Metrics;

public sealed class MetricsCollectorService(
    IServerMetricsProvider provider,
    IMetricsHistory history,
    IOptions<MonitorOptions> options,
    ILogger<MetricsCollectorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Prime rate based counters so the first stored sample is meaningful.
        await Task.Run(provider.Capture, stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.Value.MetricsIntervalSeconds)));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    history.Add(provider.Capture());
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to capture server metrics");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }
}
