using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WinServerMonitor.Core.Domain;
using WinServerMonitor.Core.Events;
using WinServerMonitor.Core.Execution;
using WinServerMonitor.Infrastructure.Persistence;

namespace WinServerMonitor.Infrastructure.Execution;

public enum RunOutcome
{
    /// <summary>The run was not in the Queued state any more (cancelled, already taken).</summary>
    Skipped,

    /// <summary>Another run of a non-concurrent task is running; try again later.</summary>
    Deferred,

    Completed,
}

/// <summary>Executes a single queued run: claims it, invokes the executor, stores result and schedules retries.</summary>
public sealed class TaskRunner(
    IDbContextFactory<MonitorDbContext> dbFactory,
    TaskExecutorRegistry executors,
    RunningTaskRegistry running,
    IMonitorEvents events,
    TimeProvider time,
    ILogger<TaskRunner> logger)
{
    public async Task<RunOutcome> RunAsync(long runId, CancellationToken stoppingToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(stoppingToken);

        var run = await db.TaskRuns
            .Include(r => r.TaskDefinition)
            .FirstOrDefaultAsync(r => r.Id == runId, stoppingToken);
        if (run?.TaskDefinition is null || run.Status != TaskRunStatus.Queued)
        {
            return RunOutcome.Skipped;
        }

        var definition = run.TaskDefinition;
        if (!definition.AllowConcurrentRuns &&
            await db.TaskRuns.AnyAsync(
                r => r.TaskDefinitionId == definition.Id && r.Status == TaskRunStatus.Running && r.Id != runId,
                stoppingToken))
        {
            return RunOutcome.Deferred;
        }

        // Atomic claim - protects against a parallel cancel or a second worker.
        var now = time.GetUtcNow().UtcDateTime;
        var claimed = await db.TaskRuns
            .Where(r => r.Id == runId && r.Status == TaskRunStatus.Queued)
            .ExecuteUpdateAsync(
                s => s.SetProperty(r => r.Status, TaskRunStatus.Running)
                    .SetProperty(r => r.StartedAtUtc, now)
                    .SetProperty(r => r.MachineName, Environment.MachineName),
                stoppingToken);
        if (claimed == 0)
        {
            return RunOutcome.Skipped;
        }

        await db.Entry(run).ReloadAsync(stoppingToken);
        events.PublishRunChanged(new TaskRunChanged(run.Id, definition.Id, TaskRunStatus.Running));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        if (definition.TimeoutSeconds > 0)
        {
            cts.CancelAfter(TimeSpan.FromSeconds(definition.TimeoutSeconds));
        }

        running.Register(runId, cts);

        var status = TaskRunStatus.Failed;
        string? summary = null;
        string? error = null;
        var retryable = true;

        var runLogger = new DbTaskRunLogger(runId, dbFactory, events, logger, time);
        try
        {
            ITaskRunLogger log = runLogger;
            log.Info($"Start: {definition.Name} ({definition.TaskType}), pokus {run.Attempt}, spuštěno: {run.Trigger}");

            var executor = executors.Find(definition.TaskType)
                ?? throw new TaskConfigurationException($"Unknown task type '{definition.TaskType}'.");

            var context = new TaskExecutionContext(definition, run, definition.GetParameters(), runLogger);
            var result = await executor.ExecuteAsync(context, cts.Token);

            status = result.Success ? TaskRunStatus.Succeeded : TaskRunStatus.Failed;
            summary = result.Summary;
            if (!result.Success)
            {
                error = result.Summary;
                log.Error($"Task skončil neúspěšně: {result.Summary}");
            }
            else
            {
                log.Info($"Hotovo{(summary is null ? string.Empty : ": " + summary)}");
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            if (running.WasCancelledByUser(runId))
            {
                status = TaskRunStatus.Cancelled;
                error = "Zrušeno uživatelem.";
                retryable = false;
            }
            else if (stoppingToken.IsCancellationRequested)
            {
                error = "Přerušeno ukončením služby.";
            }
            else
            {
                error = $"Překročen časový limit {definition.TimeoutSeconds} s.";
            }

            runLogger.Write(TaskLogLevel.Error, error);
        }
        catch (TaskConfigurationException ex)
        {
            error = ex.Message;
            retryable = false;
            runLogger.Write(TaskLogLevel.Error, $"Chyba konfigurace: {ex.Message}");
        }
        catch (Exception ex)
        {
            error = ex.Message;
            runLogger.Write(TaskLogLevel.Error, ex.ToString());
        }
        finally
        {
            running.Unregister(runId);
            await runLogger.DisposeAsync();
        }

        // Use a fresh token: the result must be stored even while the service shuts down.
        using var saveCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var finishedAt = time.GetUtcNow().UtcDateTime;
        run.Status = status;
        run.FinishedAtUtc = finishedAt;
        run.ResultSummary = Truncate(summary, 2000);
        run.ErrorMessage = Truncate(error, 4000);

        TaskRun? retry = null;
        if (status == TaskRunStatus.Failed && retryable && run.Attempt <= definition.MaxRetries)
        {
            retry = new TaskRun
            {
                TaskDefinitionId = definition.Id,
                Status = TaskRunStatus.Scheduled,
                Trigger = TaskTrigger.Retry,
                ParentRunId = run.Id,
                Attempt = run.Attempt + 1,
                RequestedBy = run.RequestedBy,
                CreatedAtUtc = finishedAt,
                ScheduledForUtc = finishedAt.AddSeconds(Math.Max(0, definition.RetryDelaySeconds)),
            };
            db.TaskRuns.Add(retry);
        }

        await db.SaveChangesAsync(saveCts.Token);
        logger.LogInformation("Run {RunId} of {Task} finished with {Status}", run.Id, definition.Name, status);

        events.PublishRunChanged(new TaskRunChanged(run.Id, definition.Id, status));
        if (retry is not null)
        {
            events.PublishRunChanged(new TaskRunChanged(retry.Id, definition.Id, retry.Status));
        }

        return RunOutcome.Completed;
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
