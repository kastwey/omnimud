using System.Text;
using FluentAssertions;
using Omnimud.Core.Connection;
using Omnimud.Core.Options;

namespace Omnimud.Core.Tests.Connection;

public sealed class TelnetConnectionProxyTests
{
    // Never resolvable locally (.test is reserved): reaching the server proves the proxy got the name.
    private const string MudHost = "mud.example.test";
    private const int MudPort = 4000;

    private static ConnectionConfig Through(FakeProxy proxy, ProxyProtocol protocol, string? user = null, string? password = null) =>
        new(MudHost, MudPort, Proxy: new ProxyConfig("127.0.0.1", proxy.Port, protocol, user, password));

    private static async Task AssertRoundTripAsync(TelnetConnection connection, ConnectionProbe probe, ServerPeer peer)
    {
        await connection.SendAsync("look").OrTimeout();
        (await peer.ReadBytesAsync(6)).Should().Equal("look\r\n"u8.ToArray());
        await peer.WriteAsync("A room.\r\n"u8.ToArray());
        (await probe.WaitForBytesAsync(9)).Should().Equal("A room.\r\n"u8.ToArray());
    }

    // ── SOCKS5 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Socks5_NoAuthentication_TunnelsUsingDomainName()
    {
        await using var server = new LoopbackServer();
        await using var proxy = new FakeSocks5Proxy(server.Port);
        await using var connection = new TelnetConnection();
        var probe = new ConnectionProbe(connection);

        await connection.ConnectAsync(Through(proxy, ProxyProtocol.Socks5)).OrTimeout();
        var peer = await server.AcceptAsync();

        proxy.OfferedMethods.Should().Equal(0x00);
        proxy.RequestedAddressType.Should().Be(0x03, "the proxy must resolve the name");
        proxy.RequestedHost.Should().Be(MudHost);
        proxy.RequestedPort.Should().Be(MudPort);
        await AssertRoundTripAsync(connection, probe, peer);
    }

    [Fact]
    public async Task Socks5_IpLiteralTarget_UsesIpv4AddressType()
    {
        await using var server = new LoopbackServer();
        await using var proxy = new FakeSocks5Proxy(server.Port);
        await using var connection = new TelnetConnection();
        var config = new ConnectionConfig("192.0.2.7", 23, Proxy: new ProxyConfig("127.0.0.1", proxy.Port));

        await connection.ConnectAsync(config).OrTimeout();

        proxy.RequestedAddressType.Should().Be(0x01);
        proxy.RequestedHost.Should().Be("192.0.2.7");
        proxy.RequestedPort.Should().Be(23);
    }

    [Fact]
    public async Task Socks5_UserPassword_AuthenticatesAndTunnels()
    {
        await using var server = new LoopbackServer();
        await using var proxy = new FakeSocks5Proxy(server.Port) { RequiredUser = "juan", RequiredPassword = "contraseña" };
        await using var connection = new TelnetConnection();
        var probe = new ConnectionProbe(connection);

        await connection.ConnectAsync(Through(proxy, ProxyProtocol.Socks5, "juan", "contraseña")).OrTimeout();
        var peer = await server.AcceptAsync();

        proxy.OfferedMethods.Should().BeEquivalentTo(new byte[] { 0x00, 0x02 });
        proxy.SeenUser.Should().Be("juan");
        proxy.SeenPassword.Should().Be("contraseña");
        await AssertRoundTripAsync(connection, probe, peer);
    }

    [Fact]
    public async Task Socks5_WrongPassword_ThrowsAuthenticationFailed()
    {
        await using var server = new LoopbackServer();
        await using var proxy = new FakeSocks5Proxy(server.Port) { RequiredUser = "juan", RequiredPassword = "right" };
        await using var connection = new TelnetConnection();

        var act = () => connection.ConnectAsync(Through(proxy, ProxyProtocol.Socks5, "juan", "wrong")).OrTimeout();

        var error = (await act.Should().ThrowAsync<ProxyException>()).Which;
        error.Kind.Should().Be(ProxyErrorKind.AuthenticationFailed);
        error.Protocol.Should().Be(ProxyProtocol.Socks5);
        connection.State.Should().Be(ConnectionState.Disconnected);
    }

