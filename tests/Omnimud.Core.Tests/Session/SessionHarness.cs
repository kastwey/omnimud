using System.Globalization;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Omnimud.Core.Aliases;
using Omnimud.Core.Connection;
using Omnimud.Core.Options;
using Omnimud.Core.Paths;
using Omnimud.Core.Scripting;
using Omnimud.Core.Session;
using Omnimud.Core.Sound;
using Omnimud.Core.Triggers;

namespace Omnimud.Core.Tests.Session;

internal sealed class FakeConnection : IConnection
{
    private readonly object _gate = new();
    private readonly List<byte[]> _sent = [];

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public ConnectionConfig? LastConfig { get; private set; }
    public Exception? ConnectFailure { get; set; }
    /// <summary>When set, ConnectAsync waits on it (a server that does not answer).</summary>
    public Func<CancellationToken, Task>? ConnectBehavior { get; set; }
    public Exception? SendFailure { get; set; }
    public int ConnectCount { get; private set; }
    public bool Disposed { get; private set; }

    public event Func<ReadOnlyMemory<byte>, Task>? DataReceived;
    public event Func<DisconnectReason, Task>? Disconnected;

    public async Task ConnectAsync(ConnectionConfig config, CancellationToken ct = default)
    {
        LastConfig = config;
        ConnectCount++;
        if (ConnectFailure is not null) throw ConnectFailure;
        if (ConnectBehavior is not null) await ConnectBehavior(ct);
        State = ConnectionState.Connected;
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        if (State == ConnectionState.Disconnected) return;
        State = ConnectionState.Disconnected;
        if (Disconnected is not null) await Disconnected(DisconnectReason.UserRequested);
    }

    /// <summary>Same contract as TelnetConnection: encoding, terminator and IAC doubling come from the config.</summary>
    public Task SendAsync(string command, CancellationToken ct = default)
    {
        var config = LastConfig ?? new ConnectionConfig("none", 1);
        var data = Encoding.GetEncoding(config.TextEncoding).GetBytes(command + config.LineTerminator);
        if (config.EscapeTelnetIac) data = global::Omnimud.Core.Telnet.TelnetNegotiator.EscapeIac(data);
        return SendRawAsync(data, ct);
    }

    public Task SendRawAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        if (SendFailure is not null) throw SendFailure;
        if (State != ConnectionState.Connected) throw new InvalidOperationException("Not connected");
        lock (_gate) _sent.Add(data.ToArray());
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        State = ConnectionState.Disconnected;
        return ValueTask.CompletedTask;
    }

    public IReadOnlyList<byte[]> SentPackets
    {
        get { lock (_gate) return _sent.ToArray(); }
    }

    public void ClearSent()
    {
        lock (_gate) _sent.Clear();
    }

    public Task RaiseData(byte[] data) => DataReceived?.Invoke(data) ?? Task.CompletedTask;

    public async Task RaiseServerClosed(DisconnectReason reason = DisconnectReason.ServerClosed)
    {
        State = ConnectionState.Disconnected;
        if (Disconnected is not null) await Disconnected(reason);
    }
}

internal sealed class FakeStore : ISessionStore
{
    public List<AliasDefinition> Aliases { get; } = [];
    public List<TriggerDefinition> Triggers { get; } = [];
    public List<PathDefinition> Paths { get; } = [];
    public List<DirectionEntry> Directions { get; } = [];
    public Dictionary<int, string> Movements { get; } = [];
    public List<MessageRule> Rules { get; } = [];
    /// <summary>Not blank = the MUD's rule set is of type script.</summary>
    public string? RuleScript { get; set; }
    public int RuleScriptReads { get; private set; }
    public bool MovementMode { get; set; }
    public List<(string Id, bool Enabled)> TriggerWrites { get; } = [];
    public Exception? WriteFailure { get; set; }

