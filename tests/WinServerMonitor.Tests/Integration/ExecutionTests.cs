using WinServerMonitor.Core.Domain;
using WinServerMonitor.Core.Execution;
using WinServerMonitor.Infrastructure.Execution;
using WinServerMonitor.Tests.Support;

namespace WinServerMonitor.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class ExecutionTests(PostgresFixture fixture) : IAsyncLifetime
{
    private readonly TaskCompletionSource _blockingStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Harness _h = default!;

    public async Task InitializeAsync()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetAsync();
        _h = new Harness(
            fixture,
            new LambdaExecutor("Ok", (ctx, _) =>
            {
                ctx.Log.Info("hello from task");
                return Task.FromResult(TaskExecutionResult.Ok("42 rows"));
            }),
            new LambdaExecutor("Boom", (_, _) => throw new InvalidOperationException("boom")),
            new LambdaExecutor("Blocking", async (_, ct) =>
            {
                _blockingStarted.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return TaskExecutionResult.Ok();
            }));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [SkippableFact]
    public async Task Run_Success_StoresResultAndLogs()
    {
        var task = await _h.AddTaskAsync("Ok");
        var run = await _h.Runs.RunNowAsync(task.Id, "tester");

        var outcome = await _h.Runner.RunAsync(run.Id, default);

        Assert.Equal(RunOutcome.Completed, outcome);
        var stored = Assert.Single(await _h.RunsOfAsync(task.Id));
        Assert.Equal(TaskRunStatus.Succeeded, stored.Status);
        Assert.Equal("42 rows", stored.ResultSummary);
        Assert.NotNull(stored.StartedAtUtc);
        Assert.NotNull(stored.FinishedAtUtc);
        Assert.Equal("tester", stored.RequestedBy);
        Assert.Contains(await _h.LogsOfAsync(run.Id), l => l.Message == "hello from task");
    }

    [SkippableFact]
    public async Task Run_Failure_SchedulesRetry_UntilMaxRetries()
    {
        var task = await _h.AddTaskAsync("Boom", d =>
        {
            d.MaxRetries = 1;
            d.RetryDelaySeconds = 30;
        });
        var first = await _h.Runs.RunNowAsync(task.Id, null);

        await _h.Runner.RunAsync(first.Id, default);

        var runs = await _h.RunsOfAsync(task.Id);
        Assert.Equal(2, runs.Count);
        Assert.Equal(TaskRunStatus.Failed, runs[0].Status);
        Assert.Equal("boom", runs[0].ErrorMessage);
        var retry = runs[1];
        Assert.Equal(TaskRunStatus.Scheduled, retry.Status);
        Assert.Equal(TaskTrigger.Retry, retry.Trigger);
        Assert.Equal(2, retry.Attempt);
        Assert.Equal(first.Id, retry.ParentRunId);
        Assert.Equal(runs[0].FinishedAtUtc!.Value.AddSeconds(30), retry.ScheduledForUtc);

        await _h.Runs.StartScheduledNowAsync(retry.Id);
        await _h.Runner.RunAsync(retry.Id, default);

        runs = await _h.RunsOfAsync(task.Id);
        Assert.Equal(2, runs.Count); // no third attempt
        Assert.All(runs, r => Assert.Equal(TaskRunStatus.Failed, r.Status));
    }

    [SkippableFact]
    public async Task Run_Timeout_FailsRun()
    {
        var task = await _h.AddTaskAsync("Blocking", d => d.TimeoutSeconds = 1);
        var run = await _h.Runs.RunNowAsync(task.Id, null);

        await _h.Runner.RunAsync(run.Id, default);

        var stored = Assert.Single(await _h.RunsOfAsync(task.Id));
        Assert.Equal(TaskRunStatus.Failed, stored.Status);
        Assert.Contains("časový limit", stored.ErrorMessage);
    }

    [SkippableFact]
    public async Task Cancel_RunningRun_MarksCancelled_WithoutRetry()
    {
        var task = await _h.AddTaskAsync("Blocking", d => d.MaxRetries = 3);
        var run = await _h.Runs.RunNowAsync(task.Id, null);

        var execution = _h.Runner.RunAsync(run.Id, default);
        await _blockingStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(await _h.Runs.CancelAsync(run.Id));
        await execution.WaitAsync(TimeSpan.FromSeconds(10));

        var stored = Assert.Single(await _h.RunsOfAsync(task.Id));
        Assert.Equal(TaskRunStatus.Cancelled, stored.Status);
    }

    [SkippableFact]
    public async Task Cancel_QueuedRun_IsNotExecuted()
    {
        var task = await _h.AddTaskAsync("Ok");
        var run = await _h.Runs.RunNowAsync(task.Id, null);

        Assert.True(await _h.Runs.CancelAsync(run.Id));
        var outcome = await _h.Runner.RunAsync(run.Id, default);

        Assert.Equal(RunOutcome.Skipped, outcome);
        Assert.Equal(TaskRunStatus.Cancelled, Assert.Single(await _h.RunsOfAsync(task.Id)).Status);
    }

    [SkippableFact]
    public async Task Rerun_CreatesNewQueuedRun_LinkedToOriginal()
    {
        var task = await _h.AddTaskAsync("Boom");
        var original = await _h.Runs.RunNowAsync(task.Id, null);
        await _h.Runner.RunAsync(original.Id, default);
        await _h.Queue.DequeueAsync(default); // manual run's queue entry

        var rerun = await _h.Runs.RerunAsync(original.Id, "operator");

        Assert.Equal(TaskRunStatus.Queued, rerun.Status);
        Assert.Equal(TaskTrigger.Rerun, rerun.Trigger);
        Assert.Equal(original.Id, rerun.ParentRunId);
        Assert.Equal("operator", rerun.RequestedBy);
        Assert.Equal(rerun.Id, await _h.Queue.DequeueAsync(default));

        var related = await _h.Runs.GetRelatedRunsAsync(rerun);
        Assert.Equal([original.Id, rerun.Id], related.Select(r => r.Id));
    }

    [SkippableFact]
    public async Task Rerun_OfActiveRun_IsRejected()
    {
        var task = await _h.AddTaskAsync("Ok");
        var run = await _h.Runs.RunNowAsync(task.Id, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _h.Runs.RerunAsync(run.Id, null));
    }

    [SkippableFact]
    public async Task NonConcurrentTask_IsDeferred_WhileAnotherRunIsRunning()
    {
        var task = await _h.AddTaskAsync("Blocking", d => d.AllowConcurrentRuns = false);
        var first = await _h.Runs.RunNowAsync(task.Id, null);
        var second = await _h.Runs.RunNowAsync(task.Id, null);

        var execution = _h.Runner.RunAsync(first.Id, default);
        await _blockingStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(RunOutcome.Deferred, await _h.Runner.RunAsync(second.Id, default));

        await _h.Runs.CancelAsync(first.Id);
        await execution.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [SkippableFact]
    public async Task UnknownTaskType_FailsWithoutRetry()
    {
        var task = await _h.AddTaskAsync("DoesNotExist", d => d.MaxRetries = 2);
        var run = await _h.Runs.RunNowAsync(task.Id, null);

        await _h.Runner.RunAsync(run.Id, default);

        var stored = Assert.Single(await _h.RunsOfAsync(task.Id));
        Assert.Equal(TaskRunStatus.Failed, stored.Status);
        Assert.Contains("DoesNotExist", stored.ErrorMessage);
    }
}
