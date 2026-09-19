using System.Text;

namespace Omnimud.Core.Text;

/// <summary>
/// Turns a stream of decoded text into complete lines. Text without a trailing newline stays
/// pending; when nothing else arrives for the prompt delay, <see cref="PromptTimeout"/> fires
/// and the owner takes the pending text as a prompt. Empty lines are kept. Backspaces delete
/// the previous character of the line being built and carriage returns are dropped.
/// </summary>
public sealed class LineBuffer : IDisposable
{
    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private readonly Func<TimeSpan> _promptDelay;
    private readonly StringBuilder _pending = new();
    private readonly ITimer _timer;
    private long _generation;
    private long _lastAppendTimestamp;
    private TimeSpan _armedDelay;
    private bool _disposed;

    public LineBuffer(TimeProvider time, Func<TimeSpan> promptDelay)
    {
        _time = time;
        _promptDelay = promptDelay;
        _timer = time.CreateTimer(OnTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Raised from a timer thread when pending text has been waiting for the prompt delay. The
    /// argument is a generation token for <see cref="TakePendingIf"/>: if more text arrived in
    /// the meantime the token is stale and nothing is taken.
    /// </summary>
    public event Action<long>? PromptTimeout;

    public bool HasPending
    {
        get { lock (_gate) return _pending.Length > 0; }
    }

    /// <summary>Adds text and returns the lines it completed (without the newline).</summary>
    public IReadOnlyList<string> Append(string text)
    {
        lock (_gate)
        {
            List<string>? lines = null;

            foreach (var c in text)
            {
                switch (c)
                {
                    case '\n':
                        (lines ??= []).Add(_pending.ToString());
                        _pending.Clear();
                        break;
                    case '\r':
                        break;
                    case '\b':
                        if (_pending.Length > 0) _pending.Length--;
                        break;
                    default:
                        _pending.Append(c);
                        break;
                }
            }

            _generation++;
            _lastAppendTimestamp = _time.GetTimestamp();
            Arm();
            return (IReadOnlyList<string>?)lines ?? [];
        }
    }

    /// <summary>Takes the pending text only if nothing was appended since the timeout fired.</summary>
    public string? TakePendingIf(long generation)
    {
        lock (_gate)
            return generation == _generation ? TakePendingCore() : null;
    }

    /// <summary>Takes whatever is pending (GA/EOR received, disconnection...). Null if nothing.</summary>
    public string? TakePending()
    {
        lock (_gate)
            return TakePendingCore();
    }

    public void Reset()
    {
        lock (_gate)
        {
            _pending.Clear();
            _generation++;
            Arm();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _timer.Dispose();
    }

    private string? TakePendingCore()
    {
        if (_pending.Length == 0) return null;
        var text = _pending.ToString();
        _pending.Clear();
        _generation++;
        Arm();
        return text;
    }

    private void Arm()
    {
        if (_disposed) return;
        if (_pending.Length == 0)
        {
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }

        var delay = _promptDelay();
        if (delay < TimeSpan.FromMilliseconds(1)) delay = TimeSpan.FromMilliseconds(1);
        _armedDelay = delay;
        _timer.Change(delay, Timeout.InfiniteTimeSpan);
    }

    private void OnTimer(object? state)
    {
        long generation;
        lock (_gate)
        {
            if (_disposed || _pending.Length == 0) return;
            // A callback already in flight when the timer was re-armed is stale. Half the delay
            // is margin enough and tolerates timers that fire a little early.
            if (_time.GetElapsedTime(_lastAppendTimestamp) < _armedDelay / 2) return;
            generation = _generation;
        }
        PromptTimeout?.Invoke(generation);
    }
}
