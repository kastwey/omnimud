using System.Text;
using FluentAssertions;
using NSubstitute;
using Omnimud.Core.Aliases;
using Omnimud.Core.Connection;
using Omnimud.Core.Options;
using Omnimud.Core.Scripting;
using Omnimud.Core.Session;
using Omnimud.Core.Sound;

namespace Omnimud.Core.Tests.Session;

public sealed class MudSessionLifecycleTests : IAsyncDisposable
{
    private readonly SessionHarness _h = new();

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private string ReadLog() => File.ReadAllText(LogFiles().Single());

    private string[] LogFiles() => Directory.Exists(_h.LogDirectory)
        ? Directory.GetFiles(_h.LogDirectory, "*.log", SearchOption.AllDirectories)
        : [];

    // ── Initialize / connect ───────────────────────────────────────────────

    [Fact]
    public async Task Initialize_ResolvesOptions_AndConfiguresSound()
    {
        _h.SetOptions(o => o with { Volume = 40, EnableMusic = false, HistorySize = 5 });
        _h.Profile = _h.Profile with { SoundDirectory = @"C:\sonidos\reinos" };

        await _h.StartAsync(connect: false);

        _h.Session.Options.HistorySize.Should().Be(5);
        _h.Sound.Received(1).Configure(Arg.Is<SoundSettings>(s =>
            s.Volume == 40 && !s.EnableMusic && s.MudSoundDirectory == @"C:\sonidos\reinos" &&
            s.AppSoundDirectory.EndsWith("appsounds")));
    }

    [Fact]
    public async Task Initialize_WithoutSoundFolderInProfile_UsesAFolderNamedAfterTheMud()
    {
        await _h.StartAsync(connect: false);

        _h.Sound.Received(1).Configure(Arg.Is<SoundSettings>(s => s.MudSoundDirectory.EndsWith(Path.Combine("sounds", "Reinos"))));
    }

    [Fact]
    public async Task Connect_BuildsTheConfigFromTheProfile_AndRaisesStates()
    {
        _h.Profile = _h.Profile with { UseTls = true, ValidateCertificate = false };

        await _h.StartAsync();

        _h.Connection.LastConfig.Should().BeEquivalentTo(new
        {
            Host = "mud.example",
            Port = 4000,
            UseTls = true,
            TlsOptions = new { ValidateCertificate = false },
            TextEncoding = "utf-8",
            EscapeTelnetIac = true
        });
        _h.Connection.LastConfig!.LineTerminator.Should().Be(((char)10).ToString());
        _h.States.Should().Equal(SessionState.Connecting, SessionState.Connected);
        _h.Session.ConnectedSince.Should().Be(new DateTime(2026, 3, 14, 10, 30, 0));
    }

    [Fact]
    public async Task Connect_WithoutTls_SendsNoTlsOptions_AndProxyOnlyWhenAskedFor()
    {
        _h.SetOptions(o => o with { ProxyType = ProxyMode.Manual, ProxyHost = "proxy", ProxyPort = 1080, UseProxyForMud = false });
        await _h.StartAsync();
        _h.Connection.LastConfig!.TlsOptions.Should().BeNull();
        _h.Connection.LastConfig.Proxy.Should().BeNull();

        await using var proxied = new SessionHarness();
        proxied.SetOptions(o => o with { ProxyType = ProxyMode.Manual, ProxyHost = "proxy", ProxyPort = 1080, UseProxyForMud = true });
        await proxied.StartAsync();
        proxied.Connection.LastConfig!.Proxy.Should().Be(new ProxyConfig("proxy", 1080));
    }

    [Theory]
    [InlineData("windows-1252", "windows-1252")]
    [InlineData("ISO-8859-1", "iso-8859-1")]
    [InlineData("no-existe", "utf-8")]
    public async Task Connect_PassesTheResolvedEncodingToTheConnection(string profileEncoding, string expected)
    {
        _h.Profile = _h.Profile with { Encoding = profileEncoding };

        await _h.StartAsync();

        _h.Connection.LastConfig!.TextEncoding.Should().Be(expected);
    }

    [Fact]
    public async Task Connect_RunsTheLoginScript_WithPlaceholders()
    {
        _h.Profile = _h.Profile with { LoginScript = "conectar %character\r\n\r\n%PASSWORD\nidioma es" };
        await _h.StartAsync(connect: false);

        await _h.Session.ConnectAsync();

        _h.SentLines.Should().Equal("conectar Zork", "secreto", "idioma es");
        _h.Session.History.Should().BeEmpty();
    }

