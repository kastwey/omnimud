namespace Omnimud.Core.Session;

/// <summary>
/// A MUD that reports a channel message through GMCP (Comm.Channel.Text) usually also prints it
/// as ordinary text. Without care the user would hear it twice (once as received text, once as a
/// message) and, if the MUD also has a message rule, see it twice in the Messages box.
///
/// The two copies arrive within moments of each other, in either order. This remembers what was
/// just seen on each side so the second copy can be recognised. A copy only counts as the same
/// message when the text line contains the GMCP text and, when there is one, the talker's name:
/// that keeps a short "ok" on a channel from silencing an unrelated line.
/// </summary>
public sealed class GmcpEchoFilter(TimeProvider time)
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(3);
    private const int Capacity = 32;

    private readonly List<(string Text, string Sender, long At)> _gmcp = [];
    private readonly List<(string Line, long At)> _spokenLines = [];

    /// <summary>A GMCP channel message arrived. Returns true if its text was already spoken as a MUD line.</summary>
    public bool NoteGmcpMessage(string text, string? sender)
    {
        text = Normalize(text);
        var who = Normalize(sender);
        if (text.Length == 0) return false;

        Prune();
        var index = _spokenLines.FindIndex(l => IsSame(l.Line, text, who));
        if (index >= 0)
        {
            _spokenLines.RemoveAt(index);
            return true;
        }

        Remember(_gmcp, (text, who, time.GetTimestamp()));
        return false;
    }

    /// <summary>True if this MUD line is the text copy of a GMCP message that was just handled.</summary>
    public bool IsEchoOfGmcpMessage(string plainLine)
    {
        var line = Normalize(plainLine);
        if (line.Length == 0) return false;

        Prune();
        var index = _gmcp.FindIndex(g => IsSame(line, g.Text, g.Sender));
        if (index < 0) return false;
        _gmcp.RemoveAt(index);
        return true;
    }

    /// <summary>A MUD line was spoken; a GMCP copy arriving right after must stay silent.</summary>
    public void NoteSpokenLine(string plainLine)
    {
        var line = Normalize(plainLine);
        if (line.Length > 0) Remember(_spokenLines, (line, time.GetTimestamp()));
    }

    public void Reset()
    {
        _gmcp.Clear();
        _spokenLines.Clear();
    }

    private static bool IsSame(string line, string text, string sender) =>
        line.Contains(text, StringComparison.OrdinalIgnoreCase) &&
        (sender.Length == 0 ? text.Length >= 8 : line.Contains(sender, StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static void Remember<T>(List<T> list, T item)
    {
        if (list.Count >= Capacity) list.RemoveAt(0);
        list.Add(item);
    }

    private void Prune()
    {
        _gmcp.RemoveAll(g => time.GetElapsedTime(g.At) > Window);
        _spokenLines.RemoveAll(l => time.GetElapsedTime(l.At) > Window);
    }
}
