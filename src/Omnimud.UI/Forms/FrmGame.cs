using System.Runtime.InteropServices;
using Omnimud.Core.Connection;
using Omnimud.Core.Session;
using Omnimud.Core.Sound;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;
using Omnimud.UI.Services.Accessibility;

namespace Omnimud.UI.Forms;

/// <summary>
/// The game window. It is only a view over <see cref="IMudSession"/>: it paints what the
/// session emits, forwards what the user types and owns everything keyboard/accessibility
/// related (labels, shortcuts, message review, announcements).
/// </summary>
public sealed partial class FrmGame : Form
{
    private readonly IMudSession _session;
    private readonly ISessionSound _sound;
    private readonly ISessionDialogs _dialogs;
    private readonly IAppDialogs _app;
    private readonly IAnnouncer _announcer;
    private readonly MessageReviewer _reviewer;

    // Messages shown in the Messages box with the character offset where each one starts.
    private readonly List<(int Start, SessionMessage Message)> _shownMessages = [];
    private int _messagesTextLength;
    private int _historyIndex = -1;
    private bool _settingInputText;
    private bool _closing;
    private bool _messagesHiddenByUser;

    /// <param name="app">Reports and help, shared with the launcher; null = a basic set that works without the container.</param>
    public FrmGame(IMudSession session, ISessionSound sound, ISessionDialogs dialogs, TimeProvider? time = null, IAnnouncer? announcer = null,
        IAppDialogs? app = null)
    {
        _app = app ?? AppDialogs.CreateBasic();
        _session = session;
        _sound = sound;
        _dialogs = dialogs;
        _reviewer = new MessageReviewer(time ?? TimeProvider.System);

        InitializeComponent();
        ApplyTexts();
        BuildMenu();
        BuildBoxMenu();

        // Notifications are raised from the window itself: it has a full WinForms UIA provider, while the
        // Received box deliberately declines native UI Automation (see AnsiTerminalBox.WndProc).
        // Lines that arrive together are spoken as one announcement; see BatchingAnnouncer.
        _announcer = new BatchingAnnouncer(announcer ?? new Announcer(new ControlUiaNotifier(this)), time);
        _rtbMessages.MaxLines = 1_000_000;

        SubscribeToSession();
        ShowState(_session.State);
        UpdateMessagesStatus();
        UpdateActionsMenu();
    }

    internal IAnnouncer Announcer => _announcer;

    private void ApplyTexts()
    {
        Text = string.Format(Strings.App_TitleWithName, _session.Profile.Title);

        _lblInput.Text = Strings.Client_InputLabel;
        _txtInput.AccessibleName = Strings.Client_InputName;

        _lblOutput.Text = Strings.Client_OutputLabel;
        _terminal.AccessibleName = Strings.Client_OutputName;

        _lblMessages.Text = Strings.Client_MessagesLabel;
        _rtbMessages.AccessibleName = Strings.Client_MessagesName;

        _btnReconnect.Text = Strings.Client_BtnReconnect;
        _btnReconnect.AccessibleName = Strings.Client_Reconnect;

        _stlConnection.AccessibleName = Strings.Client_StatusConnectionName;
        _stlTime.AccessibleName = Strings.Client_StatusTimeName;
        _stlMessages.AccessibleName = Strings.Client_StatusMessagesName;
    }

    // ── Session events (may arrive on any thread) ──────────────────────────

    private void SubscribeToSession()
    {
        _session.LineReceived += OnLineReceived;
        _session.MessageAdded += OnMessageAdded;
        _session.ActionMenuChanged += OnActionMenuChanged;
        _session.Announce += OnAnnounce;
        _session.StateChanged += OnStateChanged;
        _session.PasswordModeChanged += OnPasswordModeChanged;
        _session.MovementModeChanged += OnMovementModeChanged;
        _session.SilentModeChanged += OnSilentModeChanged;
        _session.ClearRequested += OnClearRequested;
        _session.WindowRequested += OnWindowRequested;
        _session.FlashRequested += OnFlashRequested;
        _session.StatusChanged += OnStatusChanged;
        _session.ConfirmRequested += OnConfirmRequested;
        _session.UiSoundRequested += OnUiSoundRequested;
    }

