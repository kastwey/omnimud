using Omnimud.Core.Text;

namespace Omnimud.Core.Sound;

/// <summary>
/// Plays sound/music files and mixes any number of them. One instance is shared by every
/// session, so callers that must not disturb each other address their playbacks by handle.
/// Implementations never throw to the caller: a file that cannot be played yields handle 0.
/// </summary>
public interface ISoundPlayer : IDisposable
{
    /// <summary>
    /// Starts playing and returns a handle (&gt; 0) that identifies this playback, or 0 if
    /// nothing could be started (missing or corrupt file, no audio device).
    /// </summary>
    long Play(SoundPlayRequest request);

    /// <summary>Stops one playback. Unknown or finished handles are ignored.</summary>
    void Stop(long handle);

    /// <summary>Stops every playback of a file (any owner).</summary>
    void Stop(string fileName);

    /// <summary>Stops all currently playing sounds/music (any owner).</summary>
    void StopAll();

    /// <summary>Stops all sounds of a given type (any owner).</summary>
    void StopByType(SoundType type);

    /// <summary>Changes the volume (0-100) of a playback that is already running.</summary>
    void SetVolume(long handle, int volume);

    /// <summary>True while the playback has neither finished nor been stopped.</summary>
    bool IsPlaying(long handle);

    /// <summary>Gets or sets the master volume (0-100), applied on top of each playback's volume.</summary>
    int MasterVolume { get; set; }

    /// <summary>
    /// Raised once per playback when it ends for any reason: it reached its end, it was stopped
    /// or it failed. May be raised from any thread, never while the player holds an internal lock
    /// and never from inside <see cref="Play"/>. A player shared by several sessions should raise
    /// it from the thread pool rather than from inside Stop: listeners take their own locks.
    /// </summary>
    event EventHandler<PlaybackEndedEventArgs>? PlaybackEnded;
}

public enum PlaybackEndReason
{
    Completed,
    Stopped,
    Failed
}

public sealed class PlaybackEndedEventArgs(long handle, string filePath, SoundType type, PlaybackEndReason reason) : EventArgs
{
    public long Handle { get; } = handle;
    public string FilePath { get; } = filePath;
    public SoundType Type { get; } = type;
    public PlaybackEndReason Reason { get; } = reason;
}

/// <summary>
/// Request to play a sound file.
/// </summary>
public sealed record SoundPlayRequest
{
    public required string FilePath { get; init; }
    public SoundType Type { get; init; } = SoundType.Sound;
    /// <summary>0-100, before the player's master volume.</summary>
    public int Volume { get; init; } = 100;
    /// <summary>1 = once, N = N times, -1 = until stopped.</summary>
    public int Loop { get; init; } = 1;
    public int Priority { get; init; } = 50;
    public bool Continue { get; init; }
}
