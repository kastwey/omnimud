using System.Diagnostics;
using System.Threading.Channels;

namespace Omnimud.Core.Session;

/// <summary>
/// Runs asynchronous work items one at a time, in order. It is how the session serializes its
/// state: network data, timers, scripts and the UI all post here and nobody ever blocks.
/// Callers must never wait synchronously for an item from inside another item.
/// </summary>
internal sealed class SerialQueue : IAsyncDisposable
{
    private readonly Channel<Func<Task>> _channel = Channel.CreateUnbounded<Func<Task>>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
    private readonly Task _loop;

    public SerialQueue()
    {
        _loop = Task.Run(RunAsync);
    }

    /// <summary>Queues work; the task completes (or faults) when the work has run.</summary>
    public Task Enqueue(Func<Task> work)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = _channel.Writer.TryWrite(async () =>
        {
            try
            {
                await work().ConfigureAwait(false);
                tcs.TrySetResult();
            }
            catch (OperationCanceledException ex)
            {
                tcs.TrySetCanceled(ex.CancellationToken);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        if (!accepted) tcs.TrySetResult(); // disposed: nothing left to do
        return tcs.Task;
    }

    public Task<T> Enqueue<T>(Func<Task<T>> work, T whenDisposed)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = _channel.Writer.TryWrite(async () =>
        {
            try
            {
                tcs.TrySetResult(await work().ConfigureAwait(false));
            }
            catch (OperationCanceledException ex)
            {
                tcs.TrySetCanceled(ex.CancellationToken);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        if (!accepted) tcs.TrySetResult(whenDisposed);
        return tcs.Task;
    }

    /// <summary>Fire and forget. Exceptions are reported to <paramref name="onError"/> and never escape.</summary>
    public void Post(Func<Task> work, Action<Exception>? onError = null)
    {
        _channel.Writer.TryWrite(async () =>
        {
            try
            {
                await work().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                try { onError?.Invoke(ex); }
                catch (Exception inner) { Debug.WriteLine(inner); }
            }
        });
    }

    /// <summary>Completes when everything queued so far has run.</summary>
    public Task WhenIdleAsync() => Enqueue(static () => Task.CompletedTask);

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        await _loop.ConfigureAwait(false);
    }

    private async Task RunAsync()
    {
        await foreach (var item in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                await item().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Items wrap their own work; this is only a last line of defence for the loop.
                Debug.WriteLine(ex);
            }
        }
    }
}