    private void UnsubscribeFromSession()
    {
        _session.LineReceived -= OnLineReceived;
        _session.MessageAdded -= OnMessageAdded;
        _session.ActionMenuChanged -= OnActionMenuChanged;
        _session.Announce -= OnAnnounce;
        _session.StateChanged -= OnStateChanged;
        _session.PasswordModeChanged -= OnPasswordModeChanged;
        _session.MovementModeChanged -= OnMovementModeChanged;
        _session.SilentModeChanged -= OnSilentModeChanged;
        _session.ClearRequested -= OnClearRequested;
        _session.WindowRequested -= OnWindowRequested;
        _session.FlashRequested -= OnFlashRequested;
        _session.StatusChanged -= OnStatusChanged;
        _session.ConfirmRequested -= OnConfirmRequested;
        _session.UiSoundRequested -= OnUiSoundRequested;
    }

    /// <summary>Runs on the UI thread, never blocking the caller. Safe while the form is closing.</summary>
    private void Ui(Action action)
    {
        if (IsDisposed || Disposing) return;
        try
        {
            if (InvokeRequired) BeginInvoke(action);
            else action();
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { } // handle not created yet or already destroyed
    }

    private void OnLineReceived(SessionLine line) => Ui(() => _terminal.AppendLine(line.Segments));

    private void OnMessageAdded(SessionMessage message) => Ui(() =>
    {
        var text = message.Text.Replace("\r\n", "\n").Replace('\r', '\n');
        var start = _messagesTextLength == 0 ? 0 : _messagesTextLength + 1;
        foreach (var part in text.Split('\n'))
            _rtbMessages.AppendPlainLine(part);
        _shownMessages.Add((start, message));
        _messagesTextLength = start + text.Length;
        UpdateMessagesStatus();
    });

    private void OnAnnounce(string text, AnnouncePriority priority) => Ui(() => _announcer.Announce(text, priority));

    private void OnStateChanged(SessionState state) => Ui(() => ShowState(state));

    private void OnPasswordModeChanged(bool passwordMode) => Ui(() =>
    {
        // A multiline TextBox ignores the password character, so the box becomes single-line meanwhile.
        _settingInputText = true;
        _txtInput.Clear();
        _txtInput.Multiline = !passwordMode;
        _txtInput.UseSystemPasswordChar = passwordMode;
        _txtInput.AccessibleName = passwordMode ? Strings.Client_PasswordName : Strings.Client_InputName;
        _settingInputText = false;
    });

    private void OnMovementModeChanged(bool enabled) => Ui(() =>
    {
        _miMovement.Checked = enabled;
        _lblInput.Font = new Font(_lblInput.Font, enabled ? FontStyle.Italic : FontStyle.Regular);
        _announcer.Announce(enabled ? Strings.Client_MovementOn : Strings.Client_MovementOff, AnnouncePriority.MostRecent);
    });

    private void OnSilentModeChanged(bool enabled) => Ui(() => _miSilent.Checked = enabled);

    private void OnClearRequested() => Ui(_terminal.Clear);

    private void OnWindowRequested(SessionWindow window, string? payload) => Ui(() =>
    {
        switch (window)
        {
            case SessionWindow.Aliases: OpenAliases(); break;
            case SessionWindow.Triggers: OpenTriggers(); break;
            case SessionWindow.Paths: OpenPaths(); break;
            case SessionWindow.NewPath: OpenDialog(needsCharacter: true, () => _dialogs.ShowNewPath(this, _session.Profile, payload ?? string.Empty)); break;
        }
    });

    private void OnFlashRequested() => Ui(() => WindowFlasher.Flash(this));

    private void OnStatusChanged(string text) => Ui(() => _stlConnection.Text = text);

    private bool OnConfirmRequested(string question)
    {
        if (IsDisposed || !IsHandleCreated) return true;
        bool Ask() => MessageBox.Show(this, question, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        try
        {
            return InvokeRequired ? (bool)Invoke(Ask) : Ask();
        }
        catch (ObjectDisposedException) { return true; }
        catch (InvalidOperationException) { return true; }
    }

    private void OnUiSoundRequested(string name) => _sound.PlayUiSound(name);

    // ── Lifecycle ──────────────────────────────────────────────────────────

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ErrorReporter.Run(this, async () =>
        {
            await _session.InitializeAsync();
            ApplyOptions();
            _miMovement.Checked = _session.MovementMode;
            await ConnectAsync();
        });
    }

    private async Task ConnectAsync()
    {
        _btnReconnect.Visible = false;
        try
        {
            await _session.ConnectAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (IsDisposed || _closing) return;
            // Proxy failures come localized and say where to fix them; never with credentials.
            MessageBox.Show(this, string.Format(Strings.Client_ConnectFailed, _session.Profile.Title, ConnectionErrorDescriber.Describe(ex)),
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);

            if (_session.Profile.CharacterId is not null &&
                MessageBox.Show(this, Strings.Client_AskOffline, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                _session.EnterOfflineMode();
            else
                ShowState(_session.State);
        }
    }

    private void ShowState(SessionState state)
    {
        var profile = _session.Profile;
        var (buttonText, buttonDescription) = state switch
        {
            SessionState.Connecting => (Strings.Client_BtnCancel, Strings.Client_BtnCancelDescription),
            SessionState.Connected => (Strings.Client_BtnDisconnect, Strings.Client_BtnDisconnectDescription),
            _ => (Strings.Client_BtnClose, Strings.Client_BtnCloseDescription),
        };
        _btnAction.Text = buttonText;
        _btnAction.AccessibleName = buttonText.Replace("&", string.Empty);
        _btnAction.AccessibleDescription = buttonDescription;

        _stlConnection.Text = state switch
        {
            SessionState.Connecting => string.Format(Strings.Client_StatusConnectingTo, profile.Title),
            SessionState.Connected when profile.CharacterName is not null =>
                string.Format(Strings.Client_StatusConnectedWith, profile.CharacterName, profile.MudName ?? profile.Title),
            SessionState.Connected => string.Format(Strings.Client_StatusConnectedTo, profile.Title),
            SessionState.Offline => string.Format(Strings.Client_StatusOffline, profile.Title),
            _ => string.Format(Strings.Client_StatusDisconnected, profile.Title),
        };

        var canType = state is SessionState.Connected or SessionState.Offline;
        _txtInput.Enabled = canType;
        _lblInput.Enabled = canType;
        _miSave.Enabled = state == SessionState.Connected && !string.IsNullOrWhiteSpace(profile.SaveCommand);
        _miQuit.Enabled = state == SessionState.Connected && !string.IsNullOrWhiteSpace(profile.QuitCommand);
        _miReconnect.Enabled = state == SessionState.Disconnected;

        _btnReconnect.Visible = state == SessionState.Disconnected && IsHandleCreated && Visible;
        _tmrSecond.Enabled = state == SessionState.Connected;
        UpdateConnectedTime();

        if (!IsHandleCreated) return;
        if (canType) _txtInput.Focus();
        else if (_btnReconnect.Visible) _btnReconnect.Focus();
    }

    private void ApplyOptions()
    {
        var options = _session.Options;
        try
        {
            var font = new Font(options.FontFamily, options.FontSize);
            _terminal.Font = font;
            _rtbMessages.Font = font;
            _txtInput.Font = font;
        }
        catch (ArgumentException)
        {
            // Unknown font family: keep the current one.
        }

        _terminal.CursorBehavior = options.CursorOnReceived;
        _terminal.MaxLines = options.MaxLines;
        _rtbMessages.CursorBehavior = options.CursorOnMessages;
        _announcer.Mode = options.ScreenReader;
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        _session.IsWindowActive = true;
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        _session.IsWindowActive = false;
    }

    private void BtnAction_Click(object? sender, EventArgs e) => Close();

    private void BtnReconnect_Click(object? sender, EventArgs e) => ErrorReporter.Run(this, ConnectAsync);

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel || _closing) return;

        // Windows is shutting down: there is no time for a graceful goodbye.
        if (e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing)
        {
            _closing = true;
            return;
        }

        var connected = _session.State == SessionState.Connected;
        if (connected && _session.Options.ConfirmBeforeExit &&
            MessageBox.Show(this, Strings.Client_ConfirmDisconnect, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        // Closing is asynchronous (save, quit, wait, disconnect): hold the window until it is done.
        e.Cancel = true;
        _closing = true;
        Enabled = false;
        ErrorReporter.Run(this, async () =>
        {
            try
            {
                await _session.CloseAsync(sendSaveAndQuit: connected);
            }
            finally
            {
                Close();
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            UnsubscribeFromSession();
            (_announcer as IDisposable)?.Dispose();
            _ = _session.DisposeAsync();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    // ── Status bar ─────────────────────────────────────────────────────────

    private void TmrSecond_Tick(object? sender, EventArgs e) => UpdateConnectedTime();

    private void UpdateConnectedTime()
    {
        _stlTime.Text = _session.ConnectedSince is { } since && _session.State == SessionState.Connected
            ? FormatElapsed(DateTime.Now - since)
            : string.Empty;
    }

    internal static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        var parts = new List<string>(4);
        Add(elapsed.Days, Strings.Client_Day, Strings.Client_Days);
        Add(elapsed.Hours, Strings.Client_Hour, Strings.Client_Hours);
        Add(elapsed.Minutes, Strings.Client_Minute, Strings.Client_Minutes);
        if (elapsed.Seconds > 0 || parts.Count == 0)
            parts.Add(string.Format(elapsed.Seconds == 1 ? Strings.Client_Second : Strings.Client_Seconds, elapsed.Seconds));
        return string.Join(", ", parts) + ".";

        void Add(int value, string singular, string plural)
        {
            if (value > 0) parts.Add(string.Format(value == 1 ? singular : plural, value));
        }
    }

    private void UpdateMessagesStatus()
    {
        _stlMessages.Text = _shownMessages.Count == 0
            ? Strings.Client_NoMessages
            : string.Format(Strings.Client_LastMessageAt, _shownMessages[^1].Message.Time);
    }

    private void RtbMessages_SelectionChanged(object? sender, EventArgs e)
    {
        if (!_rtbMessages.Focused) return;
        if (MessageAtCaret() is not { } message) return;
        _stlMessages.Text = string.Format(Strings.Client_SelectedMessageAt, message.Time);
        _tmrMessageStatus.Stop();
        _tmrMessageStatus.Start();
    }

    private void TmrMessageStatus_Tick(object? sender, EventArgs e)
    {
        _tmrMessageStatus.Stop();
        UpdateMessagesStatus();
    }

    private SessionMessage? MessageAtCaret()
    {
        if (_shownMessages.Count == 0) return null;
        var caret = _rtbMessages.SelectionStart;
        var index = _shownMessages.FindLastIndex(m => m.Start <= caret);
        return index < 0 ? null : _shownMessages[index].Message;
    }

    private static class WindowFlasher
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct FLASHWINFO
        {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }

        private const uint FLASHW_TRAY = 0x2;
        private const uint FLASHW_TIMERNOFG = 0xC;

        [DllImport("user32.dll")]
        private static extern bool FlashWindowEx(ref FLASHWINFO info);

        /// <summary>Flashes the taskbar button until the window comes to the foreground.</summary>
        public static void Flash(Form form)
        {
            if (!form.IsHandleCreated || Form.ActiveForm == form) return;
            var info = new FLASHWINFO
            {
                cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                hwnd = form.Handle,
                dwFlags = FLASHW_TRAY | FLASHW_TIMERNOFG,
                uCount = 6,
                dwTimeout = 0,
            };
            FlashWindowEx(ref info);
        }
    }
}
