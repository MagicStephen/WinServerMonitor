using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WinServerMonitor.Infrastructure.Execution;

/// <summary>Runs <see cref="MonitorOptions.MaxConcurrentRuns"/> workers that consume the <see cref="TaskQueue"/>.</summary>
public sealed class TaskWorkerService(
    TaskQueue queue,
    TaskRunner runner,
    IOptions<MonitorOptions> options,
    ILogger<TaskWorkerService> logger) : BackgroundService
{
    private static readonly TimeSpan DeferDelay = TimeSpan.FromSeconds(5);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerCount = Math.Max(1, options.Value.MaxConcurrentRuns);
        logger.LogInformation("Starting {Count} task workers", workerCount);
        return Task.WhenAll(Enumerable.Range(1, workerCount).Select(i => WorkerLoopAsync(i, stoppingToken)));
    }

    private async Task WorkerLoopAsync(int workerId, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            long runId;
            try
            {
                runId = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                var outcome = await runner.RunAsync(runId, stoppingToken);
                if (outcome == RunOutcome.Deferred)
                {
                    _ = RequeueLaterAsync(runId, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Worker {Worker} failed while processing run {RunId}", workerId, runId);
            }
        }
    }

    private async Task RequeueLaterAsync(long runId, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(DeferDelay, stoppingToken);
            queue.Enqueue(runId);
        }
        catch (OperationCanceledException)
        {
            // shutting down - the run stays Queued in the database and is recovered on next start
        }
    }
}
