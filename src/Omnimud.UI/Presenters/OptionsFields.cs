using Omnimud.Core.Options;

namespace Omnimud.UI.Presenters;

/// <summary>Every editable field of the options dialog. Used to tell the view where a validation error is.</summary>
public enum OptionsField
{
    ScreenReader,
    CursorOnReceived,
    CursorOnMessages,
    HistorySize,
    ConfirmBeforeExit,
    TrySaveBeforeExit,
    TelnetNegotiation,
    Language,
    AnnounceMudText,
    AnnounceMessages,
    FlashWindow,
    MaxLines,
    PromptFlushMilliseconds,
    LogType,
    LogDirectory,
    EnableSounds,
    EnableMusic,
    PlaySoundsInBackground,
    PlayMusicInBackground,
    DownloadSounds,
    AllowHttpDownloads,
    Volume,
    ProxyType,
    ProxyHost,
    ProxyPort,
    UseProxyForMud,
    ProxyProtocol,
    ProxyUsername,
    /// <summary>The password box (named after the option it feeds; the box itself only ever holds a NEW password).</summary>
    ProxyPasswordProtected,
    /// <summary>The "delete the stored proxy password" box.</summary>
    ClearProxyPassword,
    FontFamily,
    FontSize,
    UseConcatChar,
    ConcatChar,
    UseRepeatChar,
    RepeatChar
}

/// <summary>
/// The options as the dialog edits them: the same data as <see cref="OmnimudOptions"/>, but with the
/// free-text boxes as the text the user typed (which may be wrong until validated).
/// </summary>
public sealed record OptionsFields
{
    // General
    public ScreenReaderMode ScreenReader { get; set; }
    public CursorBehavior CursorOnReceived { get; set; }
    public CursorBehavior CursorOnMessages { get; set; }
    public int HistorySize { get; set; }
    public bool ConfirmBeforeExit { get; set; }
    public bool TrySaveBeforeExit { get; set; }
    public bool TelnetNegotiation { get; set; }
    /// <summary>"" = automatic, "es", "en".</summary>
    public string Language { get; set; } = string.Empty;
    public bool AnnounceMudText { get; set; }
    public bool AnnounceMessages { get; set; }
    public bool FlashWindow { get; set; }
    public int MaxLines { get; set; }
    public int PromptFlushMilliseconds { get; set; }

    // Logs
    public LogMode LogType { get; set; }
    /// <summary>Empty = the default folder.</summary>
    public string LogDirectory { get; set; } = string.Empty;

    // Sounds
    public bool EnableSounds { get; set; }
    public bool EnableMusic { get; set; }
    public bool PlaySoundsInBackground { get; set; }
    public bool PlayMusicInBackground { get; set; }
    public bool DownloadSounds { get; set; }
    public bool AllowHttpDownloads { get; set; }
    public int Volume { get; set; }

    // Connection
    public ProxyMode ProxyType { get; set; }
    public string ProxyHost { get; set; } = string.Empty;
    public int ProxyPort { get; set; }
    public bool UseProxyForMud { get; set; }
    public ProxyProtocol ProxyProtocol { get; set; }
    public string ProxyUsername { get; set; } = string.Empty;
    /// <summary>What the user typed in the password box. Never filled from the store: empty = keep the stored one.</summary>
    public string NewProxyPassword { get; set; } = string.Empty;
    /// <summary>The "delete the stored password" box. Only matters while <see cref="NewProxyPassword"/> is empty.</summary>
    public bool ClearProxyPassword { get; set; }
    /// <summary>The stored password exactly as it is kept (protected, opaque). The dialog carries it along and never shows it.</summary>
    public string? ProxyPasswordProtected { get; set; }

    // Appearance
    public string FontFamily { get; set; } = string.Empty;
    public float FontSize { get; set; }

    // Special characters
    public bool UseConcatChar { get; set; }
    /// <summary>Text of the box: valid when it is exactly one character.</summary>
    public string ConcatChar { get; set; } = string.Empty;
    public bool UseRepeatChar { get; set; }
    public string RepeatChar { get; set; } = string.Empty;

    // ── Dependent controls ─────────────────────────────────────────────────
    public bool LogDirectoryEnabled => LogType != LogMode.None;
    public bool SoundsBackgroundEnabled => EnableSounds;
    public bool MusicBackgroundEnabled => EnableMusic;
    public bool AllowHttpEnabled => DownloadSounds;
    /// <summary>Host, port and protocol: only when the proxy is entered by hand.</summary>
    public bool ProxyDetailsEnabled => ProxyType == ProxyMode.Manual;
    public bool ProxyForMudEnabled => ProxyType != ProxyMode.Disabled;
    /// <summary>User and password: manual and automatic (the proxy of the system may ask for them too).</summary>
    public bool ProxyCredentialsEnabled => ProxyType != ProxyMode.Disabled;
    public bool HasStoredProxyPassword => !string.IsNullOrWhiteSpace(ProxyPasswordProtected);
    public bool ClearProxyPasswordEnabled => ProxyCredentialsEnabled && HasStoredProxyPassword;
    public bool ConcatCharEnabled => UseConcatChar;
    public bool RepeatCharEnabled => UseRepeatChar;

