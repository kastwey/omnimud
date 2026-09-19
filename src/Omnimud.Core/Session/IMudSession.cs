using Omnimud.Core.Actions;
using Omnimud.Core.Options;

namespace Omnimud.Core.Session;

/// <summary>
/// One connection to a MUD with all its engines (telnet, line buffer, MSP, ANSI, triggers,
/// scripts, aliases, paths, messages, log). The window is only a view: it subscribes to the
/// events and forwards what the user types.
///
/// Threading: events may be raised from any thread; the view marshals to the UI thread.
/// All public members are safe to call from the UI thread and never block it.
/// </summary>
public interface IMudSession : IAsyncDisposable
{
    SessionProfile Profile { get; }
    SessionState State { get; }
    OmnimudOptions Options { get; }

    /// <summary>True while the server handles echo (password entry): input must be masked and is
    /// never stored in history, "last command" or logs.</summary>
    bool PasswordMode { get; }

    /// <summary>Silent mode (F8 / "callate" / "hablar"): only text after the "all_speak:" marker is announced.</summary>
    bool SilentMode { get; set; }

    /// <summary>Movement mode (F2): the numeric keypad and, on an empty input box, the arrows,
    /// Page Up/Down, Home and End send commands. Persisted per MUD or character.</summary>
    bool MovementMode { get; }
    Task SetMovementModeAsync(bool enabled);

    /// <summary>Effective command of every movement key that has one (<see cref="MovementKey"/> code → command):
    /// what the character or MUD configured, else the default for the UI language. A snapshot that is
    /// replaced on reload; safe to read from the UI thread.</summary>
    IReadOnlyDictionary<int, string> Movements { get; }

    /// <summary>True if the key (<see cref="MovementKey"/> code) would send a command. The view asks
    /// before swallowing a key press: a key without command keeps its normal behaviour.</summary>
    bool HasMovement(int key);

    /// <summary>The view tells the session whether its window is active: speech only happens when
    /// it is, and background sound/music and window flashing depend on it.</summary>
    bool IsWindowActive { get; set; }

    /// <summary>When the connection was established (for the "connected time" status panel).</summary>
    DateTime? ConnectedSince { get; }

    IReadOnlyList<SessionMessage> Messages { get; }
    IReadOnlyList<string> History { get; }

    /// <summary>What the MUD says can be done right now (GMCP Char.Inventory, Room.Info...), as a tree of
    /// menus. Never null; empty when the MUD sends nothing of the kind and after a disconnection. An
    /// immutable snapshot that is replaced as a whole: safe to read from the UI thread.</summary>
    ActionMenu ActionMenu { get; }

    // ── Lifecycle ──────────────────────────────────────────────────────────
    /// <summary>Loads options, aliases, triggers, paths, directions, movements and message rules.</summary>
    Task InitializeAsync(CancellationToken ct = default);
    /// <summary>Connects and runs the login script. Throws on failure; the view then offers offline mode.</summary>
    Task ConnectAsync(CancellationToken ct = default);
    void EnterOfflineMode();
    /// <summary>Optionally sends save + quit commands (per options), waits briefly, disconnects and closes the log.</summary>
    Task CloseAsync(bool sendSaveAndQuit, CancellationToken ct = default);
    /// <summary>Reloads aliases, triggers, paths, directions, movements, rules and options after the user edited them.</summary>
    Task ReloadAsync(CancellationToken ct = default);

    // ── Input ──────────────────────────────────────────────────────────────
    /// <summary>What the user typed and confirmed with Enter. Empty text repeats the last command
    /// (or sends a blank line if there is none). Adds to history unless in password mode.</summary>
    Task SubmitInputAsync(string text);
    /// <summary>Sends an empty line (Shift+Enter / Ctrl+Enter on an empty box).</summary>
    Task SendBlankLineAsync();
    /// <summary>Runs a command through the full input pipeline without touching history
    /// (numpad movement, F3/F4, trigger actions).</summary>
    Task ExecuteCommandAsync(string command);
    /// <summary>Movement key (<see cref="MovementKey"/> code, 0-17) pressed in movement mode. Runs its
    /// effective command through the full input pipeline, without history. Returns false if it has none.</summary>
    Task<bool> ExecuteMovementAsync(int key);
    /// <summary>The user chose an entry of <see cref="ActionMenu"/>: its command goes through the input pipeline
    /// (aliases, paths, concatenation...) without history. The command was written by the MUD, so the client's
    /// own commands (calias, -triggers, cls...) are not obeyed: an action can only send text to the MUD.
    /// Nodes without command (submenus, information) do nothing.</summary>
    Task ExecuteActionAsync(ActionMenuNode node);
    Task SendSaveCommandAsync();
    Task SendQuitCommandAsync();

    // ── Events ─────────────────────────────────────────────────────────────
    event Action<SessionLine>? LineReceived;
    event Action<SessionMessage>? MessageAdded;
    /// <summary><see cref="ActionMenu"/> was replaced. Nothing is announced: the menu is there for when the user wants it.</summary>
    event Action? ActionMenuChanged;
    /// <summary>Text for the screen reader. Never contains ANSI sequences. Already filtered by
    /// window-active, silent mode, "all_speak:" and the announce options.</summary>
    event Action<string, AnnouncePriority>? Announce;
    event Action<SessionState>? StateChanged;
    event Action<bool>? PasswordModeChanged;
    event Action<bool>? MovementModeChanged;
    event Action<bool>? SilentModeChanged;
    /// <summary>"cls": clear the received-text box.</summary>
    event Action? ClearRequested;
    event Action<SessionWindow, string?>? WindowRequested;
    /// <summary>Text arrived while the window was inactive and the option is on.</summary>
    event Action? FlashRequested;
    /// <summary>A short status text for the status bar (om.status, recording state...).</summary>
    event Action<string>? StatusChanged;
    /// <summary>Asks the user a yes/no question (e.g. cancel path recording). Default answer when nobody listens: yes.</summary>
    event Func<string, bool>? ConfirmRequested;
    /// <summary>Client sound effects: "click" (history), "pop" (direction recorded), "url", "error".</summary>
    event Action<string>? UiSoundRequested;
}