    [Fact]
    public async Task Connect_WithoutLoginScript_SendsNameAndPassword_LikeTheOriginal()
    {
        _h.Profile = _h.Profile with { LoginScript = null };
        await _h.StartAsync(connect: false);
        await _h.Session.ConnectAsync();
        _h.SentLines.Should().Equal("Zork", "secreto");

        await using var noPassword = new SessionHarness();
        noPassword.Profile = noPassword.Profile with { LoginScript = " ", CharacterPassword = null };
        await noPassword.StartAsync(connect: false);
        await noPassword.Session.ConnectAsync();
        noPassword.SentLines.Should().Equal("Zork");

        await using var anonymous = new SessionHarness();
        anonymous.Profile = anonymous.Profile with { LoginScript = null, CharacterId = null, CharacterName = null };
        await anonymous.StartAsync(connect: false);
        await anonymous.Session.ConnectAsync();
        anonymous.SentLines.Should().BeEmpty();
    }

    [Fact]
    public async Task Connect_LoginScript_WaitsBetweenLines_UsingTheInjectedClock()
    {
        await using var slow = new SessionHarness();
        slow.Session = new MudSession(slow.Profile, slow.Connection, slow.Store, slow.OptionsService, slow.Sound, slow.Scripts, slow.Time,
            new MudSessionSettings { LogDirectory = slow.LogDirectory, SoundsDirectory = slow.LogDirectory, AppSoundsDirectory = slow.LogDirectory });
        await slow.Session.InitializeAsync();

        var connect = slow.Session.ConnectAsync();
        await slow.WaitUntilAsync(() => slow.SentLines.Count == 1);
        await Task.Delay(30);
        slow.SentLines.Should().Equal("Zork");

        while (!connect.IsCompleted)
        {
            slow.Time.Advance(TimeSpan.FromMilliseconds(250));
            await Task.Delay(5);
        }

        await connect;
        slow.SentLines.Should().Equal("Zork", "secreto");
    }

    [Fact]
    public async Task Connect_Failure_Throws_GoesBackToDisconnected_AndOfflineModeIsPossible()
    {
        _h.Connection.ConnectFailure = new IOException("rechazada");
        await _h.StartAsync(connect: false);

        await FluentActions.Awaiting(() => _h.Session.ConnectAsync()).Should().ThrowAsync<IOException>();
        _h.Session.EnterOfflineMode();
        await _h.Session.WhenIdleAsync();

        _h.States.Should().Equal(SessionState.Connecting, SessionState.Disconnected, SessionState.Offline);
        _h.Session.ConnectedSince.Should().BeNull();
    }

    [Fact]
    public async Task Close_WhileStillConnecting_AbandonsTheAttempt_WithoutWaitingForIt()
    {
        _h.Connection.ConnectBehavior = ct => Task.Delay(Timeout.Infinite, ct);
        await _h.StartAsync(connect: false);

        var connect = _h.Session.ConnectAsync();
        await _h.WaitUntilConnectingAsync();
        var close = _h.Session.CloseAsync(sendSaveAndQuit: true);

        await FluentActions.Awaiting(() => connect).Should().ThrowAsync<OperationCanceledException>();
        await close;
        _h.Session.State.Should().Be(SessionState.Disconnected);
        _h.SentLines.Should().BeEmpty();
    }

    [Fact]
    public async Task EnterOfflineMode_IsImmediate_AndOnlyFromDisconnected()
    {
        await _h.StartAsync();
        _h.Session.EnterOfflineMode();
        _h.Session.State.Should().Be(SessionState.Connected);

        await _h.Session.CloseAsync(false);
        _h.Session.EnterOfflineMode();
        _h.Session.State.Should().Be(SessionState.Offline);
    }

    [Fact]
    public async Task Connect_WhileConnected_Throws()
    {
        await _h.StartAsync();

        await FluentActions.Awaiting(() => _h.Session.ConnectAsync()).Should().ThrowAsync<InvalidOperationException>();
        _h.Session.State.Should().Be(SessionState.Connected);
    }

    // ── Server closes / reconnect ──────────────────────────────────────────

