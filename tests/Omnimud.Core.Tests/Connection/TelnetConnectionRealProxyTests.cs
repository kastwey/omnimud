using FluentAssertions;
using Omnimud.Core.Connection;
using Omnimud.Core.Options;

namespace Omnimud.Core.Tests.Connection;

/// <summary>
/// <see cref="TelnetConnection"/> against a real proxy server (see <see cref="RealProxy"/>) with user and
/// password, everything on loopback. The hand-written fakes of <see cref="TelnetConnectionProxyTests"/> keep
/// covering what a well-behaved real proxy never does (garbage, silence, odd reply codes, data glued to the headers).
/// </summary>
public sealed class TelnetConnectionRealProxyTests
{
    private const string User = "juan";
    private const string Password = "s3cret-Passw0rd!";
    private const string Mud = "127.0.0.1";

    private static ConnectionConfig Through(int proxyPort, ProxyProtocol protocol, int mudPort, string? user, string? password) =>
        new(Mud, mudPort, Proxy: new ProxyConfig("127.0.0.1", proxyPort, protocol, user, password));

    private static int Port(RealProxy proxy, ProxyProtocol protocol) =>
        protocol == ProxyProtocol.HttpConnect ? proxy.HttpPort : proxy.SocksPort;

    /// <summary>Client speaks first (see the note in <see cref="RealProxy"/>), then the MUD answers: both directions.</summary>
    private static async Task AssertRoundTripAsync(TelnetConnection connection, ConnectionProbe probe, ServerPeer peer)
    {
        await connection.SendAsync("look").OrTimeout();
        (await peer.ReadBytesAsync(6)).Should().Equal("look\r\n"u8.ToArray());
        await peer.WriteAsync("A room.\r\n"u8.ToArray());
        (await probe.WaitForBytesAsync(9)).Should().Equal("A room.\r\n"u8.ToArray());

        await peer.WriteAsync("Rain.\r\n"u8.ToArray());
        (await probe.WaitForBytesAsync(16))[9..].Should().Equal("Rain.\r\n"u8.ToArray());
        await connection.SendAsync("north").OrTimeout();
        (await peer.ReadBytesAsync(7)).Should().Equal("north\r\n"u8.ToArray());
    }

    [Theory]
    [InlineData(ProxyProtocol.HttpConnect)]
    [InlineData(ProxyProtocol.Socks5)]
    public async Task RightCredentials_TunnelDataBothWays(ProxyProtocol protocol)
    {
        await using var server = new LoopbackServer();
        await using var proxy = new RealProxy(User, Password);
        await using var connection = new TelnetConnection();
        var probe = new ConnectionProbe(connection);

        await connection.ConnectAsync(Through(Port(proxy, protocol), protocol, server.Port, User, Password)).OrTimeout();
        await connection.SendAsync("hi").OrTimeout();
        var peer = await server.AcceptAsync();
        (await peer.ReadBytesAsync(4)).Should().Equal("hi\r\n"u8.ToArray());

        await AssertRoundTripAsync(connection, probe, peer);
        connection.State.Should().Be(ConnectionState.Connected);
        if (protocol == ProxyProtocol.HttpConnect)
        {
            proxy.Connects.Should().Equal($"{Mud}:{server.Port}");
            proxy.HttpLogins.Should().Equal(User);
        }
        else
        {
            proxy.SocksLogins.Should().Equal(User);
        }
    }

    [Theory]
    [InlineData(ProxyProtocol.HttpConnect)]
    [InlineData(ProxyProtocol.Socks5)]
    public async Task ProxyWithoutAuthentication_NoCredentials_Tunnels(ProxyProtocol protocol)
    {
        await using var server = new LoopbackServer();
        await using var proxy = new RealProxy();
        await using var connection = new TelnetConnection();
        var probe = new ConnectionProbe(connection);

        await connection.ConnectAsync(Through(Port(proxy, protocol), protocol, server.Port, null, null)).OrTimeout();
        await connection.SendAsync("hi").OrTimeout();
        var peer = await server.AcceptAsync();
        (await peer.ReadBytesAsync(4)).Should().Equal("hi\r\n"u8.ToArray());

        await AssertRoundTripAsync(connection, probe, peer);
    }

    [Theory]
    [InlineData(ProxyProtocol.HttpConnect)]
    [InlineData(ProxyProtocol.Socks5)]
    public async Task RightCredentials_TlsInsideTheTunnel_Works(ProxyProtocol protocol)
    {
        using var certificate = TestCertificates.CreateSelfSigned("mud.example.test");
        await using var server = new LoopbackServer();
        await using var proxy = new RealProxy(User, Password);
        await using var connection = new TelnetConnection();
        var probe = new ConnectionProbe(connection);
        var config = Through(Port(proxy, protocol), protocol, server.Port, User, Password) with
        {
            UseTls = true,
            TlsOptions = new TlsConfig(ValidateCertificate: false) { TargetHost = "mud.example.test" }
        };

        var serverSide = server.AcceptTlsAsync(certificate);
        await connection.ConnectAsync(config).OrTimeout();
        var peer = await serverSide;

        peer.SniHostName.Should().Be("mud.example.test", "the proxy relays the TLS bytes untouched: it never decrypts");
        await AssertRoundTripAsync(connection, probe, peer);
    }

