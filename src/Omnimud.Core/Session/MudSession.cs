using System.Collections.Concurrent;
using System.Text;
using Omnimud.Core.Actions;
using Omnimud.Core.Commands;
using Omnimud.Core.Connection;
using Omnimud.Core.Logging;
using Omnimud.Core.Messages;
using Omnimud.Core.Options;
using Omnimud.Core.Resources;
using Omnimud.Core.Scripting;
using Omnimud.Core.Sound;
using Omnimud.Core.Telnet;
using Omnimud.Core.Text;
using Omnimud.Core.Triggers;

namespace Omnimud.Core.Session;

/// <summary>
/// One connection to a MUD and everything that hangs from it.
///
/// Threading: all mutable state is touched only from work items of a <see cref="SerialQueue"/>.
/// Network data, timers, scripts and the UI post to that queue, so nothing blocks and there
/// is no lock ordering to get wrong. A trigger whose action sends a command runs the input
/// processor directly (it is already inside a work item); scripts run on the thread pool and
/// come back through the queue.
/// </summary>
public sealed partial class MudSession : IMudSession, IScriptHost, IInputHost
{
    private const string SpeakMarker = "all_speak:";

    private readonly IConnection _connection;
    private readonly ISessionStore _store;
    private readonly IOptionsService _optionsService;
    private readonly ISessionSound _sound;
    private readonly IScriptEngine _scripts;
    private readonly TimeProvider _time;
    private readonly MudSessionSettings _settings;
    private readonly IProxySettingsResolver _proxies;

    private readonly SerialQueue _queue = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly TelnetNegotiator _telnet = new();
    private readonly StreamDecoder _decoder;
    private readonly Encoding _encoding;
    private readonly LineBuffer _lineBuffer;
    private readonly AnsiParser _ansi = new();
    private readonly MspExtractor _msp = new();
    private readonly TriggerEngine _triggers = new(new TriggerMatcher());
    private readonly InputProcessor _input;
    private readonly MessageStore _messages = new();
    private readonly ActionMenuState _actionMenu;
    private readonly SessionLogWriter _log;
    private readonly ConcurrentDictionary<string, string> _variables = new(StringComparer.Ordinal);
    private readonly HashSet<(TelnetVerb Verb, TelnetCommand Option)> _answeredOptions = [];
    private readonly HashSet<string> _runningScripts = new(StringComparer.Ordinal);

    private MessageRuleSet _rules = new([]);
    // Effective movement commands (configured ?? default for the UI language); swapped as a whole, read from any thread.
    private readonly IReadOnlyDictionary<int, string> _movementDefaults;
    private volatile IReadOnlyDictionary<int, string> _movements;

    private volatile OmnimudOptions _options = OmnimudOptions.Default;
    private volatile SessionState _state = SessionState.Disconnected;
    private volatile bool _passwordMode;
    private volatile bool _silentMode;
    private volatile bool _movementMode;
    private volatile bool _windowActive;
    private long _connectedSinceTicks;
    private long _lastActivityTimestamp;
    private bool _closing;
    private CancellationTokenSource? _connectCts;
    private int _disposed;