    public Task<IReadOnlyList<AliasDefinition>> GetAliasesAsync(int characterId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AliasDefinition>>(Aliases.ToArray());

    public Task<bool> AddAliasAsync(int characterId, string command, string action, CancellationToken ct = default)
    {
        if (WriteFailure is not null) throw WriteFailure;
        if (Aliases.Any(a => a.Command == command)) return Task.FromResult(false);
        Aliases.Add(new AliasDefinition(command, action));
        return Task.FromResult(true);
    }

    public Task<bool> RemoveAliasAsync(int characterId, string command, CancellationToken ct = default)
    {
        if (WriteFailure is not null) throw WriteFailure;
        return Task.FromResult(Aliases.RemoveAll(a => a.Command == command) > 0);
    }

    public Task<IReadOnlyList<TriggerDefinition>> GetTriggersAsync(int characterId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TriggerDefinition>>(Triggers.ToArray());

    public Task SetTriggerEnabledAsync(string triggerId, bool enabled, CancellationToken ct = default)
    {
        if (WriteFailure is not null) throw WriteFailure;
        TriggerWrites.Add((triggerId, enabled));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PathDefinition>> GetPathsAsync(int characterId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PathDefinition>>(Paths.ToArray());

    public Task<IReadOnlyList<DirectionEntry>> GetDirectionsAsync(int mudId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DirectionEntry>>(Directions.ToArray());

    public Task<IReadOnlyDictionary<int, string>> GetMovementsAsync(int? mudId, int? characterId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<int, string>>(new Dictionary<int, string>(Movements));

    public Task<bool> GetMovementModeAsync(int? mudId, int? characterId, CancellationToken ct = default)
        => Task.FromResult(MovementMode);

    public Task SetMovementModeAsync(int? mudId, int? characterId, bool enabled, CancellationToken ct = default)
    {
        MovementMode = enabled;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MessageRule>> GetMessageRulesAsync(int mudId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MessageRule>>(Rules.ToArray());

    public Task<string?> GetMessageRuleScriptAsync(int mudId, CancellationToken ct = default)
    {
        RuleScriptReads++;
        return Task.FromResult(RuleScript);
    }
}

internal sealed class FakeOptionsService : IOptionsService
{
    public OmnimudOptions Current { get; set; } = OmnimudOptions.Default with { LogType = LogMode.None };

    public event EventHandler<OptionsChangedEventArgs>? Changed;

    public Task<OmnimudOptions> ResolveAsync(int? mudId, int? characterId, CancellationToken ct = default)
        => Task.FromResult(Current);

    public Task<bool> HasOwnOptionsAsync(OptionScope scope, int? scopeId, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task SaveAsync(OptionScope scope, int? scopeId, OmnimudOptions options, CancellationToken ct = default)
    {
        Current = options;
        Changed?.Invoke(this, new OptionsChangedEventArgs(scope, scopeId));
        return Task.CompletedTask;
    }

    public Task ResetToInheritedAsync(OptionScope scope, int? scopeId, CancellationToken ct = default)
        => Task.CompletedTask;

    public void RaiseChanged(OptionScope scope, int? scopeId)
        => Changed?.Invoke(this, new OptionsChangedEventArgs(scope, scopeId));
}

/// <summary>A session wired to fakes, in Spanish, with everything it emits recorded.</summary>
internal sealed class SessionHarness : IAsyncDisposable
{
    public const string Esc = "\x1b";

    private readonly object _gate = new();
    private readonly List<SessionLine> _lines = [];
    private readonly List<(string Text, AnnouncePriority Priority)> _announcements = [];
    private readonly List<SessionMessage> _messages = [];

    public FakeConnection Connection { get; } = new();
    public FakeStore Store { get; } = new();
    public FakeOptionsService OptionsService { get; } = new();
    public ISessionSound Sound { get; } = Substitute.For<ISessionSound>();
    /// <summary>A substitute unless the test needs real Lua (set it before StartAsync; the session disposes it).</summary>
    public IScriptEngine Scripts { get; set; } = Substitute.For<IScriptEngine>();
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 3, 14, 10, 30, 0, TimeSpan.Zero));
    public string LogDirectory { get; }
    public SessionProfile Profile { get; set; }
    public MudSession Session { get; set; } = null!;
    /// <summary>Translators of the actions menu; null = the ones of the application.</summary>
    public IReadOnlyList<global::Omnimud.Core.Actions.IActionMenuTranslator>? Translators { get; set; }
    /// <summary>Who turns the proxy options into something usable; null = what a session gets by default.</summary>
    public global::Omnimud.Core.Connection.IProxySettingsResolver? ProxySettings { get; set; }

    public List<SessionState> States { get; } = [];
    public List<bool> PasswordModes { get; } = [];
    public List<(SessionWindow Window, string? Payload)> Windows { get; } = [];
    public List<string> UiSounds { get; } = [];
    public List<string> Statuses { get; } = [];
    public List<string> Confirmations { get; } = [];
    public bool ConfirmAnswer { get; set; } = true;
    public int Flashes;
    public int Clears;

    private readonly string _culture;

    public SessionHarness(string culture = "es")
    {
        _culture = culture;
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        Time.SetLocalTimeZone(TimeZoneInfo.Utc);
        LogDirectory = Path.Combine(Path.GetTempPath(), "omnimud-tests", Guid.NewGuid().ToString("N"));
        Profile = new SessionProfile
        {
            Title = "Reinos",
            Host = "mud.example",
            Port = 4000,
            MudId = 1,
            MudName = "Reinos",
            CharacterId = 7,
            CharacterName = "Zork",
            CharacterPassword = "secreto",
            SaveCommand = "salvar",
            QuitCommand = "abandonar",
            LoginScript = "%character\n%password"
        };
    }

    public Encoding Encoding => global::Omnimud.Core.Text.StreamDecoder.ResolveEncoding(Profile.Encoding);

    public async Task<MudSession> StartAsync(bool connect = true, bool windowActive = true)
    {
        // The session's queue captures the culture of whoever creates it.
        CultureInfo.CurrentUICulture = new CultureInfo(_culture);
        Session = new MudSession(Profile, Connection, Store, OptionsService, Sound, Scripts, Time, new MudSessionSettings
        {
            LogDirectory = LogDirectory,
            SoundsDirectory = Path.Combine(LogDirectory, "sounds"),
            AppSoundsDirectory = Path.Combine(LogDirectory, "appsounds"),
            QuitGraceDelay = TimeSpan.Zero,
            LoginLineDelay = TimeSpan.Zero,
            ScriptGagWait = TimeSpan.FromSeconds(5),
            ActionMenuTranslators = Translators ?? global::Omnimud.Core.Actions.ActionMenuTranslators.Default
        }, ProxySettings);

        Session.LineReceived += l => { lock (_gate) _lines.Add(l); };
        Session.Announce += (t, p) => { lock (_gate) _announcements.Add((t, p)); };
        Session.MessageAdded += m => { lock (_gate) _messages.Add(m); };
        Session.StateChanged += s => { lock (_gate) States.Add(s); };
        Session.PasswordModeChanged += p => { lock (_gate) PasswordModes.Add(p); };
        Session.WindowRequested += (w, p) => { lock (_gate) Windows.Add((w, p)); };
        Session.UiSoundRequested += s => { lock (_gate) UiSounds.Add(s); };
        Session.StatusChanged += s => { lock (_gate) Statuses.Add(s); };
        Session.FlashRequested += () => Interlocked.Increment(ref Flashes);
        Session.ClearRequested += () => Interlocked.Increment(ref Clears);
        Session.ConfirmRequested += q => { lock (_gate) Confirmations.Add(q); return ConfirmAnswer; };
        Session.IsWindowActive = windowActive;

        await Session.InitializeAsync();
        if (connect)
        {
            await Session.ConnectAsync();
            await Session.WhenIdleAsync();
            Connection.ClearSent();
        }
        return Session;
    }

    public IReadOnlyList<SessionLine> Lines
    {
        get { lock (_gate) return _lines.ToArray(); }
    }

    public IReadOnlyList<string> PlainLines => Lines.Select(l => l.PlainText).ToArray();

    public IReadOnlyList<string> SystemLines => Lines.Where(l => l.Kind == SessionLineKind.System).Select(l => l.PlainText).ToArray();

    public IReadOnlyList<string> MudLines => Lines.Where(l => l.Kind != SessionLineKind.System).Select(l => l.PlainText).ToArray();

    public IReadOnlyList<(string Text, AnnouncePriority Priority)> Announcements
    {
        get { lock (_gate) return _announcements.ToArray(); }
    }

    public IReadOnlyList<string> Spoken => Announcements.Select(a => a.Text).ToArray();

    public IReadOnlyList<SessionMessage> AddedMessages
    {
        get { lock (_gate) return _messages.ToArray(); }
    }

    /// <summary>Text lines sent to the MUD (telnet packets excluded), decoded with the session encoding.</summary>
    public IReadOnlyList<string> SentLines
    {
        get
        {
            var result = new List<string>();
            foreach (var packet in Connection.SentPackets)
            {
                if (packet.Length > 0 && packet[0] == 255 && (packet.Length < 2 || packet[1] != 255)) continue;
                var text = Encoding.GetString(packet);
                result.Add(text.EndsWith('\n') ? text[..^1] : text);
            }
            return result;
        }
    }

    public IReadOnlyList<byte[]> SentTelnet
        => Connection.SentPackets.Where(p => p.Length > 1 && p[0] == 255 && p[1] != 255).ToArray();

    public void ClearOutput()
    {
        lock (_gate)
        {
            _lines.Clear();
            _announcements.Clear();
            _messages.Clear();
        }
        Connection.ClearSent();
    }

    public async Task ReceiveAsync(string text)
    {
        await Connection.RaiseData(Encoding.GetBytes(text));
        await Session.WhenIdleAsync();
    }

    public async Task ReceiveBytesAsync(params byte[] data)
    {
        await Connection.RaiseData(data);
        await Session.WhenIdleAsync();
    }

    /// <summary>Advances the fake clock and waits for whatever the timers queued.</summary>
    public async Task AdvanceAsync(int milliseconds)
    {
        Time.Advance(TimeSpan.FromMilliseconds(milliseconds));
        await Session.WhenIdleAsync();
    }

    public async Task SubmitAsync(string text)
    {
        await Session.SubmitInputAsync(text);
        await Session.WhenIdleAsync();
    }

    public void SetOptions(Func<OmnimudOptions, OmnimudOptions> change)
        => OptionsService.Current = change(OptionsService.Current);

    /// <summary>Waits (real time, briefly) for something that happens on a script thread.</summary>
    public async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var start = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - start > timeoutMs)
                throw new TimeoutException("Condition not reached");
            await Task.Delay(5);
            await Session.WhenIdleAsync();
        }
    }

    /// <summary>The queue is busy with the connection attempt, so this one must not wait for it.</summary>
    public async Task WaitUntilConnectingAsync()
    {
        var start = Environment.TickCount64;
        while (Session.State != SessionState.Connecting || Connection.ConnectCount == 0)
        {
            if (Environment.TickCount64 - start > 5000) throw new TimeoutException("Never started connecting");
            await Task.Delay(5);
        }
    }

    public static TriggerDefinition Trigger(
        string pattern,
        string action = "",
        TriggerActionType type = TriggerActionType.SendCommand,
        PatternType patternType = PatternType.Literal,
        string? name = null,
        int priority = 50,
        bool multiline = false,
        bool gag = false,
        bool caseSensitive = false,
        string? sound = null,
        bool enabled = true)
        => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name ?? pattern,
            Pattern = pattern,
            PatternType = patternType,
            Action = action,
            ActionType = type,
            Priority = priority,
            Multiline = multiline,
            GagLine = gag,
            CaseSensitive = caseSensitive,
            Sound = sound,
            Enabled = enabled
        };

    public async ValueTask DisposeAsync()
    {
        if (Session is not null)
            await Session.DisposeAsync();
        try
        {
            if (Directory.Exists(LogDirectory)) Directory.Delete(LogDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