    [Theory]
    [InlineData(ProxyProtocol.HttpConnect)]
    [InlineData(ProxyProtocol.Socks5)]
    public async Task WrongPassword_ThrowsAuthenticationFailed_AndNeverMentionsThePassword(ProxyProtocol protocol)
    {
        const string wrong = "wr0ng-and-very-recognisable";
        await using var server = new LoopbackServer();
        await using var proxy = new RealProxy(User, Password);
        await using var connection = new TelnetConnection();
        var probe = new ConnectionProbe(connection);

        var act = () => connection.ConnectAsync(Through(Port(proxy, protocol), protocol, server.Port, User, wrong)).OrTimeout();

        var error = (await act.Should().ThrowAsync<ProxyException>()).Which;
        error.Kind.Should().Be(ProxyErrorKind.AuthenticationFailed);
        error.Protocol.Should().Be(protocol);
        error.ToString().Should().NotContain(wrong).And.NotContain(Password);
        ConnectionErrorDescriber.Describe(error).Should().NotContain(wrong).And.NotContain(Password);
        connection.State.Should().Be(ConnectionState.Disconnected);
        probe.Disconnects.Should().BeEmpty("the connection never existed");
    }

    [Theory]
    [InlineData(ProxyProtocol.HttpConnect)]
    [InlineData(ProxyProtocol.Socks5)]
    public async Task NoCredentials_WhenTheProxyDemandsThem_ThrowsAuthenticationFailed(ProxyProtocol protocol)
    {
        await using var server = new LoopbackServer();
        await using var proxy = new RealProxy(User, Password);
        await using var connection = new TelnetConnection();

        var act = () => connection.ConnectAsync(Through(Port(proxy, protocol), protocol, server.Port, null, null)).OrTimeout();

        var error = (await act.Should().ThrowAsync<ProxyException>()).Which;
        error.Kind.Should().Be(ProxyErrorKind.AuthenticationFailed);
        error.Message.Should().Contain("requires authentication");
        connection.State.Should().Be(ConnectionState.Disconnected);
    }

    [Theory]
    [InlineData(ProxyProtocol.HttpConnect)]
    [InlineData(ProxyProtocol.Socks5)]
    public async Task ProxyDown_ThrowsConnectionFailed(ProxyProtocol protocol)
    {
        int deadPort;
        await using (var proxy = new RealProxy(User, Password))
            deadPort = Port(proxy, protocol);
        await using var server = new LoopbackServer();
        await using var connection = new TelnetConnection();

        var act = () => connection.ConnectAsync(Through(deadPort, protocol, server.Port, User, Password)).OrTimeout();

        var error = (await act.Should().ThrowAsync<ProxyException>()).Which;
        error.Kind.Should().Be(ProxyErrorKind.ConnectionFailed);
        error.ToString().Should().NotContain(Password);
        connection.State.Should().Be(ConnectionState.Disconnected);
    }

    [Fact]
    public async Task HttpConnect_NonAsciiCredentials_TravelAsUtf8()
    {
        // Only over HTTP: RFC 1929 (SOCKS5) says nothing about the encoding and this server does not read it as
        // UTF-8, which is what Omnimud sends (see Socks5_UserPassword_AuthenticatesAndTunnels for the bytes).
        await using var server = new LoopbackServer();
        await using var proxy = new RealProxy("josé", "contraseña-ñ");
        await using var connection = new TelnetConnection();
        var probe = new ConnectionProbe(connection);

        await connection.ConnectAsync(Through(proxy.HttpPort, ProxyProtocol.HttpConnect, server.Port, "josé", "contraseña-ñ")).OrTimeout();
        await connection.SendAsync("hi").OrTimeout();
        var peer = await server.AcceptAsync();
        (await peer.ReadBytesAsync(4)).Should().Equal("hi\r\n"u8.ToArray());

        await AssertRoundTripAsync(connection, probe, peer);
    }

    [Fact]
    public async Task AfterAFailedAuthentication_TheSameConnectionCanRetryWithTheRightPassword()
    {
        await using var server = new LoopbackServer();
        await using var proxy = new RealProxy(User, Password);
        await using var connection = new TelnetConnection();
        var probe = new ConnectionProbe(connection);

        var first = () => connection.ConnectAsync(Through(proxy.HttpPort, ProxyProtocol.HttpConnect, server.Port, User, "nope")).OrTimeout();
        await first.Should().ThrowAsync<ProxyException>();

        await connection.ConnectAsync(Through(proxy.HttpPort, ProxyProtocol.HttpConnect, server.Port, User, Password)).OrTimeout();
        await connection.SendAsync("hi").OrTimeout();
        var peer = await server.AcceptAsync();
        (await peer.ReadBytesAsync(4)).Should().Equal("hi\r\n"u8.ToArray());
        await AssertRoundTripAsync(connection, probe, peer);
    }
}
