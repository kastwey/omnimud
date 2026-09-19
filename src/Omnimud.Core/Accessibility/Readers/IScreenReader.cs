namespace Omnimud.Core.Accessibility.Readers;

/// <summary>
/// Common abstraction over a concrete screen reader (JAWS, NVDA, ...).
/// Implementations wrap the native API of a specific reader.
/// </summary>
public interface IScreenReader
{
    /// <summary>Friendly name of the reader (e.g. "JAWS", "NVDA").</summary>
    string Name { get; }

    /// <summary>True if the reader is currently running and reachable.</summary>
    bool IsRunning { get; }

    /// <summary>Speaks <paramref name="text"/>. When <paramref name="interrupt"/> is true,
    /// any current speech is interrupted; otherwise the text is queued.</summary>
    bool Speak(string text, bool interrupt);

    /// <summary>Stops all current speech.</summary>
    bool StopSpeech();

    /// <summary>Displays text on a braille line, if supported. Returns false when unsupported.</summary>
    bool Braille(string text);
}
