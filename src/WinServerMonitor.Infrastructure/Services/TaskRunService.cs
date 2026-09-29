using Microsoft.EntityFrameworkCore;
using WinServerMonitor.Core.Domain;
using WinServerMonitor.Core.Events;
using WinServerMonitor.Infrastructure.Execution;
using WinServerMonitor.Infrastructure.Persistence;

namespace WinServerMonitor.Infrastructure.Services;

public sealed record RunFilter
{
    public string? Domain { get; init; }

    public int? TaskDefinitionId { get; init; }

    public TaskRunStatus? Status { get; init; }

    public string? Search { get; init; }

    public int Skip { get; init; }

    public int Take { get; init; } = 50;
}

public sealed record RunStatistics(int Scheduled, int Queued, int Running, int Succeeded, int Failed, int Cancelled);

public sealed class TaskRunService(
    IDbContextFactory<MonitorDbContext> dbFactory,
    TaskQueue queue,
    RunningTaskRegistry running,
    IMonitorEvents events,
    TimeProvider time)
{
    /// <summary>Runs for the Kanban board: all active runs plus runs finished within <paramref name="finishedWithin"/>.</summary>
    public async Task<List<TaskRun>> GetBoardAsync(string? domain, TimeSpan finishedWithin, int maxFinishedPerStatus = 100, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var since = time.GetUtcNow().UtcDateTime - finishedWithin;

        var query = db.TaskRuns.AsNoTracking().Include(r => r.TaskDefinition).AsQueryable();
        if (!string.IsNullOrEmpty(domain))
        {
            query = query.Where(r => r.TaskDefinition!.Domain == domain);
        }

        var active = await query
            .Where(r => r.Status == TaskRunStatus.Scheduled || r.Status == TaskRunStatus.Queued || r.Status == TaskRunStatus.Running)
            .OrderBy(r => r.ScheduledForUtc)
            .ToListAsync(cancellationToken);

        var finished = new List<TaskRun>();
        foreach (var status in new[] { TaskRunStatus.Succeeded, TaskRunStatus.Failed, TaskRunStatus.Cancelled })
        {
            finished.AddRange(await query
                .Where(r => r.Status == status && r.FinishedAtUtc >= since)
                .OrderByDescending(r => r.FinishedAtUtc)
                .Take(maxFinishedPerStatus)
                .ToListAsync(cancellationToken));
        }

        return [.. active, .. finished];
    }

    public async Task<TaskRun?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.TaskRuns.AsNoTracking()
            .Include(r => r.TaskDefinition)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<List<TaskLogEntry>> GetLogsAsync(long runId, long afterId = 0, int take = 5000, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.TaskLogs.AsNoTracking()
            .Where(l => l.TaskRunId == runId && l.Id > afterId)
            .OrderBy(l => l.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Chain of attempts this run belongs to (parent re-runs / retries and their children).</summary>
    public async Task<List<TaskRun>> GetRelatedRunsAsync(TaskRun run, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var ids = new HashSet<long> { run.Id };
        var parentId = run.ParentRunId;
        while (parentId is { } pid && ids.Add(pid))
        {
            parentId = await db.TaskRuns.Where(r => r.Id == pid).Select(r => r.ParentRunId).FirstOrDefaultAsync(cancellationToken);
        }

        var frontier = ids.ToList();
        while (frontier.Count > 0)
        {
            var children = await db.TaskRuns
                .Where(r => r.ParentRunId != null && frontier.Contains(r.ParentRunId.Value))
                .Select(r => r.Id)
                .ToListAsync(cancellationToken);
            frontier = children.Where(ids.Add).ToList();
        }

        return await db.TaskRuns.AsNoTracking()
            .Where(r => ids.Contains(r.Id))
            .OrderBy(r => r.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<(List<TaskRun> Items, int Total)> GetHistoryAsync(RunFilter filter, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.TaskRuns.AsNoTracking().Include(r => r.TaskDefinition).AsQueryable();
        if (!string.IsNullOrEmpty(filter.Domain))
        {
            query = query.Where(r => r.TaskDefinition!.Domain == filter.Domain);
        }

        if (filter.TaskDefinitionId is { } defId)
        {
            query = query.Where(r => r.TaskDefinitionId == defId);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(r => r.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(r => r.TaskDefinition!.Name.Contains(term)
                                     || (r.ErrorMessage != null && r.ErrorMessage.Contains(term)));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(r => r.Id)
            .Skip(filter.Skip)
            .Take(filter.Take)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<RunStatistics> GetStatisticsAsync(TimeSpan finishedWithin, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var since = time.GetUtcNow().UtcDateTime - finishedWithin;
        var counts = await db.TaskRuns
            .Where(r => r.FinishedAtUtc == null || r.FinishedAtUtc >= since)
            .GroupBy(r => r.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        int Get(TaskRunStatus s) => counts.GetValueOrDefault(s);
        return new RunStatistics(
            Get(TaskRunStatus.Scheduled), Get(TaskRunStatus.Queued), Get(TaskRunStatus.Running),
            Get(TaskRunStatus.Succeeded), Get(TaskRunStatus.Failed), Get(TaskRunStatus.Cancelled));
    }

    /// <summary>Creates a manual run and queues it immediately.</summary>
    public async Task<TaskRun> RunNowAsync(int taskDefinitionId, string? requestedBy, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.TaskDefinitions.AnyAsync(d => d.Id == taskDefinitionId, cancellationToken))
        {
            throw new InvalidOperationException($"Task {taskDefinitionId} neexistuje.");
        }

        return await QueueNewRunAsync(db, taskDefinitionId, TaskTrigger.Manual, null, requestedBy, cancellationToken);
    }

    /// <summary>Re-runs a finished run as a new run (the original run and its logs stay untouched).</summary>
    public async Task<TaskRun> RerunAsync(long runId, string? requestedBy, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var original = await db.TaskRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken)
                       ?? throw new InvalidOperationException($"Běh {runId} neexistuje.");
        if (!original.Status.IsFinished())
        {
            throw new InvalidOperationException("Znovu spustit lze pouze dokončený běh.");
        }

        return await QueueNewRunAsync(db, original.TaskDefinitionId, TaskTrigger.Rerun, original.Id, requestedBy, cancellationToken);
    }

    /// <summary>Moves a planned (Scheduled) run to the queue right now.</summary>
    public async Task StartScheduledNowAsync(long runId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        var run = await db.TaskRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        var updated = await db.TaskRuns
            .Where(r => r.Id == runId && r.Status == TaskRunStatus.Scheduled)
            .ExecuteUpdateAsync(
                s => s.SetProperty(r => r.Status, TaskRunStatus.Queued).SetProperty(r => r.ScheduledForUtc, now),
                cancellationToken);
        if (updated > 0 && run is not null)
        {
            queue.Enqueue(runId);
            events.PublishRunChanged(new(runId, run.TaskDefinitionId, TaskRunStatus.Queued));
        }
    }

    /// <summary>Cancels a scheduled, queued or running run.</summary>
    public async Task<bool> CancelAsync(long runId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var run = await db.TaskRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
        {
            return false;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var updated = await db.TaskRuns
            .Where(r => r.Id == runId && (r.Status == TaskRunStatus.Scheduled || r.Status == TaskRunStatus.Queued))
            .ExecuteUpdateAsync(
                s => s.SetProperty(r => r.Status, TaskRunStatus.Cancelled)
                    .SetProperty(r => r.FinishedAtUtc, now)
                    .SetProperty(r => r.ErrorMessage, "Zrušeno uživatelem před spuštěním."),
                cancellationToken);
        if (updated > 0)
        {
            events.PublishRunChanged(new(runId, run.TaskDefinitionId, TaskRunStatus.Cancelled));
            return true;
        }

        // Running - the worker stores the Cancelled state once the executor stops.
        return running.RequestCancel(runId);
    }

    private async Task<TaskRun> QueueNewRunAsync(
        MonitorDbContext db, int taskDefinitionId, TaskTrigger trigger, long? parentRunId, string? requestedBy, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var run = new TaskRun
        {
            TaskDefinitionId = taskDefinitionId,
            Status = TaskRunStatus.Queued,
            Trigger = trigger,
            ParentRunId = parentRunId,
            RequestedBy = requestedBy,
            CreatedAtUtc = now,
            ScheduledForUtc = now,
        };
        db.TaskRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        queue.Enqueue(run.Id);
        events.PublishRunChanged(new(run.Id, taskDefinitionId, run.Status));
        return run;
    }
}
