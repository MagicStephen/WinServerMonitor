using WinServerMonitor.Core.Domain;
using WinServerMonitor.Core.Execution;
using WinServerMonitor.Tests.Support;

namespace WinServerMonitor.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class SchedulingTests(PostgresFixture fixture) : IAsyncLifetime
{
    private readonly Harness _h = new(fixture, new LambdaExecutor("Noop", (_, _) => Task.FromResult(TaskExecutionResult.Ok())));

    public async Task InitializeAsync()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [SkippableFact]
    public async Task Tick_PlansNextOccurrence_OnlyOnce()
    {
        var task = await _h.AddTaskAsync("Noop", d => d.CronExpression = "*/5 * * * *");

        await _h.Scheduler.TickAsync(default);
        await _h.Scheduler.TickAsync(default);

        var run = Assert.Single(await _h.RunsOfAsync(task.Id));
        Assert.Equal(TaskRunStatus.Scheduled, run.Status);
        Assert.Equal(TaskTrigger.Schedule, run.Trigger);
        Assert.Equal(new DateTime(2026, 1, 15, 10, 5, 0, DateTimeKind.Utc), run.ScheduledForUtc);
        Assert.Equal(0, _h.Queue.Count);
    }

    [SkippableFact]
    public async Task Tick_QueuesDueRun_AndPlansTheFollowingOne()
    {
        var task = await _h.AddTaskAsync("Noop", d => d.CronExpression = "*/5 * * * *");
        await _h.Scheduler.TickAsync(default);

        _h.Time.Advance(TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(1)); // 10:05:01
        await _h.Scheduler.TickAsync(default);

        var runs = await _h.RunsOfAsync(task.Id);
        Assert.Equal(2, runs.Count);
        Assert.Equal(TaskRunStatus.Queued, runs[0].Status);
        Assert.Equal(TaskRunStatus.Scheduled, runs[1].Status);
        Assert.Equal(new DateTime(2026, 1, 15, 10, 10, 0, DateTimeKind.Utc), runs[1].ScheduledForUtc);
        Assert.Equal(runs[0].Id, await _h.Queue.DequeueAsync(default));
    }

    [SkippableFact]
    public async Task Tick_RemovesPlannedRun_WhenTaskIsDisabled()
    {
        var task = await _h.AddTaskAsync("Noop", d => d.CronExpression = "*/5 * * * *");
        await _h.Scheduler.TickAsync(default);

        await _h.Definitions.SetEnabledAsync(task.Id, false);
        await _h.Scheduler.TickAsync(default);

        Assert.Empty(await _h.RunsOfAsync(task.Id));
    }

    [SkippableFact]
    public async Task Tick_IgnoresTasksWithoutSchedule()
    {
        var task = await _h.AddTaskAsync("Noop");

        await _h.Scheduler.TickAsync(default);

        Assert.Empty(await _h.RunsOfAsync(task.Id));
    }

    [SkippableFact]
    public async Task Recover_FailsInterruptedRuns_AndRequeuesWaitingRuns()
    {
        var task = await _h.AddTaskAsync("Noop", d => d.AllowConcurrentRuns = true);
        var running = await _h.Runs.RunNowAsync(task.Id, "test");
        var queued = await _h.Runs.RunNowAsync(task.Id, "test");
        await using (var db = await _h.Db.CreateDbContextAsync())
        {
            var entity = await db.TaskRuns.FindAsync(running.Id);
            entity!.Status = TaskRunStatus.Running;
            await db.SaveChangesAsync();
        }

        var harness = new Harness(fixture); // fresh, empty queue = service restart
        await harness.Scheduler.RecoverAsync(default);

        var runs = await harness.RunsOfAsync(task.Id);
        Assert.Equal(TaskRunStatus.Failed, runs.Single(r => r.Id == running.Id).Status);
        Assert.Equal(TaskRunStatus.Queued, runs.Single(r => r.Id == queued.Id).Status);
        Assert.Equal(queued.Id, await harness.Queue.DequeueAsync(default));
    }
}