    public static OptionsFields From(OmnimudOptions o)
    {
        ArgumentNullException.ThrowIfNull(o);
        return new OptionsFields
        {
            ScreenReader = o.ScreenReader,
            CursorOnReceived = o.CursorOnReceived,
            CursorOnMessages = o.CursorOnMessages,
            HistorySize = o.HistorySize,
            ConfirmBeforeExit = o.ConfirmBeforeExit,
            TrySaveBeforeExit = o.TrySaveBeforeExit,
            TelnetNegotiation = o.TelnetNegotiation,
            Language = o.Language ?? string.Empty,
            AnnounceMudText = o.AnnounceMudText,
            AnnounceMessages = o.AnnounceMessages,
            FlashWindow = o.FlashWindow,
            MaxLines = o.MaxLines,
            PromptFlushMilliseconds = o.PromptFlushMilliseconds,
            LogType = o.LogType,
            LogDirectory = o.LogDirectory ?? string.Empty,
            EnableSounds = o.EnableSounds,
            EnableMusic = o.EnableMusic,
            PlaySoundsInBackground = o.PlaySoundsInBackground,
            PlayMusicInBackground = o.PlayMusicInBackground,
            DownloadSounds = o.DownloadSounds,
            AllowHttpDownloads = o.AllowHttpDownloads,
            Volume = o.Volume,
            ProxyType = o.ProxyType,
            ProxyHost = o.ProxyHost ?? string.Empty,
            ProxyPort = o.ProxyPort,
            UseProxyForMud = o.UseProxyForMud,
            ProxyProtocol = o.ProxyProtocol,
            ProxyUsername = o.ProxyUsername ?? string.Empty,
            ProxyPasswordProtected = o.ProxyPasswordProtected,
            FontFamily = o.FontFamily ?? string.Empty,
            FontSize = o.FontSize,
            UseConcatChar = o.UseConcatChar,
            ConcatChar = o.ConcatChar.ToString(),
            UseRepeatChar = o.UseRepeatChar,
            RepeatChar = o.RepeatChar.ToString()
        };
    }

    /// <summary>
    /// The complete block to store. Call it with validated fields; a character box that is switched off
    /// and does not hold exactly one character keeps the character of <paramref name="previous"/>.
    /// The proxy password comes out as it was stored, or removed when <see cref="ClearProxyPassword"/> is set; a newly
    /// typed <see cref="NewProxyPassword"/> is NOT here: only <see cref="OptionsEditorModel"/> can protect it.
    /// </summary>
    public OmnimudOptions ToOptions(OmnimudOptions? previous = null)
    {
        previous ??= OmnimudOptions.Default;
        return new OmnimudOptions
        {
            ScreenReader = ScreenReader,
            CursorOnReceived = CursorOnReceived,
            CursorOnMessages = CursorOnMessages,
            HistorySize = HistorySize,
            ConfirmBeforeExit = ConfirmBeforeExit,
            TrySaveBeforeExit = TrySaveBeforeExit,
            TelnetNegotiation = TelnetNegotiation,
            Language = (Language ?? string.Empty).Trim(),
            AnnounceMudText = AnnounceMudText,
            AnnounceMessages = AnnounceMessages,
            FlashWindow = FlashWindow,
            MaxLines = MaxLines,
            PromptFlushMilliseconds = PromptFlushMilliseconds,
            LogType = LogType,
            LogDirectory = NullIfBlank(LogDirectory),
            EnableSounds = EnableSounds,
            EnableMusic = EnableMusic,
            PlaySoundsInBackground = PlaySoundsInBackground,
            PlayMusicInBackground = PlayMusicInBackground,
            DownloadSounds = DownloadSounds,
            AllowHttpDownloads = AllowHttpDownloads,
            Volume = Volume,
            ProxyType = ProxyType,
            ProxyHost = NullIfBlank(ProxyHost),
            ProxyPort = ProxyPort,
            UseProxyForMud = UseProxyForMud,
            ProxyProtocol = ProxyProtocol,
            ProxyUsername = NullIfBlank(ProxyUsername),
            ProxyPasswordProtected = ClearProxyPassword ? null : NullIfBlank(ProxyPasswordProtected),
            FontFamily = (FontFamily ?? string.Empty).Trim(),
            FontSize = FontSize,
            UseConcatChar = UseConcatChar,
            ConcatChar = SingleChar(ConcatChar, previous.ConcatChar),
            UseRepeatChar = UseRepeatChar,
            RepeatChar = SingleChar(RepeatChar, previous.RepeatChar)
        };
    }

    /// <summary>The record's usual text without the typed password or the stored one: fields end up in test and error output.</summary>
    public override string ToString()
    {
        var builder = new System.Text.StringBuilder(nameof(OptionsFields)).Append(" { ");
        (this with
        {
            NewProxyPassword = string.IsNullOrEmpty(NewProxyPassword) ? string.Empty : "***",
            ProxyPasswordProtected = ProxyPasswordProtected is null ? null : "***"
        }).PrintMembers(builder);
        return builder.Append(" }").ToString();
    }

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static char SingleChar(string? text, char fallback) => text is { Length: 1 } ? text[0] : fallback;
}
