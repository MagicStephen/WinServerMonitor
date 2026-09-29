using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WinServerMonitor.Core.Domain;
using WinServerMonitor.Core.Events;
using WinServerMonitor.Infrastructure.Execution;
using WinServerMonitor.Infrastructure.Persistence;

namespace WinServerMonitor.Infrastructure.Scheduling;

/// <summary>
/// Scheduling logic, separated from the hosted service so it can be tested.
/// Each enabled task with a cron expression always has exactly one <see cref="TaskRunStatus.Scheduled"/>
/// run representing its next occurrence; due runs are moved to <see cref="TaskRunStatus.Queued"/>.
/// </summary>
public sealed class TaskSchedulingEngine(
    IDbContextFactory<MonitorDbContext> dbFactory,
    TaskQueue queue,
    IMonitorEvents events,
    IOptions<MonitorOptions> options,
    TimeProvider time,
    ILogger<TaskSchedulingEngine> logger)
{
    private DateTime _lastCleanupUtc = DateTime.MinValue;

    /// <summary>Called once at startup: fails runs interrupted by a restart and re-queues waiting ones.</summary>
    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;

        var interrupted = await db.TaskRuns
            .Where(r => r.Status == TaskRunStatus.Running)
            .ExecuteUpdateAsync(
                s => s.SetProperty(r => r.Status, TaskRunStatus.Failed)
                    .SetProperty(r => r.FinishedAtUtc, now)
                    .SetProperty(r => r.ErrorMessage, "Přerušeno restartem služby."),
                cancellationToken);
        if (interrupted > 0)
        {
            logger.LogWarning("{Count} runs were interrupted by a restart and marked as failed", interrupted);
        }

        var queued = await db.TaskRuns
            .Where(r => r.Status == TaskRunStatus.Queued)
            .OrderBy(r => r.ScheduledForUtc)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);
        foreach (var id in queued)
        {
            queue.Enqueue(id);
        }
    }

    public async Task TickAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;

        // Queue first, so a task whose occurrence was just queued gets its next occurrence planned in the same tick.
        await QueueDueRunsAsync(db, now, cancellationToken);
        await PlanNextOccurrencesAsync(db, now, cancellationToken);
        await CleanupAsync(db, now, cancellationToken);
    }

    private async Task PlanNextOccurrencesAsync(MonitorDbContext db, DateTime now, CancellationToken cancellationToken)
    {
        // Drop planned occurrences of tasks that were disabled or lost their schedule.
        var orphaned = await db.TaskRuns
            .Where(r => r.Status == TaskRunStatus.Scheduled && r.Trigger == TaskTrigger.Schedule)
            .Where(r => !r.TaskDefinition!.IsEnabled || r.TaskDefinition.CronExpression == null)
            .ToListAsync(cancellationToken);
        if (orphaned.Count > 0)
        {
            db.TaskRuns.RemoveRange(orphaned);
        }

        var definitions = await db.TaskDefinitions
            .Where(d => d.IsEnabled && d.CronExpression != null)
            .Where(d => !d.Runs.Any(r => r.Status == TaskRunStatus.Scheduled && r.Trigger == TaskTrigger.Schedule))
            .ToListAsync(cancellationToken);

        var added = new List<TaskRun>();
        foreach (var definition in definitions)
        {
            DateTime? next;
            try
            {
                next = CronSchedule.GetNextOccurrence(definition.CronExpression!, definition.TimeZoneId, now);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Invalid cron expression '{Cron}' of task {Task}", definition.CronExpression, definition.Name);
                continue;
            }

            if (next is null)
            {
                continue;
            }

            var run = new TaskRun
            {
                TaskDefinitionId = definition.Id,
                Status = TaskRunStatus.Scheduled,
                Trigger = TaskTrigger.Schedule,
                CreatedAtUtc = now,
                ScheduledForUtc = next.Value,
                RequestedBy = "scheduler",
            };
            db.TaskRuns.Add(run);
            added.Add(run);
        }

        if (orphaned.Count == 0 && added.Count == 0)
        {
            return;
        }

        await db.SaveChangesAsync(cancellationToken);
        foreach (var run in added)
        {
            events.PublishRunChanged(new(run.Id, run.TaskDefinitionId, run.Status));
        }

        foreach (var run in orphaned)
        {
            events.PublishRunChanged(new(run.Id, run.TaskDefinitionId, TaskRunStatus.Cancelled));
        }
    }

    private async Task QueueDueRunsAsync(MonitorDbContext db, DateTime now, CancellationToken cancellationToken)
    {
        var due = await db.TaskRuns
            .Where(r => r.Status == TaskRunStatus.Scheduled && r.ScheduledForUtc <= now)
            .OrderBy(r => r.ScheduledForUtc)
            .Select(r => new { r.Id, r.TaskDefinitionId })
            .ToListAsync(cancellationToken);

        foreach (var run in due)
        {
            var updated = await db.TaskRuns
                .Where(r => r.Id == run.Id && r.Status == TaskRunStatus.Scheduled)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, TaskRunStatus.Queued), cancellationToken);
            if (updated == 0)
            {
                continue;
            }

            queue.Enqueue(run.Id);
            events.PublishRunChanged(new(run.Id, run.TaskDefinitionId, TaskRunStatus.Queued));
        }
    }

    private async Task CleanupAsync(MonitorDbContext db, DateTime now, CancellationToken cancellationToken)
    {
        var retentionDays = options.Value.RunRetentionDays;
        if (retentionDays <= 0 || now - _lastCleanupUtc < TimeSpan.FromHours(1))
        {
            return;
        }

        _lastCleanupUtc = now;
        var threshold = now.AddDays(-retentionDays);
        var deleted = await db.TaskRuns
            .Where(r => r.FinishedAtUtc != null && r.FinishedAtUtc < threshold)
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted > 0)
        {
            logger.LogInformation("Retention: deleted {Count} runs older than {Days} days", deleted, retentionDays);
        }
    }
}