    [Fact]
    public async Task Socks5_ProxyRequiresAuthButNoneConfigured_ThrowsAuthenticationFailed()
    {
        await using var server = new LoopbackServer();
        await using var proxy = new FakeSocks5Proxy(server.Port) { RequiredUser = "juan", RequiredPassword = "right" };
        await using var connection = new TelnetConnection();

        var act = () => connection.ConnectAsync(Through(proxy, ProxyProtocol.Socks5)).OrTimeout();

        var error = (await act.Should().ThrowAsync<ProxyException>()).Which;
        error.Kind.Should().Be(ProxyErrorKind.AuthenticationFailed);
        error.Message.Should().Contain("requires authentication");
    }

    [Theory]
    [InlineData(0x02, "not allowed")]
    [InlineData(0x04, "host unreachable")]
    [InlineData(0x05, "connection refused")]
    public async Task Socks5_ConnectRejected_ThrowsRejectedWithReason(byte replyCode, string expectedText)
    {
        await using var server = new LoopbackServer();
        await using var proxy = new FakeSocks5Proxy(server.Port) { ReplyCode = replyCode };
        await using var connection = new TelnetConnection();
        var probe = new ConnectionProbe(connection);

        var act = () => connection.ConnectAsync(Through(proxy, ProxyProtocol.Socks5)).OrTimeout();

        var error = (await act.Should().ThrowAsync<ProxyException>()).Which;
        error.Kind.Should().Be(ProxyErrorKind.Rejected);
        error.StatusCode.Should().Be(replyCode);
        error.Message.Should().Contain(expectedText).And.Contain(MudHost);
        connection.State.Should().Be(ConnectionState.Disconnected);
        probe.Disconnects.Should().BeEmpty();
    }

    [Fact]
    public async Task Socks5_NotASocksServer_ThrowsProtocolError()
    {
        // The "proxy" here is an HTTP proxy, which answers the SOCKS greeting with garbage or silence+close.
        await using var server = new LoopbackServer();
        await using var connection = new TelnetConnection();
        var config = new ConnectionConfig(MudHost, MudPort, Proxy: new ProxyConfig("127.0.0.1", server.Port));

        var connecting = connection.ConnectAsync(config);
        var peer = await server.AcceptAsync();
        await peer.WriteAsync("HTTP/1.1 400 Bad Request\r\n\r\n"u8.ToArray());

        var act = () => connecting.OrTimeout();
        (await act.Should().ThrowAsync<ProxyException>()).Which.Kind.Should().Be(ProxyErrorKind.ProtocolError);
    }

    [Fact]
    public async Task Socks5_SilentProxy_ThrowsTimeoutException()
    {
        await using var server = new LoopbackServer();
        await using var proxy = new FakeSocks5Proxy(server.Port) { Silent = true };
        await using var connection = new TelnetConnection();
        var config = Through(proxy, ProxyProtocol.Socks5) with { ConnectTimeout = TimeSpan.FromMilliseconds(300) };

        var act = () => connection.ConnectAsync(config).OrTimeout();

        await act.Should().ThrowAsync<TimeoutException>();
        connection.State.Should().Be(ConnectionState.Disconnected);
    }

    [Fact]
    public async Task Socks5_TlsOverTheTunnel_Works()
    {
        using var certificate = TestCertificates.CreateSelfSigned(MudHost);
        await using var server = new LoopbackServer();
        await using var proxy = new FakeSocks5Proxy(server.Port) { RequiredUser = "u", RequiredPassword = "p" };
        await using var connection = new TelnetConnection();
        var probe = new ConnectionProbe(connection);
        var config = Through(proxy, ProxyProtocol.Socks5, "u", "p") with
        {
            UseTls = true,
            TlsOptions = new TlsConfig(ValidateCertificate: false)
        };

        var serverSide = server.AcceptTlsAsync(certificate);
        await connection.ConnectAsync(config).OrTimeout();
        var peer = await serverSide;

        peer.SniHostName.Should().Be(MudHost, "SNI must name the MUD, not the proxy");
        await AssertRoundTripAsync(connection, probe, peer);
    }

