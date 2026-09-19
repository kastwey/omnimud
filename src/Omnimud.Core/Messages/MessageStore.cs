using Omnimud.Core.Session;

namespace Omnimud.Core.Messages;

/// <summary>
/// The Messages box of one session, in memory only. Keeps the newest <see cref="Capacity"/>
/// messages. Numbering is the one the user hears with Ctrl+number: 1 is the most recent.
/// Thread-safe.
/// </summary>
public sealed class MessageStore
{
    public const int DefaultCapacity = 1000;

    private readonly object _gate = new();
    private readonly LinkedList<(DateTime Time, string Text, string? Channel, string? Sender)> _items = new();
    private IReadOnlyList<SessionMessage>? _snapshot;

    public MessageStore(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
    }

    public int Capacity { get; }

    public int Count
    {
        get { lock (_gate) return _items.Count; }
    }

    /// <summary>Adds a message and returns it numbered 1 (it is the most recent).</summary>
    public SessionMessage Add(DateTime time, string text, string? channel = null, string? sender = null)
    {
        lock (_gate)
        {
            _items.AddLast((time, text, channel, sender));
            while (_items.Count > Capacity)
                _items.RemoveFirst();
            _snapshot = null;
        }
        return new SessionMessage(1, time, text, channel, sender);
    }

    /// <summary>Oldest first; each message carries its current number (last one = 1).</summary>
    public IReadOnlyList<SessionMessage> Snapshot()
    {
        lock (_gate)
        {
            if (_snapshot is not null) return _snapshot;

            var result = new SessionMessage[_items.Count];
            var i = 0;
            foreach (var (time, text, channel, sender) in _items)
            {
                result[i] = new SessionMessage(_items.Count - i, time, text, channel, sender);
                i++;
            }
            return _snapshot = result;
        }
    }

    /// <summary>Message by its number (1 = most recent). Null when out of range.</summary>
    public SessionMessage? GetByNumber(int number)
    {
        var snapshot = Snapshot();
        return number >= 1 && number <= snapshot.Count ? snapshot[^number] : null;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _items.Clear();
            _snapshot = null;
        }
    }
}
