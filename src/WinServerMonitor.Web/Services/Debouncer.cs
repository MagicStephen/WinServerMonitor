namespace WinServerMonitor.Web.Services;

/// <summary>
/// Coalesces bursts of events (e.g. many runs changing at once) into a single refresh
/// executed <see cref="Delay"/> after the first event.
/// </summary>
public sealed class Debouncer(TimeSpan delay) : IDisposable
{
    private readonly CancellationTokenSource _disposed = new();
    private int _pending;

    public TimeSpan Delay { get; } = delay;

    public void Trigger(Func<Task> action)
    {
        if (Interlocked.Exchange(ref _pending, 1) == 1)
        {
            return;
        }

        _ = RunAsync(action);
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await Task.Delay(Delay, _disposed.Token);
            Interlocked.Exchange(ref _pending, 0);
            await action();
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
            // component was disposed meanwhile
        }
    }

    public void Dispose()
    {
        _disposed.Cancel();
        _disposed.Dispose();
    }
}