    public MudSession(
        SessionProfile profile,
        IConnection connection,
        ISessionStore store,
        IOptionsService optionsService,
        ISessionSound sound,
        IScriptEngine scriptEngine,
        TimeProvider timeProvider,
        MudSessionSettings settings,
        IProxySettingsResolver? proxySettings = null)
    {
        Profile = profile;
        _connection = connection;
        _store = store;
        _optionsService = optionsService;
        _sound = sound;
        _scripts = scriptEngine;
        _time = timeProvider;
        _settings = settings;
        // Without one: manual proxies work (without stored password) and "automatic" means direct.
        _proxies = proxySettings ?? ProxySettingsResolver.Basic;

        _encoding = StreamDecoder.ResolveEncoding(profile.Encoding);
        _decoder = new StreamDecoder(_encoding);
        _lineBuffer = new LineBuffer(timeProvider, () => TimeSpan.FromMilliseconds(_options.PromptFlushMilliseconds));
        _lineBuffer.PromptTimeout += OnPromptTimeout;
        _input = new InputProcessor(this);
        _actionMenu = new ActionMenuState(settings.ActionMenuTranslators);
        _log = new SessionLogWriter(timeProvider, settings.LogDirectory);
        _log.Failed += OnLogFailed;
        _lastActivityTimestamp = timeProvider.GetTimestamp();

        // Like the queue, the defaults take the UI language of whoever creates the session.
        _movementDefaults = MovementKeys.DefaultCommands(System.Globalization.CultureInfo.CurrentUICulture);
        _movements = MovementKeys.Effective(new Dictionary<int, string>(), _movementDefaults);

        _connection.DataReceived += OnConnectionData;
        _connection.Disconnected += OnConnectionClosed;
        _optionsService.Changed += OnOptionsChanged;
    }

    // ── State ──────────────────────────────────────────────────────────────

    public SessionProfile Profile { get; }
    public SessionState State => _state;
    public OmnimudOptions Options => _options;
    public bool PasswordMode => _passwordMode;
    public bool MovementMode => _movementMode;
    public IReadOnlyDictionary<int, string> Movements => _movements;
    public bool HasMovement(int key) => _movements.ContainsKey(key);

    public bool SilentMode
    {
        get => _silentMode;
        set
        {
            if (_silentMode == value) return;
            _silentMode = value;
            Raise(SilentModeChanged, value);
        }
    }

    public bool IsWindowActive
    {
        get => _windowActive;
        set
        {
            _windowActive = value;
            Guard(() => _sound.IsWindowActive = value);
        }
    }

    public DateTime? ConnectedSince
    {
        get
        {
            var ticks = Interlocked.Read(ref _connectedSinceTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Local);
        }
    }

    /// <summary>Oldest first; <see cref="SessionMessage.Number"/> 1 is the most recent.</summary>
    public IReadOnlyList<SessionMessage> Messages => _messages.Snapshot();

    /// <summary>Message by the number the user hears with Ctrl+number (1 = most recent). Null if out of range.</summary>
    public SessionMessage? GetMessage(int number) => _messages.GetByNumber(number);

    public IReadOnlyList<string> History => _input.History;

    public ActionMenu ActionMenu => _actionMenu.Current;

    /// <summary>True between "paths iniciar" and "paths detener" / "paths cancelar".</summary>
    public bool IsRecordingPath => _input.IsRecordingPath;

    /// <summary>False after "-triggers".</summary>
    public bool TriggersEnabled => _triggers.IsEnabled;

    public event Action<SessionLine>? LineReceived;
    public event Action<SessionMessage>? MessageAdded;
    public event Action? ActionMenuChanged;
    public event Action<string, AnnouncePriority>? Announce;
    public event Action<SessionState>? StateChanged;
    public event Action<bool>? PasswordModeChanged;
    public event Action<bool>? MovementModeChanged;
    public event Action<bool>? SilentModeChanged;
    public event Action? ClearRequested;
    public event Action<SessionWindow, string?>? WindowRequested;
    public event Action? FlashRequested;
    public event Action<string>? StatusChanged;
    public event Func<string, bool>? ConfirmRequested;
    public event Action<string>? UiSoundRequested;

    /// <summary>Completes when everything queued so far (data, input, script calls) has been processed.</summary>
    public Task WhenIdleAsync() => _queue.WhenIdleAsync();

    // ── Lifecycle ──────────────────────────────────────────────────────────

    public Task InitializeAsync(CancellationToken ct = default)
        => _queue.Enqueue(() => LoadAllAsync(ct));

