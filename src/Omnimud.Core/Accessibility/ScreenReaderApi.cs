using Omnimud.Core.Accessibility.Readers;

namespace Omnimud.Core.Accessibility;

/// <summary>
/// Unified facade over all supported screen readers. Detects which reader is currently
/// running and delegates speech/braille calls to it. If no reader is available, calls
/// become no-ops and <see cref="IsAvailable"/> returns false.
/// </summary>
public sealed class ScreenReaderApi
{
    private readonly IReadOnlyList<IScreenReader> _readers;
    private IScreenReader? _active;
    private DateTime _lastProbe = DateTime.MinValue;
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(2);

    /// <summary>Creates the facade with the default set of readers (JAWS, NVDA).</summary>
    public ScreenReaderApi()
        : this(new IScreenReader[] { new JawsScreenReader(), new NvdaScreenReader() })
    {
    }

    /// <summary>Creates the facade with a custom ordered list of readers (first match wins).</summary>
    public ScreenReaderApi(IReadOnlyList<IScreenReader> readers)
    {
        _readers = readers;
        Refresh();
    }

    /// <summary>Returns the active reader, or null if none is running.</summary>
    public IScreenReader? Active
    {
        get
        {
            if (DateTime.UtcNow - _lastProbe > ProbeInterval)
                Refresh();
            return _active;
        }
    }

    /// <summary>True when at least one screen reader is running.</summary>
    public bool IsAvailable => Active is not null;

    /// <summary>Name of the active reader, or "None".</summary>
    public string ActiveName => Active?.Name ?? "None";

    /// <summary>When true, all <see cref="Speak"/> calls become no-ops.</summary>
    public bool Muted { get; set; }

    /// <summary>Speaks the text via the active reader. Returns false if no reader is active or when muted.</summary>
    public bool Speak(string text, bool interrupt = true)
    {
        if (Muted || string.IsNullOrWhiteSpace(text)) return false;
        return Active?.Speak(text, interrupt) ?? false;
    }

    /// <summary>Stops current speech on the active reader.</summary>
    public bool StopSpeech() => Active?.StopSpeech() ?? false;

    /// <summary>Sends text to the braille display of the active reader, if supported.</summary>
    public bool Braille(string text) => Active?.Braille(text) ?? false;

    /// <summary>Re-probes all readers and updates the active one.</summary>
    public void Refresh()
    {
        _lastProbe = DateTime.UtcNow;
        foreach (var reader in _readers)
        {
            if (reader.IsRunning)
            {
                _active = reader;
                return;
            }
        }
        _active = null;
    }
}