    [Fact]
    public async Task ServerCloses_StateLineAndInterruptingAnnouncement()
    {
        await _h.StartAsync();
        await _h.ReceiveAsync("Adiós> ");

        await _h.Connection.RaiseServerClosed();
        await _h.Session.WhenIdleAsync();

        _h.Session.State.Should().Be(SessionState.Disconnected);
        _h.Session.ConnectedSince.Should().BeNull();
        _h.Lines.Select(l => (l.PlainText, l.Kind)).Should().Equal(
            ("Adiós> ", SessionLineKind.Prompt),
            ("El servidor ha cerrado la conexión.", SessionLineKind.System));
        _h.Announcements[^1].Should().Be(("El servidor ha cerrado la conexión.", AnnouncePriority.Interrupt));
        _h.Sound.Received().StopAll();
    }

    [Theory]
    [InlineData(DisconnectReason.Error, "Se ha perdido la conexión.")]
    [InlineData(DisconnectReason.Timeout, "La conexión ha superado el tiempo de espera.")]
    public async Task ConnectionLost_SaysWhy(DisconnectReason reason, string expected)
    {
        await _h.StartAsync();

        await _h.Connection.RaiseServerClosed(reason);
        await _h.Session.WhenIdleAsync();

        _h.SystemLines.Should().Equal(expected);
    }

    [Fact]
    public async Task ServerCloses_InPasswordMode_LeavesPasswordMode()
    {
        await _h.StartAsync();
        await _h.ReceiveBytesAsync(255, 251, 1);

        await _h.Connection.RaiseServerClosed();
        await _h.Session.WhenIdleAsync();

        _h.Session.PasswordMode.Should().BeFalse();
        _h.PasswordModes.Should().Equal(true, false);
    }

    [Fact]
    public async Task Reconnect_StartsClean_NoLeftoversFromTheOldConnection()
    {
        await _h.StartAsync();
        // Half a telnet sequence, half a UTF-8 character, half a line and an open colour.
        await _h.ReceiveAsync($"{SessionHarness.Esc}[31mresto sin terminar");
        await _h.ReceiveBytesAsync(0xC3);
        await _h.ReceiveBytesAsync(255, 250, 201, (byte)'x');
        await _h.Connection.RaiseServerClosed();
        await _h.Session.WhenIdleAsync();
        _h.ClearOutput();

        await _h.Session.ConnectAsync();
        await _h.ReceiveAsync("Bienvenido de nuevo\n");

        _h.Connection.ConnectCount.Should().Be(2);
        _h.Session.State.Should().Be(SessionState.Connected);
        _h.Lines.Single().PlainText.Should().Be("Bienvenido de nuevo");
        _h.Lines.Single().Segments.Single().Style.Should().Be(Omnimud.Core.Text.AnsiStyle.Default);
        _h.SentLines.Should().Equal("Zork", "secreto");
    }

    [Fact]
    public async Task Reconnect_TelnetOptionsAreAnsweredAgain()
    {
        await _h.StartAsync();
        await _h.ReceiveBytesAsync(255, 253, 31);
        await _h.Connection.RaiseServerClosed();
        await _h.Session.WhenIdleAsync();
        await _h.Session.ConnectAsync();
        _h.Connection.ClearSent();

        await _h.ReceiveBytesAsync(255, 253, 31);

        _h.SentTelnet.Should().ContainSingle().Which.Should().Equal(255, 252, 31);
    }

    // ── Close ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Close_WithSaveAndQuit_SendsSaveThenQuit_ThenDisconnects()
    {
        await _h.StartAsync();

        await _h.Session.CloseAsync(sendSaveAndQuit: true);

        _h.SentLines.Should().Equal("salvar", "abandonar");
        _h.Connection.State.Should().Be(ConnectionState.Disconnected);
        _h.Session.State.Should().Be(SessionState.Disconnected);
        _h.Sound.Received().StopAll();
        _h.SystemLines.Should().BeEmpty(); // closing on purpose is not "the server closed the connection"
    }