    public Task ReloadAsync(CancellationToken ct = default)
        => _queue.Enqueue(() => LoadAllAsync(ct));

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        // Closing or disposing must be able to abandon a connection attempt that is still waiting.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        Volatile.Write(ref _connectCts, linked);
        try
        {
            await _queue.Enqueue(() => ConnectCoreAsync(linked.Token)).ConfigureAwait(false);
            await RunLoginScriptAsync(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.CompareExchange(ref _connectCts, null, linked);
        }
    }

    /// <summary>Only valid after a failed connection. Takes effect immediately.</summary>
    public void EnterOfflineMode()
    {
        if (_state == SessionState.Disconnected)
            SetState(SessionState.Offline);
    }

    public async Task CloseAsync(bool sendSaveAndQuit, CancellationToken ct = default)
    {
        CancelPendingConnect();
        // The wait happens outside the queue so the MUD's farewell is still shown and logged.
        var sent = await _queue.Enqueue(() => SendFarewellAsync(sendSaveAndQuit), false).ConfigureAwait(false);
        if (sent && _settings.QuitGraceDelay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(_settings.QuitGraceDelay, _time, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Closing anyway.
            }
        }

        await _queue.Enqueue(ShutdownAsync).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _optionsService.Changed -= OnOptionsChanged;
        CancelPendingConnect();
        await _queue.Enqueue(ShutdownAsync).ConfigureAwait(false);

        _connection.DataReceived -= OnConnectionData;
        _connection.Disconnected -= OnConnectionClosed;
        _cts.Cancel();
        await _queue.DisposeAsync().ConfigureAwait(false);

        _lineBuffer.PromptTimeout -= OnPromptTimeout;
        _lineBuffer.Dispose();
        _log.Dispose();
        Guard(_scripts.Dispose);
        Guard(_sound.Dispose);
        try
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A dying socket is not worth reporting.
        }
        _cts.Dispose();
    }

    private void CancelPendingConnect()
    {
        try
        {
            Volatile.Read(ref _connectCts)?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The attempt finished between the read and the cancel.
        }
    }

    private async Task LoadAllAsync(CancellationToken ct)
    {
        var options = await _optionsService.ResolveAsync(Profile.MudId, Profile.CharacterId, ct).ConfigureAwait(false);

        IReadOnlyList<Aliases.AliasDefinition> aliases = [];
        IReadOnlyList<TriggerDefinition> triggers = [];
        IReadOnlyList<Paths.PathDefinition> paths = [];
        IReadOnlyList<Paths.DirectionEntry> directions = [];
        IReadOnlyList<MessageRule> rules = [];
        string? ruleScript = null;

        if (Profile.CharacterId is { } characterId)
        {
            aliases = await _store.GetAliasesAsync(characterId, ct).ConfigureAwait(false);
            triggers = await _store.GetTriggersAsync(characterId, ct).ConfigureAwait(false);
            paths = await _store.GetPathsAsync(characterId, ct).ConfigureAwait(false);
        }

        if (Profile.MudId is { } mudId)
        {
            directions = await _store.GetDirectionsAsync(mudId, ct).ConfigureAwait(false);
            rules = await _store.GetMessageRulesAsync(mudId, ct).ConfigureAwait(false);
            ruleScript = await _store.GetMessageRuleScriptAsync(mudId, ct).ConfigureAwait(false);
        }

        var movements = await _store.GetMovementsAsync(Profile.MudId, Profile.CharacterId, ct).ConfigureAwait(false);
        var movementMode = await _store.GetMovementModeAsync(Profile.MudId, Profile.CharacterId, ct).ConfigureAwait(false);

        // Everything loaded: swap it in at once.
        _input.Load(aliases, paths, directions);
        _triggers.LoadTriggers(triggers);
        LoadMessageRules(rules, ruleScript);
        if (_ruleScript is { } loadedScript)
            await MessageRuleScript.WarmUpAsync(_scripts, loadedScript, ct).ConfigureAwait(false);
        _movements = MovementKeys.Effective(movements, _movementDefaults);
        ApplyOptions(options);

        if (_movementMode != movementMode)
        {
            _movementMode = movementMode;
            Raise(MovementModeChanged, movementMode);
        }
    }

    private void ApplyOptions(OmnimudOptions options)
    {
        var previous = _options;
        _options = options;

        Guard(() => _sound.Configure(BuildSoundSettings(options)));
        _input.TrimHistory();

        var logChanged = previous.LogType != options.LogType ||
                         !string.Equals(previous.LogDirectory, options.LogDirectory, StringComparison.OrdinalIgnoreCase);
        if (logChanged && _state == SessionState.Connected)
            OpenLog();
    }

    private SoundSettings BuildSoundSettings(OmnimudOptions options)
    {
        var mudFolder = !string.IsNullOrWhiteSpace(Profile.SoundDirectory)
            ? Profile.SoundDirectory
            : Path.Combine(_settings.SoundsDirectory, SessionLogWriter.SanitizeName(Profile.MudName ?? Profile.Title));

        // Downloads follow the proxy options whether or not the MUD connection does.
        var proxy = _proxies.ForDownloads(options);

        return new SoundSettings
        {
            MudSoundDirectory = mudFolder,
            AppSoundDirectory = _settings.AppSoundsDirectory,
            EnableSounds = options.EnableSounds,
            EnableMusic = options.EnableMusic,
            PlaySoundsInBackground = options.PlaySoundsInBackground,
            PlayMusicInBackground = options.PlayMusicInBackground,
            DownloadSounds = options.DownloadSounds,
            AllowHttpDownloads = options.AllowHttpDownloads,
            Volume = options.Volume,
            DownloadProxy = proxy.Kind == DownloadProxyKind.Manual ? proxy.Address : null,
            DownloadProxySettings = proxy
        };
    }

    private async Task ConnectCoreAsync(CancellationToken ct)
    {
        if (_state is SessionState.Connected or SessionState.Connecting)
            throw new InvalidOperationException(string.Format(Strings.Error_CannotConnectInState, _state));

        // A new connection starts clean: no half sequence, half character or half line from the last one.
        _closing = false;
        _telnet.Reset();
        _decoder.Reset();
        _lineBuffer.Reset();
        _ansi.Reset();
        _answeredOptions.Clear();
        ClearActionMenu();
        SetPasswordMode(false);
        SetState(SessionState.Connecting);

        // Resolved on every attempt, so a reconnection sees the options (and the system proxy) of that moment.
        // Off the queue's thread: in automatic mode the system may have to run a PAC script.
        var options = _options;
        ProxyConfig? proxy;
        try
        {
            proxy = await Task.Run(() => _proxies.ForMud(options, Profile.Host, Profile.Port), ct).ConfigureAwait(false);
        }
        catch
        {
            SetState(SessionState.Disconnected);
            throw;
        }

        var config = new ConnectionConfig(
            Profile.Host,
            Profile.Port,
            Profile.UseTls,
            Profile.UseTls ? new TlsConfig(Profile.ValidateCertificate) : null,
            proxy)
        {
            // The resolved name, not the profile's: an unknown encoding has already fallen back to UTF-8.
            TextEncoding = _encoding.WebName,
            LineTerminator = "\n",
            EscapeTelnetIac = true
        };

        try
        {
            await _connection.ConnectAsync(config, ct).ConfigureAwait(false);
        }
        catch
        {
            SetState(SessionState.Disconnected);
            throw;
        }

        Interlocked.Exchange(ref _connectedSinceTicks, _time.GetLocalNow().DateTime.Ticks);
        _lastActivityTimestamp = _time.GetTimestamp();
        OpenLog();
        SetState(SessionState.Connected);
    }

    private async Task RunLoginScriptAsync(CancellationToken ct)
    {
        var script = Profile.LoginScript;
        if (string.IsNullOrWhiteSpace(script))
        {
            // The original client logged in by itself: name, then password if there is one.
            if (string.IsNullOrWhiteSpace(Profile.CharacterName)) return;
            script = string.IsNullOrEmpty(Profile.CharacterPassword) ? "%character" : "%character\n%password";
        }

        var lines = script.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => l.Length > 0)
            .ToArray();

        try
        {
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var secret = line.Contains("%password", StringComparison.OrdinalIgnoreCase);
                var resolved = line
                    .Replace("%character", Profile.CharacterName ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                    .Replace("%password", Profile.CharacterPassword ?? string.Empty, StringComparison.OrdinalIgnoreCase);

                var stillConnected = await _queue.Enqueue(async () =>
                {
                    if (_state != SessionState.Connected) return false;
                    await SendLineCoreAsync(resolved, log: !secret).ConfigureAwait(false);
                    return true;
                }, false).ConfigureAwait(false);

                if (!stillConnected) return;
                if (i < lines.Length - 1 && _settings.LoginLineDelay > TimeSpan.Zero)
                    await Task.Delay(_settings.LoginLineDelay, _time, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Connection attempt abandoned: the rest of the script is not sent.
        }
    }

    private async Task<bool> SendFarewellAsync(bool sendSaveAndQuit)
    {
        _closing = true;
        if (!sendSaveAndQuit || _state != SessionState.Connected || !_options.TrySaveBeforeExit)
            return false;

        var sent = false;
        if (!string.IsNullOrWhiteSpace(Profile.SaveCommand))
        {
            await SendLineCoreAsync(Profile.SaveCommand, log: true).ConfigureAwait(false);
            sent = true;
        }
        if (!string.IsNullOrWhiteSpace(Profile.QuitCommand))
        {
            await SendLineCoreAsync(Profile.QuitCommand, log: true).ConfigureAwait(false);
            sent = true;
        }
        return sent;
    }

    private async Task ShutdownAsync()
    {
        _closing = true;
        FailPendingGets();

        if (_state is SessionState.Connected or SessionState.Connecting)
        {
            try
            {
                await _connection.DisconnectAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Closing anyway.
            }

            await FlushPendingTextAsync().ConfigureAwait(false);
        }

        EndConnection();
        Guard(_sound.StopAll);
    }

    /// <summary>Common end of a connection, whoever closed it.</summary>
    private void EndConnection()
    {
        Interlocked.Exchange(ref _connectedSinceTicks, 0);
        SetPasswordMode(false);
        // What could be done in the MUD a moment ago cannot be done any more.
        ClearActionMenu();
        _log.Close(writeFooter: true);
        if (_state != SessionState.Offline)
            SetState(SessionState.Disconnected);
    }

    private void OpenLog()
        => _log.Open(_options.LogType, _options.LogDirectory, Profile.MudName ?? Profile.Title, Profile.CharacterName);

    // ── Connection and option events (any thread) ──────────────────────────

    private Task OnConnectionData(ReadOnlyMemory<byte> data)
    {
        // The buffer belongs to the connection: copy before leaving its thread.
        var copy = data.ToArray();
        _queue.Post(() => ProcessDataAsync(copy), ReportInternalError);
        return Task.CompletedTask;
    }

    private Task OnConnectionClosed(DisconnectReason reason)
    {
        _queue.Post(() => HandleDisconnectedAsync(reason), ReportInternalError);
        return Task.CompletedTask;
    }

    private async Task HandleDisconnectedAsync(DisconnectReason reason)
    {
        if (_state is not (SessionState.Connected or SessionState.Connecting))
            return;

        FailPendingGets();
        await FlushPendingTextAsync().ConfigureAwait(false);

        if (!_closing && reason != DisconnectReason.UserRequested)
        {
            var text = reason switch
            {
                DisconnectReason.ServerClosed => Strings.Session_ServerClosed,
                DisconnectReason.Timeout => Strings.Session_ConnectionTimeout,
                _ => Strings.Session_ConnectionLost
            };
            WriteSystem(text, AnnouncePriority.Interrupt);
        }

        EndConnection();
        Guard(_sound.StopAll);
    }

    private void OnOptionsChanged(object? sender, OptionsChangedEventArgs e)
    {
        var affected = e.Scope switch
        {
            OptionScope.Global => true,
            OptionScope.Mud => e.ScopeId == Profile.MudId,
            OptionScope.Character => e.ScopeId == Profile.CharacterId,
            _ => false
        };
        if (!affected) return;

        _queue.Post(async () =>
        {
            var options = await _optionsService.ResolveAsync(Profile.MudId, Profile.CharacterId, _cts.Token).ConfigureAwait(false);
            ApplyOptions(options);
        }, ReportInternalError);
    }

    private void OnLogFailed(string error)
        => _queue.Post(() =>
        {
            WriteSystem(string.Format(Strings.Log_OpenError, error), AnnouncePriority.Queue);
            return Task.CompletedTask;
        });

    // ── Input ──────────────────────────────────────────────────────────────

    public Task SubmitInputAsync(string text)
        => _queue.Enqueue(() => _input.SubmitAsync(text));

    public Task SendBlankLineAsync()
        => _queue.Enqueue(() => _input.ExecuteAsync(string.Empty));

    public Task ExecuteCommandAsync(string command)
        => _queue.Enqueue(() => _input.ExecuteAsync(command));

    public Task<bool> ExecuteMovementAsync(int key)
        => _queue.Enqueue(async () =>
        {
            if (!_movements.TryGetValue(key, out var command) || string.IsNullOrWhiteSpace(command))
                return false;

            await _input.ExecuteAsync(command).ConfigureAwait(false);
            return true;
        }, false);

    public Task ExecuteActionAsync(ActionMenuNode node)
        => node is { IsAction: true, Command: { } command }
            ? _queue.Enqueue(() => _input.ExecuteExternalAsync(command))
            : Task.CompletedTask;

    public Task SendSaveCommandAsync() => ExecuteProfileCommandAsync(Profile.SaveCommand);

    public Task SendQuitCommandAsync() => ExecuteProfileCommandAsync(Profile.QuitCommand);

    public Task SetMovementModeAsync(bool enabled)
        => _queue.Enqueue(async () =>
        {
            await _store.SetMovementModeAsync(Profile.MudId, Profile.CharacterId, enabled).ConfigureAwait(false);
            if (_movementMode == enabled) return;
            _movementMode = enabled;
            Raise(MovementModeChanged, enabled);
        });

    private Task ExecuteProfileCommandAsync(string? command)
        => string.IsNullOrWhiteSpace(command) ? Task.CompletedTask : ExecuteCommandAsync(command);

    // ── Helpers ────────────────────────────────────────────────────────────

    private void SetState(SessionState state)
    {
        if (_state == state) return;
        _state = state;
        Raise(StateChanged, state);
    }

    private void ClearActionMenu()
    {
        if (_actionMenu.Clear())
            Raise(ActionMenuChanged);
    }

    private void SetPasswordMode(bool on)
    {
        if (_passwordMode == on) return;
        _passwordMode = on;
        Raise(PasswordModeChanged, on);
    }

    private void ReportInternalError(Exception ex)
        => _queue.Post(() =>
        {
            WriteSystem(string.Format(Strings.Session_ScriptError, "OMnimud", ex.Message), AnnouncePriority.Queue);
            return Task.CompletedTask;
        });

    /// <summary>A subscriber that throws must not take the session down.</summary>
    private static void Raise<T>(Action<T>? handler, T argument)
    {
        if (handler is null) return;
        foreach (var target in handler.GetInvocationList())
        {
            try { ((Action<T>)target)(argument); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }
    }

    private static void Raise(Action? handler)
    {
        if (handler is null) return;
        foreach (var target in handler.GetInvocationList())
        {
            try { ((Action)target)(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }
    }

    private static void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
    }
}
