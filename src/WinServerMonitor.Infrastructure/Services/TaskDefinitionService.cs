using Microsoft.EntityFrameworkCore;
using WinServerMonitor.Core.Domain;
using WinServerMonitor.Core.Events;
using WinServerMonitor.Core.Execution;
using WinServerMonitor.Infrastructure.Execution;
using WinServerMonitor.Infrastructure.Persistence;
using WinServerMonitor.Infrastructure.Scheduling;

namespace WinServerMonitor.Infrastructure.Services;

public sealed class TaskDefinitionService(
    IDbContextFactory<MonitorDbContext> dbFactory,
    TaskExecutorRegistry executors,
    IMonitorEvents events,
    TimeProvider time)
{
    public async Task<List<TaskDefinition>> GetAllAsync(string? domain = null, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.TaskDefinitions.AsNoTracking();
        if (!string.IsNullOrEmpty(domain))
        {
            query = query.Where(d => d.Domain == domain);
        }

        return await query.OrderBy(d => d.Domain).ThenBy(d => d.Name).ToListAsync(cancellationToken);
    }

    public async Task<TaskDefinition?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.TaskDefinitions.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<List<string>> GetDomainsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.TaskDefinitions.Select(d => d.Domain).Distinct().OrderBy(d => d).ToListAsync(cancellationToken);
    }

    /// <summary>Last run per task definition (for the task list).</summary>
    public async Task<Dictionary<int, TaskRun>> GetLastRunsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var lastIds = await db.TaskRuns
            .Where(r => r.StartedAtUtc != null)
            .GroupBy(r => r.TaskDefinitionId)
            .Select(g => g.Max(r => r.Id))
            .ToListAsync(cancellationToken);
        return await db.TaskRuns.AsNoTracking()
            .Where(r => lastIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.TaskDefinitionId, cancellationToken);
    }

    public IReadOnlyList<string> Validate(TaskDefinition definition)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            errors.Add("Název je povinný.");
        }

        if (string.IsNullOrWhiteSpace(definition.Domain))
        {
            errors.Add("Doména je povinná.");
        }

        var executor = executors.Find(definition.TaskType);
        if (executor is null)
        {
            errors.Add($"Neznámý typ tasku '{definition.TaskType}'.");
        }
        else
        {
            var parameters = definition.GetParameters();
            foreach (var parameter in executor.Parameters.Where(p => p.Required))
            {
                if (!parameters.TryGetValue(parameter.Name, out var value) || string.IsNullOrWhiteSpace(value))
                {
                    errors.Add($"Parametr '{parameter.Label}' je povinný.");
                }
            }
        }

        if (!CronSchedule.TryValidate(definition.CronExpression, out var cronError))
        {
            errors.Add($"Neplatný cron výraz: {cronError}");
        }

        if (!string.IsNullOrWhiteSpace(definition.TimeZoneId) &&
            !TimeZoneInfo.TryFindSystemTimeZoneById(definition.TimeZoneId, out _))
        {
            errors.Add($"Neznámé časové pásmo '{definition.TimeZoneId}'.");
        }

        if (definition.TimeoutSeconds < 0 || definition.MaxRetries < 0 || definition.RetryDelaySeconds < 0)
        {
            errors.Add("Timeout, počet opakování a prodleva nesmí být záporné.");
        }

        return errors;
    }

    public async Task<TaskDefinition> SaveAsync(TaskDefinition definition, CancellationToken cancellationToken = default)
    {
        var errors = Validate(definition);
        if (errors.Count > 0)
        {
            throw new TaskConfigurationException(string.Join(Environment.NewLine, errors));
        }

        definition.Name = definition.Name.Trim();
        definition.Domain = definition.Domain.Trim();
        definition.CronExpression = string.IsNullOrWhiteSpace(definition.CronExpression) ? null : definition.CronExpression.Trim();
        definition.TimeZoneId = string.IsNullOrWhiteSpace(definition.TimeZoneId) ? null : definition.TimeZoneId.Trim();
        definition.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (definition.Id == 0)
        {
            definition.CreatedAtUtc = definition.UpdatedAtUtc;
            db.TaskDefinitions.Add(definition);
        }
        else
        {
            db.TaskDefinitions.Update(definition);

            // Schedule may have changed - the scheduler re-plans the next occurrence on its next tick.
            await db.TaskRuns
                .Where(r => r.TaskDefinitionId == definition.Id
                            && r.Status == TaskRunStatus.Scheduled
                            && r.Trigger == TaskTrigger.Schedule)
                .ExecuteDeleteAsync(cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        events.PublishDefinitionsChanged();
        return definition;
    }

    public async Task SetEnabledAsync(int id, bool enabled, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.TaskDefinitions
            .Where(d => d.Id == id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(d => d.IsEnabled, enabled)
                    .SetProperty(d => d.UpdatedAtUtc, time.GetUtcNow().UtcDateTime),
                cancellationToken);
        if (!enabled)
        {
            await db.TaskRuns
                .Where(r => r.TaskDefinitionId == id && r.Status == TaskRunStatus.Scheduled && r.Trigger == TaskTrigger.Schedule)
                .ExecuteDeleteAsync(cancellationToken);
        }

        events.PublishDefinitionsChanged();
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await db.TaskRuns.AnyAsync(r => r.TaskDefinitionId == id && r.Status == TaskRunStatus.Running, cancellationToken))
        {
            throw new InvalidOperationException("Task právě běží, nejdříve ho zrušte.");
        }

        await db.TaskDefinitions.Where(d => d.Id == id).ExecuteDeleteAsync(cancellationToken);
        events.PublishDefinitionsChanged();
    }

    public IReadOnlyList<DateTime> PreviewSchedule(string? cron, string? timeZoneId, int count = 5)
    {
        if (string.IsNullOrWhiteSpace(cron) || !CronSchedule.TryValidate(cron, out _))
        {
            return [];
        }

        return CronSchedule.GetNextOccurrences(cron, timeZoneId, time.GetUtcNow().UtcDateTime, count);
    }
}
