using Omnimud.Core.Text;

namespace Omnimud.Core.Sound;

/// <summary>
/// Sound as seen from one session: MSP commands from the MUD, sounds played by triggers and
/// scripts, and the client's own effects. One instance per session; disposing it stops
/// everything that session started.
/// </summary>
public interface ISessionSound : IDisposable
{
    /// <summary>Applies (or re-applies, when options change) folders, switches and volume.</summary>
    void Configure(SoundSettings settings);

    /// <summary>Set by the session: with an inactive window, sounds and music only play if the
    /// corresponding "in background" option is on.</summary>
    bool IsWindowActive { get; set; }

    /// <summary>!!SOUND / !!MUSIC. Resolves the file inside the MUD's sound folder (T= is a subfolder),
    /// downloads it when allowed, applies priorities per category and plays or stops.</summary>
    Task HandleMspAsync(SoundCommand command, CancellationToken ct = default);

    /// <summary>
    /// Sound from a trigger or script. Lookup order: the path as given, then the MUD's sound
    /// folder, then the application's sounds folder. Without extension ".wav" is assumed.
    /// loop: 1 = once, N = N times, -1 = until stopped. Returns false if the file was not found.
    /// </summary>
    bool PlayTriggerSound(string name, int loop = 1, int volume = 100, int priority = 50);

    /// <summary>Stops a sound started with <see cref="PlayTriggerSound"/>. True if it was playing.</summary>
    bool StopTriggerSound(string name);

    /// <summary>Client effects shipped with the app: "click", "pop", "url", "error". Respects EnableSounds.</summary>
    void PlayUiSound(string name);

    void StopAll();
}

public sealed record SoundSettings
{
    /// <summary>This MUD's sound folder (created on demand).</summary>
    public required string MudSoundDirectory { get; init; }
    /// <summary>Folder with the sounds shipped with the application.</summary>
    public required string AppSoundDirectory { get; init; }
    public bool EnableSounds { get; init; } = true;
    public bool EnableMusic { get; init; } = true;
    public bool PlaySoundsInBackground { get; init; } = true;
    public bool PlayMusicInBackground { get; init; } = true;
    public bool DownloadSounds { get; init; } = true;
    public bool AllowHttpDownloads { get; init; }
    /// <summary>Master volume 0-100.</summary>
    public int Volume { get; init; } = 100;
    /// <summary>Proxy for downloads; null = direct.</summary>
    public Uri? DownloadProxy { get; init; }
    /// <summary>Complete proxy decision for downloads (direct, system or manual, with credentials). Wins over <see cref="DownloadProxy"/>.</summary>
    public DownloadProxySettings? DownloadProxySettings { get; init; }
}