    [Fact]
    public async Task Close_WaitsTheGraceDelay_AndShowsTheFarewell()
    {
        await using var slow = new SessionHarness();
        slow.Session = new MudSession(slow.Profile with { LoginScript = "x" }, slow.Connection, slow.Store, slow.OptionsService, slow.Sound, slow.Scripts, slow.Time,
            new MudSessionSettings { LogDirectory = slow.LogDirectory, SoundsDirectory = slow.LogDirectory, AppSoundsDirectory = slow.LogDirectory });
        var lines = new List<string>();
        slow.Session.LineReceived += l => { lock (lines) lines.Add(l.PlainText); };
        await slow.Session.InitializeAsync();
        await slow.Session.ConnectAsync();

        var close = slow.Session.CloseAsync(true);
        await slow.WaitUntilAsync(() => slow.SentLines.Contains("abandonar"));
        await slow.Connection.RaiseData(Encoding.UTF8.GetBytes("¡Hasta pronto!\n"));
        await slow.Session.WhenIdleAsync();
        close.IsCompleted.Should().BeFalse();
        slow.Connection.State.Should().Be(ConnectionState.Connected);

        while (!close.IsCompleted)
        {
            slow.Time.Advance(TimeSpan.FromMilliseconds(100));
            await Task.Delay(5);
        }

        await close;
        lock (lines) lines.Should().Equal("¡Hasta pronto!");
        slow.Connection.State.Should().Be(ConnectionState.Disconnected);
    }

    [Fact]
    public async Task Close_OptionOff_OrNotAsked_SendsNothing()
    {
        _h.SetOptions(o => o with { TrySaveBeforeExit = false });
        await _h.StartAsync();
        await _h.Session.CloseAsync(sendSaveAndQuit: true);
        _h.SentLines.Should().BeEmpty();

        await using var other = new SessionHarness();
        await other.StartAsync();
        await other.Session.CloseAsync(sendSaveAndQuit: false);
        other.SentLines.Should().BeEmpty();
        other.Session.State.Should().Be(SessionState.Disconnected);
    }

    [Fact]
    public async Task Close_WithoutSaveCommand_SendsOnlyQuit()
    {
        _h.Profile = _h.Profile with { SaveCommand = " " };
        await _h.StartAsync();

        await _h.Session.CloseAsync(true);

        _h.SentLines.Should().Equal("abandonar");
    }

    [Fact]
    public async Task Close_Offline_KeepsOfflineState_AndDoesNotTouchTheConnection()
    {
        await _h.StartAsync(connect: false);
        _h.Session.EnterOfflineMode();

        await _h.Session.CloseAsync(true);

        _h.Session.State.Should().Be(SessionState.Offline);
        _h.Connection.SentPackets.Should().BeEmpty();
    }

    [Fact]
    public async Task Dispose_DisconnectsAndDisposesWhatTheSessionOwns_AndLaterCallsAreHarmless()
    {
        await _h.StartAsync();

        await _h.Session.DisposeAsync();
        await _h.Session.DisposeAsync();
        await _h.Session.SubmitInputAsync("mirar");
        _h.Session.Send("mirar");
        _h.Session.Display("texto");
        _h.OptionsService.RaiseChanged(OptionScope.Global, null);

        _h.Connection.Disposed.Should().BeTrue();
        _h.Scripts.Received(1).Dispose();
        _h.Sound.Received(1).Dispose();
        _h.SentLines.Should().BeEmpty();
    }

    [Fact]
    public async Task Dispose_CancelsRunningScripts()
    {
        var cancelled = new TaskCompletionSource();
        _h.Scripts.ExecuteAsync(Arg.Any<string>(), Arg.Any<ScriptContext>(), Arg.Any<IScriptHost>(), Arg.Any<ScriptLimits?>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var token = call.Arg<CancellationToken>();
                using var registration = token.Register(() => cancelled.TrySetResult());
                await cancelled.Task;
                return ScriptResult.Ok([], [], []);
            });
        _h.Store.Triggers.Add(SessionHarness.Trigger("orco", "eterno()", Omnimud.Core.Triggers.TriggerActionType.Script));
        await _h.StartAsync();
        await _h.ReceiveAsync("orco\n");

        await _h.Session.DisposeAsync();

