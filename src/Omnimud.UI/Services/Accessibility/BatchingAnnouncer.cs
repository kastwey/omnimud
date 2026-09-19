using System.Text;
using Omnimud.Core.Options;
using Omnimud.Core.Session;

namespace Omnimud.UI.Services.Accessibility;

/// <summary>
/// Groups the MUD lines that arrive together into ONE announcement.
///
/// A screen of MUD text is many lines in a few milliseconds. Raising one UI Automation
/// notification per line floods the screen reader, which coalesces events and silently drops
/// roughly every other line. The original client never had this problem because it spoke each
/// received block as a whole; this restores that behaviour.
///
/// Only queued text is batched. Anything more urgent first flushes what is pending, so the
/// order of what the user hears is always the order in which things happened.
/// </summary>
public sealed class BatchingAnnouncer : IAnnouncer, IDisposable
{
    /// <summary>Quiet time after the last line before the batch is spoken.</summary>
    public static readonly TimeSpan DefaultQuietTime = TimeSpan.FromMilliseconds(60);

    /// <summary>A batch is never held longer than this, even if text keeps pouring in.</summary>
    public static readonly TimeSpan DefaultMaxDelay = TimeSpan.FromMilliseconds(300);

    private readonly IAnnouncer _inner;
    private readonly TimeProvider _time;
    private readonly TimeSpan _quietTime;
    private readonly TimeSpan _maxDelay;
    private readonly object _gate = new();
    private readonly StringBuilder _pending = new();
    private readonly ITimer _timer;
    private long _batchStartedAt;
    private bool _disposed;

    public BatchingAnnouncer(IAnnouncer inner, TimeProvider? time = null, TimeSpan? quietTime = null, TimeSpan? maxDelay = null)
    {
        _inner = inner;
        _time = time ?? TimeProvider.System;
        _quietTime = quietTime ?? DefaultQuietTime;
        _maxDelay = maxDelay ?? DefaultMaxDelay;
        _timer = _time.CreateTimer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public ScreenReaderMode Mode
    {
        get => _inner.Mode;
        set => _inner.Mode = value;
    }

    public bool Muted
    {
        get => _inner.Muted;
        set
        {
            if (value) Discard();
            _inner.Muted = value;
        }
    }

    public bool Announce(string text, AnnouncePriority priority)
    {
        if (priority != AnnouncePriority.Queue)
        {
            Flush();
            return _inner.Announce(text, priority);
        }

        if (string.IsNullOrWhiteSpace(text)) return false;

        lock (_gate)
        {
            if (_disposed || _inner.Muted) return false;

            if (_pending.Length == 0)
                _batchStartedAt = _time.GetTimestamp();
            else
                _pending.Append('\n');
            _pending.Append(text);

            // Wait for the burst to end, but never past the maximum delay.
            var remaining = _maxDelay - _time.GetElapsedTime(_batchStartedAt);
            var due = remaining < _quietTime ? remaining : _quietTime;
            _timer.Change(due > TimeSpan.Zero ? due : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }
        return true;
    }

    public void StopSpeech()
    {
        Discard();
        _inner.StopSpeech();
    }

    /// <summary>Speaks whatever is pending right now.</summary>
    public void Flush()
    {
        string text;
        lock (_gate)
        {
            if (_pending.Length == 0) return;
            text = _pending.ToString();
            _pending.Clear();
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
        _inner.Announce(text, AnnouncePriority.Queue);
    }

    private void Discard()
    {
        lock (_gate)
        {
            _pending.Clear();
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _pending.Clear();
        }
        _timer.Dispose();
    }
}
