using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using WinServerMonitor.Core.Domain;
using WinServerMonitor.Core.Execution;
using WinServerMonitor.Infrastructure;
using WinServerMonitor.Infrastructure.Events;
using WinServerMonitor.Infrastructure.Execution;
using WinServerMonitor.Infrastructure.Persistence;
using WinServerMonitor.Infrastructure.Scheduling;
using WinServerMonitor.Infrastructure.Services;

namespace WinServerMonitor.Tests.Support;

/// <summary>Wires the real services together against the test database with a controllable clock.</summary>
internal sealed class Harness(PostgresFixture fixture, params ITaskExecutor[] executors)
{
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 15, 10, 2, 0, TimeSpan.Zero));

    public TaskQueue Queue { get; } = new();

    public RunningTaskRegistry Running { get; } = new();

    public MonitorEvents Events { get; } = new(NullLogger<MonitorEvents>.Instance);

    public IDbContextFactory<MonitorDbContext> Db => fixture.DbFactory;

    public TaskExecutorRegistry Executors { get; } = new(executors);

    public TaskRunner Runner => new(Db, Executors, Running, Events, Time, NullLogger<TaskRunner>.Instance);

    public TaskSchedulingEngine Scheduler =>
        new(Db, Queue, Events, Options.Create(new MonitorOptions()), Time, NullLogger<TaskSchedulingEngine>.Instance);

    public TaskRunService Runs => new(Db, Queue, Running, Events, Time);

    public TaskDefinitionService Definitions => new(Db, Executors, Events, Time);

    public async Task<TaskDefinition> AddTaskAsync(string taskType, Action<TaskDefinition>? configure = null)
    {
        var definition = new TaskDefinition
        {
            Name = "Test " + taskType,
            Domain = "Test",
            TaskType = taskType,
            TimeZoneId = "UTC",
            CreatedAtUtc = Time.GetUtcNow().UtcDateTime,
            UpdatedAtUtc = Time.GetUtcNow().UtcDateTime,
        };
        configure?.Invoke(definition);
        await using var db = await Db.CreateDbContextAsync();
        db.TaskDefinitions.Add(definition);
        await db.SaveChangesAsync();
        return definition;
    }

    public async Task<List<TaskRun>> RunsOfAsync(int definitionId)
    {
        await using var db = await Db.CreateDbContextAsync();
        return await db.TaskRuns.AsNoTracking().Where(r => r.TaskDefinitionId == definitionId).OrderBy(r => r.Id).ToListAsync();
    }

    public async Task<List<TaskLogEntry>> LogsOfAsync(long runId)
    {
        await using var db = await Db.CreateDbContextAsync();
        return await db.TaskLogs.AsNoTracking().Where(l => l.TaskRunId == runId).OrderBy(l => l.Id).ToListAsync();
    }
}