        (await Task.WhenAny(cancelled.Task, Task.Delay(5000))).Should().Be(cancelled.Task);
    }

    // ── Reload and option changes ──────────────────────────────────────────

    [Fact]
    public async Task Reload_PicksUpEditedAliasesTriggersRulesAndOptions()
    {
        await _h.StartAsync();
        await _h.SubmitAsync("k orco");

        _h.Store.Aliases.Add(new AliasDefinition("k", "matar"));
        _h.Store.Triggers.Add(SessionHarness.Trigger("dragón", "huir"));
        _h.Store.Rules.Add(new MessageRule("dragón", "$0"));
        _h.SetOptions(o => o with { HistorySize = 1 });
        await _h.Session.ReloadAsync();
        await _h.SubmitAsync("k orco");
        await _h.ReceiveAsync("Un dragón\n");

        _h.SentLines.Should().Equal("k orco", "matar orco", "huir");
        _h.AddedMessages.Should().ContainSingle();
        _h.Session.History.Should().Equal("k orco");
    }

    [Fact]
    public async Task Reload_KeepsSessionState()
    {
        await _h.StartAsync();
        await _h.SubmitAsync("-triggers");
        await _h.SubmitAsync("callate");

        await _h.Session.ReloadAsync();

        _h.Session.TriggersEnabled.Should().BeFalse();
        _h.Session.SilentMode.Should().BeTrue();
        _h.Session.State.Should().Be(SessionState.Connected);
    }

    [Theory]
    [InlineData(OptionScope.Global, null, true)]
    [InlineData(OptionScope.Mud, 1, true)]
    [InlineData(OptionScope.Mud, 99, false)]
    [InlineData(OptionScope.Character, 7, true)]
    [InlineData(OptionScope.Character, 8, false)]
    public async Task OptionsChanged_ReResolvesOnlyWhenItAffectsThisSession(OptionScope scope, int? id, bool applies)
    {
        await _h.StartAsync();
        _h.SetOptions(o => o with { UseRepeatChar = true, Volume = 10 });

        _h.OptionsService.RaiseChanged(scope, id);
        await _h.Session.WhenIdleAsync();
        await _h.SubmitAsync("2#n");

        _h.SentLines.Should().HaveCount(applies ? 2 : 1);
        _h.Sound.Received(applies ? 1 : 0).Configure(Arg.Is<SoundSettings>(s => s.Volume == 10));
    }

    [Fact]
    public async Task WindowActive_IsForwardedToSound()
    {
        await _h.StartAsync(windowActive: true);

        _h.Session.IsWindowActive = false;

        _h.Sound.Received().IsWindowActive = true;
        _h.Sound.Received().IsWindowActive = false;
    }

    // ── Log ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Log_PerDay_HasReceivedAndSentText_WithoutAnsi_AndTheFinalLine()
    {
        _h.SetOptions(o => o with { LogType = LogMode.PerDay });
        await _h.StartAsync();

        await _h.ReceiveAsync($"{SessionHarness.Esc}[32mEstás en una plaza.{SessionHarness.Esc}[0m\nall_speak:aviso\n\n> ");
        await _h.AdvanceAsync(150);
        await _h.SubmitAsync("mirar");
        await _h.SubmitAsync("cls");
        await _h.Session.CloseAsync(false);

        LogFiles().Single().Should().EndWith(Path.Combine("Reinos", "Zork", "2026-03-14.log"));
        // The login ("Zork", the automatic name) is not logged.
        File.ReadAllLines(LogFiles().Single()).Should().Equal(
            "Estás en una plaza.",
            "aviso",
            "",
            "> ",
            "mirar",
            "OK",
            "Partida finalizada el 14/03/2026 a las 10:30:00.");
    }

    [Fact]
    public async Task Log_NeverContainsPasswords()
    {
        _h.SetOptions(o => o with { LogType = LogMode.PerDay });
        await _h.StartAsync();

        await _h.ReceiveBytesAsync(255, 251, 1);
        await _h.SubmitAsync("otra-clave");
        await _h.ReceiveBytesAsync(255, 252, 1);
        await _h.SubmitAsync("mirar");
        await _h.Session.CloseAsync(false);

        var log = ReadLog();
        log.Should().NotContain("secreto").And.NotContain("otra-clave");
        log.Should().NotContain("Zork", "no line of the automatic login is logged").And.Contain("mirar");
    }

    [Fact]
    public async Task Log_None_CreatesNoFile()
    {
        await _h.StartAsync();

        await _h.ReceiveAsync("texto\n");
        await _h.Session.CloseAsync(false);

        LogFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Log_PerSession_UsesOneFilePerConnection()
    {
        _h.SetOptions(o => o with { LogType = LogMode.PerSession });
        await _h.StartAsync();
        await _h.ReceiveAsync("primera\n");
        await _h.Connection.RaiseServerClosed();
        await _h.Session.WhenIdleAsync();

        _h.Time.Advance(TimeSpan.FromMinutes(1));
        await _h.Session.ConnectAsync();
        await _h.ReceiveAsync("segunda\n");
        await _h.Session.CloseAsync(false);

        LogFiles().Select(Path.GetFileName).Should().BeEquivalentTo(["2026-03-14 10-30-00.log", "2026-03-14 10-31-00.log"]);
        File.ReadAllLines(LogFiles().Order().First()).Should().Equal(
            "primera", "El servidor ha cerrado la conexión.", "Partida finalizada el 14/03/2026 a las 10:30:00.");
    }

    [Fact]
    public async Task Log_GaggedLinesAreNotLogged()
    {
        _h.SetOptions(o => o with { LogType = LogMode.PerDay });
        _h.Store.Triggers.Add(SessionHarness.Trigger("spam", "", Omnimud.Core.Triggers.TriggerActionType.PlaySound, gag: true, sound: "x"));
        await _h.StartAsync();

        await _h.ReceiveAsync("spam\nútil\n");
        await _h.Session.CloseAsync(false);

        ReadLog().Should().Contain("útil").And.NotContain("spam");
    }

    [Fact]
    public async Task Log_NoLineOfTheLogin_IsLogged_NameIncluded()
    {
        // The original did not log the login at all; the name identifies the player as much as the password does.
        _h.SetOptions(o => o with { LogType = LogMode.PerDay });
        _h.Profile = _h.Profile with { LoginScript = "conectar %character\n%password\nidioma es" };
        await _h.StartAsync(connect: false);

        await _h.Session.ConnectAsync();
        await _h.Session.CloseAsync(false);

        _h.SentLines.Should().Equal("conectar Zork", "secreto", "idioma es");
        ReadLog().Should().Contain("idioma es").And.NotContain("Zork").And.NotContain("secreto");
    }

    [Fact]
    public async Task Log_OptionChangeWhileConnected_SwitchesTheLogOnAndOff()
    {
        await _h.StartAsync();
        await _h.ReceiveAsync("sin log\n");

        _h.SetOptions(o => o with { LogType = LogMode.PerDay });
        _h.OptionsService.RaiseChanged(OptionScope.Global, null);
        await _h.Session.WhenIdleAsync();
        await _h.ReceiveAsync("con log\n");
        _h.SetOptions(o => o with { LogType = LogMode.None });
        _h.OptionsService.RaiseChanged(OptionScope.Global, null);
        await _h.Session.WhenIdleAsync();
        await _h.ReceiveAsync("otra vez sin log\n");

        File.ReadAllLines(LogFiles().Single()).Should().Equal("con log");
    }

    [Fact]
    public async Task Log_CustomDirectoryFromOptions()
    {
        var custom = Path.Combine(_h.LogDirectory, "personalizado");
        _h.SetOptions(o => o with { LogType = LogMode.PerDay, LogDirectory = custom });
        await _h.StartAsync();

        await _h.ReceiveAsync("texto\n");
        await _h.Session.CloseAsync(false);

        LogFiles().Single().Should().StartWith(Path.Combine(custom, "Reinos", "Zork"));
    }

    // ── Factory ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Factory_CreatesIndependentSessions_EachWithItsOwnConnectionSoundAndEngine()
    {
        var connections = new List<FakeConnection>();
        var factory = new MudSessionFactory(
            _h.Store, _h.OptionsService,
            () => { var c = new FakeConnection(); connections.Add(c); return c; },
            () => Substitute.For<ISessionSound>(),
            () => Substitute.For<IScriptEngine>(),
            _h.Time,
            new MudSessionSettings { LogDirectory = _h.LogDirectory, SoundsDirectory = _h.LogDirectory, AppSoundsDirectory = _h.LogDirectory, LoginLineDelay = TimeSpan.Zero });

        await using var first = factory.Create(_h.Profile);
        await using var second = factory.Create(_h.Profile with { Title = "Otro" });
        await first.InitializeAsync();
        await first.ConnectAsync();

        connections.Should().HaveCount(2);
        first.State.Should().Be(SessionState.Connected);
        second.State.Should().Be(SessionState.Disconnected);
        second.Profile.Title.Should().Be("Otro");
    }
}
