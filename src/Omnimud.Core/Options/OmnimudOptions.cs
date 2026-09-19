namespace Omnimud.Core.Options;

/// <summary>
/// Every user option, typed. One instance is the fully resolved set for a scope
/// (character → MUD → global → these defaults).
/// Persisted as key/value pairs where the key is the property name and the value is the
/// invariant-culture string form; see <see cref="OptionsSerializer"/>.
/// </summary>
public sealed record OmnimudOptions
{
    // ── General ────────────────────────────────────────────────────────────
    public ScreenReaderMode ScreenReader { get; init; } = ScreenReaderMode.Automatic;
    public CursorBehavior CursorOnReceived { get; init; } = CursorBehavior.FollowIfAtEnd;
    public CursorBehavior CursorOnMessages { get; init; } = CursorBehavior.FollowIfAtEnd;
    /// <summary>Number of commands kept in the input history.</summary>
    public int HistorySize { get; init; } = 50;
    public bool ConfirmBeforeExit { get; init; } = true;
    /// <summary>On close: send the MUD's save command, then its quit command, then disconnect.</summary>
    public bool TrySaveBeforeExit { get; init; } = true;
    public bool TelnetNegotiation { get; init; } = true;
    /// <summary>Empty = follow Windows; otherwise a culture name such as "es" or "en".</summary>
    public string Language { get; init; } = string.Empty;
    public bool AnnounceMudText { get; init; } = true;
    public bool AnnounceMessages { get; init; } = true;
    public bool FlashWindow { get; init; } = true;
    /// <summary>Lines kept in the received-text box.</summary>
    public int MaxLines { get; init; } = 10_000;
    /// <summary>Milliseconds to wait for a newline before treating pending text as a prompt.</summary>
    public int PromptFlushMilliseconds { get; init; } = 150;

    // ── Logs ───────────────────────────────────────────────────────────────
    public LogMode LogType { get; init; } = LogMode.PerDay;
    /// <summary>Null or empty = the default logs folder inside the data directory.</summary>
    public string? LogDirectory { get; init; }

    // ── Sound ──────────────────────────────────────────────────────────────
    public bool EnableSounds { get; init; } = true;
    public bool EnableMusic { get; init; } = true;
    public bool PlaySoundsInBackground { get; init; } = true;
    public bool PlayMusicInBackground { get; init; } = true;
    public bool DownloadSounds { get; init; } = true;
    /// <summary>Old MUDs publish sounds over plain HTTP. HTTPS is always tried first.</summary>
    public bool AllowHttpDownloads { get; init; }
    public int Volume { get; init; } = 100;

    // ── Connection ─────────────────────────────────────────────────────────
    public ProxyMode ProxyType { get; init; } = ProxyMode.Disabled;
    public string? ProxyHost { get; init; }
    public int ProxyPort { get; init; }
    /// <summary>The original client only proxied HTTP. When true the MUD connection is proxied too.</summary>
    public bool UseProxyForMud { get; init; }
    public ProxyProtocol ProxyProtocol { get; init; } = ProxyProtocol.Socks5;
    /// <summary>User name for the proxy (manual or system); null or empty = the proxy needs no authentication.</summary>
    public string? ProxyUsername { get; init; }
    /// <summary>
    /// The proxy password, NEVER in clear: Base64 of what <see cref="Security.IPasswordProtector.Protect"/> returns.
    /// Written and read only through <see cref="Security.IProxyCredentialStore"/>. It is tied to the master key of
    /// this installation, so it is left out of exported files (<see cref="OptionsSerializer.SecretKeys"/>) and
    /// masked in <see cref="ToString"/>.
    /// </summary>
    public string? ProxyPasswordProtected { get; init; }

    // ── Appearance ─────────────────────────────────────────────────────────
    public string FontFamily { get; init; } = "Consolas";
    public float FontSize { get; init; } = 10f;

    // ── Special characters ─────────────────────────────────────────────────
    public bool UseConcatChar { get; init; }
    public char ConcatChar { get; init; } = ';';
    public bool UseRepeatChar { get; init; }
    public char RepeatChar { get; init; } = '#';

    public static OmnimudOptions Default { get; } = new();

    /// <summary>The record's usual text, with the protected password masked: option blocks end up in logs and error reports.</summary>
    public override string ToString()
    {
        var builder = new System.Text.StringBuilder(nameof(OmnimudOptions)).Append(" { ");
        (ProxyPasswordProtected is null ? this : this with { ProxyPasswordProtected = "***" }).PrintMembers(builder);
        return builder.Append(" }").ToString();
    }
}

public enum ScreenReaderMode
{
    /// <summary>UI Automation notifications, falling back to the native screen reader libraries.</summary>
    Automatic,
    Jaws,
    Nvda,
    None
}

public enum CursorBehavior
{
    /// <summary>Always jump to the end when text arrives.</summary>
    GoToEnd,
    /// <summary>Never move the caret or the selection.</summary>
    Keep,
    /// <summary>Jump to the end only if the caret was already at the end (default).</summary>
    FollowIfAtEnd
}

public enum LogMode
{
    None,
    PerDay,
    PerSession
}

public enum ProxyMode
{
    Disabled,
    /// <summary>Use the system proxy.</summary>
    Automatic,
    Manual
}

public enum ProxyProtocol
{
    Socks5,
    HttpConnect
}

public enum OptionScope
{
    Global = 0,
    Mud = 1,
    Character = 2
}
