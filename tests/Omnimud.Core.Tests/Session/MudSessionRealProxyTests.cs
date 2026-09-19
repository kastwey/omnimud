using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using FluentAssertions;
using NSubstitute;
using Omnimud.Core.Connection;
using Omnimud.Core.Options;
using Omnimud.Core.Scripting;
using Omnimud.Core.Security;
using Omnimud.Core.Session;
using Omnimud.Core.Sound;
using Omnimud.Core.Tests.Connection;

namespace Omnimud.Core.Tests.Session;

/// <summary>
/// End to end: a real <see cref="MudSession"/> with a real <see cref="TelnetConnection"/>, the real
/// <see cref="ProxySettingsResolver"/> with a password protected by <see cref="AesPasswordProtector"/>, a real
/// proxy server with user and password (<see cref="RealProxy"/>) and a fake MUD on TCP. All on loopback.
/// </summary>
public sealed class MudSessionRealProxyTests : IAsyncDisposable
{
    private const string User = "juan";
    private const string Password = "s3cret-Passw0rd!";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "omnimud-tests", "proxy-" + Guid.NewGuid().ToString("N"));
    private readonly LoopbackServer _mud = new();
    private readonly RealProxy _proxy = new(User, Password);
    private readonly FakeOptionsService _options = new();
    private readonly ProxyCredentialStore _credentials = new(new AesPasswordProtector(RandomNumberGenerator.GetBytes(32)));
    private readonly ConcurrentQueue<string> _lines = new();
    private readonly List<MudSession> _sessions = [];

    public MudSessionRealProxyTests() => CultureInfo.CurrentUICulture = new CultureInfo("es");

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions)
            await session.DisposeAsync();
        await _proxy.DisposeAsync();
        await _mud.DisposeAsync();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private OmnimudOptions Manual(ProxyProtocol protocol, string? password, bool useForMud = true) =>
        _credentials.WithPassword(OmnimudOptions.Default with
        {
            LogType = LogMode.None,
            ProxyType = ProxyMode.Manual,
            ProxyHost = "127.0.0.1",
            ProxyPort = protocol == ProxyProtocol.HttpConnect ? _proxy.HttpPort : _proxy.SocksPort,
            ProxyProtocol = protocol,
            ProxyUsername = User,
            UseProxyForMud = useForMud
        }, password);

    private async Task<MudSession> StartAsync(OmnimudOptions options, ISystemProxyResolver? system = null)
    {
        _options.Current = options;
        var session = new MudSession(
            new SessionProfile
            {
                Title = "Falso", Host = "127.0.0.1", Port = _mud.Port, MudId = 1, MudName = "Falso",
                CharacterId = 7, CharacterName = "Zork" // the login script makes the client speak first
            },
            new TelnetConnection(), new FakeStore(), _options, Substitute.For<ISessionSound>(), Substitute.For<IScriptEngine>(),
            TimeProvider.System,
            new MudSessionSettings
            {
                LogDirectory = _root,
                SoundsDirectory = Path.Combine(_root, "sounds"),
                AppSoundsDirectory = Path.Combine(_root, "appsounds"),
                QuitGraceDelay = TimeSpan.Zero,
                LoginLineDelay = TimeSpan.Zero
            },
            new ProxySettingsResolver(_credentials, system ?? NoSystemProxyResolver.Instance));
        session.LineReceived += line => _lines.Enqueue(line.PlainText);
        _sessions.Add(session);
        await session.InitializeAsync();
        return session;
    }

    /// <summary>The MUD gets the character name and answers; the session shows the answer.</summary>
    private async Task AssertPlayableAsync(MudSession session)
    {
        var peer = await _mud.AcceptAsync();
        (await peer.ReadBytesAsync(5)).Should().Equal("Zork\n"u8.ToArray());
        await peer.WriteAsync("Bienvenido, Zork.\r\n"u8.ToArray());
        await WaitForLineAsync("Bienvenido, Zork.");

        await session.SubmitInputAsync("mirar");
        (await peer.ReadBytesAsync(6)).Should().Equal("mirar\n"u8.ToArray());
        session.State.Should().Be(SessionState.Connected);
    }

    private async Task WaitForLineAsync(string text)
    {
        var limit = DateTime.UtcNow + TestTimeouts.Safety;
        while (!_lines.Contains(text))
        {
            if (DateTime.UtcNow > limit)
                throw new TimeoutException($"The session never showed '{text}'. It showed: {string.Join(" | ", _lines)}");
            await Task.Delay(10);
        }
    }

    [Theory]
    [InlineData(ProxyProtocol.HttpConnect)]
    [InlineData(ProxyProtocol.Socks5)]
    public async Task ManualProxy_WithStoredPassword_TheMudTrafficGoesThroughTheProxy(ProxyProtocol protocol)
    {
        var session = await StartAsync(Manual(protocol, Password));

        await session.ConnectAsync().OrTimeout();

        await AssertPlayableAsync(session);
        if (protocol == ProxyProtocol.HttpConnect)
        {
            _proxy.Connects.Should().Equal($"127.0.0.1:{_mud.Port}");
            _proxy.HttpLogins.Should().Equal(User);
        }
        else
        {
            _proxy.SocksLogins.Should().Equal(User);
        }
    }

    [Fact]
    public async Task UseProxyForMudOff_TheMudTrafficDoesNotTouchTheProxy()
    {
        var session = await StartAsync(Manual(ProxyProtocol.HttpConnect, Password, useForMud: false));

        await session.ConnectAsync().OrTimeout();

        await AssertPlayableAsync(session);
        _proxy.Connects.Should().BeEmpty();
        _proxy.HttpLogins.Should().BeEmpty();
        _proxy.SocksLogins.Should().BeEmpty();
    }

    [Fact]
    public async Task WrongPassword_FailsWithAClearLocalizedMessage_AndReconnectingAfterFixingTheOptionsWorks()
    {
        var session = await StartAsync(Manual(ProxyProtocol.HttpConnect, "la-que-no-es"));

        var act = () => session.ConnectAsync().OrTimeout();

        var error = (await act.Should().ThrowAsync<ProxyException>()).Which;
        error.Kind.Should().Be(ProxyErrorKind.AuthenticationFailed);
        session.State.Should().Be(SessionState.Disconnected);
        var message = ConnectionErrorDescriber.Describe(error);
        message.Should().StartWith("El proxy (HTTP CONNECT) pide usuario y contraseña");
        (message + error).Should().NotContain("la-que-no-es").And.NotContain(Password);

        // The user fixes the password in the options dialog and presses Reconnect: no restart needed.
        await _options.SaveAsync(OptionScope.Global, null, Manual(ProxyProtocol.HttpConnect, Password));
        await session.WhenIdleAsync();
        await session.ConnectAsync().OrTimeout();

        await AssertPlayableAsync(session);
        _proxy.Connects.Should().Equal($"127.0.0.1:{_mud.Port}", $"127.0.0.1:{_mud.Port}");
    }

    [Fact]
    public async Task SwitchingTheProxyOffInTheOptions_TheNextConnectionIsDirect()
    {
        var session = await StartAsync(Manual(ProxyProtocol.HttpConnect, Password));
        await session.ConnectAsync().OrTimeout();
        await AssertPlayableAsync(session);
        _proxy.Connects.Should().HaveCount(1);

        await session.CloseAsync(sendSaveAndQuit: false);
        await _options.SaveAsync(OptionScope.Global, null, _options.Current with { ProxyType = ProxyMode.Disabled });
        await session.WhenIdleAsync();
        await session.ConnectAsync().OrTimeout();

        await AssertPlayableAsync(session);
        _proxy.Connects.Should().HaveCount(1, "the second connection did not go through the proxy");
    }

    [Fact]
    public async Task PasswordProtectedWithAnotherKey_CountsAsNoPassword_AndNothingThrowsButTheProxy()
    {
        // The data folder was moved to another computer: the master key is not the one that protected the password.
        var foreign = new ProxyCredentialStore(new AesPasswordProtector(RandomNumberGenerator.GetBytes(32)));
        var options = foreign.WithPassword(Manual(ProxyProtocol.HttpConnect, null), Password);
        var session = await StartAsync(options);

        var act = () => session.ConnectAsync().OrTimeout();

        (await act.Should().ThrowAsync<ProxyException>()).Which.Kind.Should().Be(ProxyErrorKind.AuthenticationFailed);
        _proxy.HttpLogins.Should().Equal(User);
    }

    [Fact]
    public async Task AutomaticMode_UsesTheProxyOfTheSystem_WithTheStoredCredentials()
    {
        var system = Substitute.For<ISystemProxyResolver>();
        system.Resolve("127.0.0.1", _mud.Port).Returns(new SystemProxy("127.0.0.1", _proxy.HttpPort, ProxyProtocol.HttpConnect));
        var options = Manual(ProxyProtocol.Socks5, Password) with { ProxyType = ProxyMode.Automatic, ProxyHost = null, ProxyPort = 0 };
        var session = await StartAsync(options, system);

        await session.ConnectAsync().OrTimeout();

        await AssertPlayableAsync(session);
        _proxy.Connects.Should().Equal($"127.0.0.1:{_mud.Port}");
        _proxy.HttpLogins.Should().Equal(User);
    }

    [Fact]
    public async Task AutomaticMode_WithTheRealDecisionLogic_NeverProxiesAMudOnThisMachine()
    {
        // The system says "everything through the proxy", but the MUD is on 127.0.0.1.
        var source = Substitute.For<ISystemProxySource>();
        source.ReadSettings().Returns(new SystemProxySettings(true, $"127.0.0.1:{_proxy.HttpPort}", null));
        var options = Manual(ProxyProtocol.HttpConnect, Password) with { ProxyType = ProxyMode.Automatic };
        var session = await StartAsync(options, new SystemProxyResolver(source));

        await session.ConnectAsync().OrTimeout();

        await AssertPlayableAsync(session);
        _proxy.Connects.Should().BeEmpty();
    }
}