    // ── HTTP CONNECT ─────────────────────────────────────────────────────────

    [Fact]
    public async Task HttpConnect_200_Tunnels()
    {
        await using var server = new LoopbackServer();
        await using var proxy = new FakeHttpProxy(server.Port);
        await using var connection = new TelnetConnection();
        var probe = new ConnectionProbe(connection);

        await connection.ConnectAsync(Through(proxy, ProxyProtocol.HttpConnect)).OrTimeout();
        var peer = await server.AcceptAsync();

        proxy.RequestLine.Should().Be($"CONNECT {MudHost}:{MudPort} HTTP/1.1");
        proxy.Headers["Host"].Should().Be($"{MudHost}:{MudPort}");
        proxy.Headers.Should().NotContainKey("Proxy-Authorization");
        await AssertRoundTripAsync(connection, probe, peer);
    }

    [Fact]
    public async Task HttpConnect_BasicCredentials_AreSentAndAccepted()
    {
        await using var server = new LoopbackServer();
        await using var proxy = new FakeHttpProxy(server.Port) { RequiredCredentials = "juan:s3cret" };
        await using var connection = new TelnetConnection();
        var probe = new ConnectionProbe(connection);

        await connection.ConnectAsync(Through(proxy, ProxyProtocol.HttpConnect, "juan", "s3cret")).OrTimeout();
        var peer = await server.AcceptAsync();

        proxy.Headers["Proxy-Authorization"].Should().Be("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("juan:s3cret")));
        await AssertRoundTripAsync(connection, probe, peer);
    }

    [Theory]
    [InlineData(null, null, "requires authentication")]
    [InlineData("juan", "wrong", "rejected the user name or password")]
    public async Task HttpConnect_407_ThrowsAuthenticationFailed(string? user, string? password, string expectedText)
    {
        await using var server = new LoopbackServer();
        await using var proxy = new FakeHttpProxy(server.Port) { RequiredCredentials = "juan:s3cret" };
        await using var connection = new TelnetConnection();

        var act = () => connection.ConnectAsync(Through(proxy, ProxyProtocol.HttpConnect, user, password)).OrTimeout();

        var error = (await act.Should().ThrowAsync<ProxyException>()).Which;
        error.Kind.Should().Be(ProxyErrorKind.AuthenticationFailed);
        error.Protocol.Should().Be(ProxyProtocol.HttpConnect);
        error.StatusCode.Should().Be(407);
        error.Message.Should().Contain(expectedText);
        connection.State.Should().Be(ConnectionState.Disconnected);
    }

    [Fact]
    public async Task HttpConnect_403_ThrowsRejectedWithStatusAndReason()
    {
        await using var server = new LoopbackServer();
        await using var proxy = new FakeHttpProxy(server.Port) { ForcedStatus = "403 Forbidden" };
        await using var connection = new TelnetConnection();
        var probe = new ConnectionProbe(connection);

        var act = () => connection.ConnectAsync(Through(proxy, ProxyProtocol.HttpConnect)).OrTimeout();

        var error = (await act.Should().ThrowAsync<ProxyException>()).Which;
        error.Kind.Should().Be(ProxyErrorKind.Rejected);
        error.StatusCode.Should().Be(403);
        error.Message.Should().Contain("403 Forbidden").And.Contain(MudHost);
        probe.Disconnects.Should().BeEmpty();
    }

    [Fact]
    public async Task HttpConnect_DataRightAfterTheHeaders_IsNotSwallowed()
    {
        // Proxy reply and the MUD banner arrive in the same TCP segment.
        await using var server = new LoopbackServer();
        await using var connection = new TelnetConnection();
        var probe = new ConnectionProbe(connection);
        var config = new ConnectionConfig(MudHost, MudPort,
            Proxy: new ProxyConfig("127.0.0.1", server.Port, ProxyProtocol.HttpConnect));

        var connecting = connection.ConnectAsync(config);
        var peer = await server.AcceptAsync();
        await peer.WriteAsync("HTTP/1.0 200 OK\r\nVia: fake\r\n\r\nBanner!"u8.ToArray());
        await connecting.OrTimeout();

        (await probe.WaitForBytesAsync(7)).Should().Equal("Banner!"u8.ToArray());
    }

    [Fact]
    public async Task HttpConnect_TlsOverTheTunnel_Works()
    {
        using var certificate = TestCertificates.CreateSelfSigned(MudHost);
        await using var server = new LoopbackServer();
        await using var proxy = new FakeHttpProxy(server.Port);
        await using var connection = new TelnetConnection();
        var probe = new ConnectionProbe(connection);
        var config = Through(proxy, ProxyProtocol.HttpConnect) with
        {
            UseTls = true,
            TlsOptions = new TlsConfig(ValidateCertificate: false)
        };

        var serverSide = server.AcceptTlsAsync(certificate);
        await connection.ConnectAsync(config).OrTimeout();
        var peer = await serverSide;

        peer.SniHostName.Should().Be(MudHost);
        await AssertRoundTripAsync(connection, probe, peer);
    }

    [Fact]
    public async Task HttpConnect_TlsCertificateProblemThroughTunnel_IsStillReadable()
    {
        using var certificate = TestCertificates.CreateSelfSigned(MudHost);
        await using var server = new LoopbackServer();
        await using var proxy = new FakeHttpProxy(server.Port);
        await using var connection = new TelnetConnection();
        var config = Through(proxy, ProxyProtocol.HttpConnect) with { UseTls = true };

        var serverSide = server.AcceptTlsAsync(certificate);
        var act = () => connection.ConnectAsync(config).OrTimeout();

        (await act.Should().ThrowAsync<TlsHandshakeException>()).Which.IsCertificateError.Should().BeTrue();
        await serverSide.ContinueWith(_ => { });
    }

    // ── proxy configuration ──────────────────────────────────────────────────

    [Fact]
    public async Task Proxy_Unreachable_ThrowsConnectionFailed()
    {
        int closedPort;
        await using (var temp = new LoopbackServer())
            closedPort = temp.Port;
        await using var connection = new TelnetConnection();
        var config = new ConnectionConfig(MudHost, MudPort, Proxy: new ProxyConfig("127.0.0.1", closedPort));

        var act = () => connection.ConnectAsync(config).OrTimeout();

        var error = (await act.Should().ThrowAsync<ProxyException>()).Which;
        error.Kind.Should().Be(ProxyErrorKind.ConnectionFailed);
        error.Message.Should().Contain($"127.0.0.1:{closedPort}");
        connection.State.Should().Be(ConnectionState.Disconnected);
    }

    [Theory]
    [InlineData("", 1080, null, null)]
    [InlineData("127.0.0.1", 0, null, null)]
    [InlineData("127.0.0.1", 70000, null, null)]
    [InlineData("127.0.0.1", 1080, null, "password-without-user")]
    public async Task Proxy_InvalidConfiguration_ThrowsArgumentException(string host, int port, string? user, string? password)
    {
        await using var connection = new TelnetConnection();
        var config = new ConnectionConfig(MudHost, MudPort, Proxy: new ProxyConfig(host, port, ProxyProtocol.Socks5, user, password));

        var act = () => connection.ConnectAsync(config);

        await act.Should().ThrowAsync<ArgumentException>();
        connection.State.Should().Be(ConnectionState.Disconnected);
    }

    [Fact]
    public void ProxyConfig_ToString_HidesThePassword()
    {
        var text = new ProxyConfig("proxy", 1080, ProxyProtocol.Socks5, "juan", "s3cret").ToString();

        text.Should().Contain("juan").And.NotContain("s3cret");
    }

    [Fact]
    public void ProxyConfig_TwoArguments_DefaultsToSocks5WithoutCredentials()
    {
        var proxy = new ProxyConfig("proxy", 1080);

        proxy.Protocol.Should().Be(ProxyProtocol.Socks5);
        proxy.Username.Should().BeNull();
        proxy.Password.Should().BeNull();
    }
}
