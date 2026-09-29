using System.Threading.Channels;

namespace WinServerMonitor.Infrastructure.Execution;

/// <summary>In-memory queue of run ids waiting for a free worker. The database stays the source of truth.</summary>
public sealed class TaskQueue
{
    private readonly Channel<long> _channel = Channel.CreateUnbounded<long>(new UnboundedChannelOptions
    {
        SingleReader = false,
        SingleWriter = false,
    });

    public void Enqueue(long runId) => _channel.Writer.TryWrite(runId);

    public ValueTask<long> DequeueAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAsync(cancellationToken);

    public int Count => _channel.Reader.Count;
}
