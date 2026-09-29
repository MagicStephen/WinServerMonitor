using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WinServerMonitor.Core.Domain;
using WinServerMonitor.Core.Events;
using WinServerMonitor.Core.Execution;
using WinServerMonitor.Infrastructure.Persistence;

namespace WinServerMonitor.Infrastructure.Execution;

/// <summary>
/// Collects log entries of one run and persists them in small batches in the background,
/// so executors can log from any thread (process output, SQL info messages) without blocking.
/// </summary>
public sealed class DbTaskRunLogger : ITaskRunLogger, IAsyncDisposable
{
    private const int MaxMessageLength = 8000;

    private readonly long _runId;
    private readonly IDbContextFactory<MonitorDbContext> _dbFactory;
    private readonly IMonitorEvents _events;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly Channel<TaskLogEntry> _pending = Channel.CreateUnbounded<TaskLogEntry>();
    private readonly Task _pump;

    public DbTaskRunLogger(
        long runId,
        IDbContextFactory<MonitorDbContext> dbFactory,
        IMonitorEvents events,
        ILogger logger,
        TimeProvider time)
    {
        _runId = runId;
        _dbFactory = dbFactory;
        _events = events;
        _logger = logger;
        _time = time;
        _pump = Task.Run(PumpAsync);
    }

    public void Write(TaskLogLevel level, string message)
    {
        if (message.Length > MaxMessageLength)
        {
            message = message[..MaxMessageLength] + " …(truncated)";
        }

        _pending.Writer.TryWrite(new TaskLogEntry
        {
            TaskRunId = _runId,
            Level = level,
            Message = message,
            TimestampUtc = _time.GetUtcNow().UtcDateTime,
        });
    }

    private async Task PumpAsync()
    {
        var batch = new List<TaskLogEntry>();
        while (await _pending.Reader.WaitToReadAsync())
        {
            while (batch.Count < 500 && _pending.Reader.TryRead(out var entry))
            {
                batch.Add(entry);
            }

            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                db.TaskLogs.AddRange(batch);
                await db.SaveChangesAsync();
                foreach (var entry in batch)
                {
                    _events.PublishLog(entry);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist {Count} log entries of run {RunId}", batch.Count, _runId);
            }

            batch.Clear();
        }
    }

    /// <summary>Flushes all pending entries.</summary>
    public async ValueTask DisposeAsync()
    {
        _pending.Writer.TryComplete();
        await _pump;
    }
}
