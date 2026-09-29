using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WinServerMonitor.Infrastructure.Scheduling;

public sealed class TaskSchedulerService(
    TaskSchedulingEngine engine,
    IOptions<MonitorOptions> options,
    ILogger<TaskSchedulerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await engine.RecoverAsync(stoppingToken);

        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.SchedulerIntervalSeconds));
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await engine.TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduler tick failed");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken token)
    {
        try
        {
            return await timer.WaitForNextTickAsync(token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
