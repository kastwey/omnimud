using System.Text;
using FluentAssertions;
using Omnimud.Core.Connection;

namespace Omnimud.Core.Tests.Connection;

public sealed class TelnetConnectionTests
{
    static TelnetConnectionTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static ConnectionConfig Local(int port) => new("127.0.0.1", port);

    // ── basics ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConnectAsync_LocalServer_SendsAndReceives()
    {
        await using var server = new LoopbackServer();
        await using var connection = new TelnetConnection();
        var probe = new ConnectionProbe(connection);

        await connection.ConnectAsync(Local(server.Port)).OrTimeout();
        var peer = await server.AcceptAsync();

        connection.State.Should().Be(ConnectionState.Connected);

        await connection.SendAsync("look").OrTimeout();
        (await peer.ReadBytesAsync(6)).Should().Equal("look\r\n"u8.ToArray());

        await peer.WriteAsync("Welcome\r\n"u8.ToArray());
        (await probe.WaitForBytesAsync(9)).Should().Equal("Welcome\r\n"u8.ToArray());
    }

    [Fact]
    public async Task SendRawAsync_Bytes_AreSentVerbatim()
    {
        await using var server = new LoopbackServer();
        await using var connection = new TelnetConnection();
        await connection.ConnectAsync(Local(server.Port) with { TextEncoding = "iso-8859-1" }).OrTimeout();
        var peer = await server.AcceptAsync();

        byte[] negotiation = [0xFF, 0xFD, 0xC9];
        await connection.SendRawAsync(negotiation).OrTimeout();

        (await peer.ReadBytesAsync(3)).Should().Equal(negotiation);
    }

    [Fact]
    public async Task SendAsync_NotConnected_Throws()
    {
        await using var connection = new TelnetConnection();

        var send = () => connection.SendAsync("look");
        var sendRaw = () => connection.SendRawAsync(new byte[] { 1 });

        await send.Should().ThrowAsync<InvalidOperationException>();
        await sendRaw.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── encoding and terminator ──────────────────────────────────────────────

    [Theory]
    [InlineData("iso-8859-1")]
    [InlineData("windows-1252")]
    [InlineData("ISO-8859-15")]
    public async Task SendAsync_SingleByteEncoding_EncodesAccentsAsSingleBytes(string encodingName)
    {
        await using var server = new LoopbackServer();
        await using var connection = new TelnetConnection();
        await connection.ConnectAsync(Local(server.Port) with { TextEncoding = encodingName }).OrTimeout();
        var peer = await server.AcceptAsync();

        await connection.SendAsync("decir ¡Añó, camión!").OrTimeout();

        // d e c i r ' ' ¡ A ñ ó , ' ' c a m i ó n ! CR LF
        byte[] expected =
        [
            0x64, 0x65, 0x63, 0x69, 0x72, 0x20, 0xA1, 0x41, 0xF1, 0xF3, 0x2C, 0x20,
            0x63, 0x61, 0x6D, 0x69, 0xF3, 0x6E, 0x21, 0x0D, 0x0A
        ];
        (await peer.ReadBytesAsync(expected.Length)).Should().Equal(expected);
    }

    [Fact]
    public async Task SendAsync_Windows1252_EncodesEuroSignAs0x80()
    {
        await using var server = new LoopbackServer();
        await using var connection = new TelnetConnection();
        await connection.ConnectAsync(Local(server.Port) with { TextEncoding = "windows-1252" }).OrTimeout();
        var peer = await server.AcceptAsync();

        await connection.SendAsync("5€").OrTimeout();

        (await peer.ReadBytesAsync(4)).Should().Equal(0x35, 0x80, 0x0D, 0x0A);
    }

    [Fact]
    public async Task SendAsync_DefaultEncoding_IsUtf8WithoutBom()
    {
        await using var server = new LoopbackServer();
        await using var connection = new TelnetConnection();
        await connection.ConnectAsync(Local(server.Port)).OrTimeout();
        var peer = await server.AcceptAsync();

        await connection.SendAsync("ñ").OrTimeout();

        (await peer.ReadBytesAsync(4)).Should().Equal(0xC3, 0xB1, 0x0D, 0x0A);
    }

    [Fact]
    public async Task DataReceived_Latin1Bytes_ArriveUntouched()
    {
        await using var server = new LoopbackServer();
        await using var connection = new TelnetConnection();
        var probe = new ConnectionProbe(connection);
        await connection.ConnectAsync(Local(server.Port) with { TextEncoding = "iso-8859-1" }).OrTimeout();
        var peer = await server.AcceptAsync();

        var sent = Encoding.Latin1.GetBytes("El leñador gritó: ¡árbol va!\r\n");
        await peer.WriteAsync(sent);

        var received = await probe.WaitForBytesAsync(sent.Length);
        received.Should().Equal(sent);
        Encoding.GetEncoding("windows-1252").GetString(received).Should().Be("El leñador gritó: ¡árbol va!\r\n");
    }

    [Theory]
    [InlineData("\n", new byte[] { 0x68, 0x69, 0x0A })]
    [InlineData("\r\n", new byte[] { 0x68, 0x69, 0x0D, 0x0A })]
    [InlineData("", new byte[] { 0x68, 0x69 })]
    public async Task SendAsync_LineTerminator_IsConfigurable(string terminator, byte[] expected)
    {
        await using var server = new LoopbackServer();
        await using var connection = new TelnetConnection();
        await connection.ConnectAsync(Local(server.Port) with { LineTerminator = terminator }).OrTimeout();
        var peer = await server.AcceptAsync();

        await connection.SendAsync("hi").OrTimeout();
        await connection.DisconnectAsync().OrTimeout();

        (await peer.ReadToEndAsync()).Should().Equal(expected);
    }

    [Fact]
    public async Task SendAsync_Latin1YDiaeresis_DoublesIacUnlessDisabled()
    {
        await using var server = new LoopbackServer();

        await using var escaping = new TelnetConnection();
        await escaping.ConnectAsync(Local(server.Port) with { TextEncoding = "iso-8859-1" }).OrTimeout();
        var peer = await server.AcceptAsync();
        await escaping.SendAsync("ÿ").OrTimeout();
        (await peer.ReadBytesAsync(4)).Should().Equal(0xFF, 0xFF, 0x0D, 0x0A);

        await using var verbatim = new TelnetConnection();
        await verbatim.ConnectAsync(Local(server.Port) with { TextEncoding = "iso-8859-1", EscapeTelnetIac = false }).OrTimeout();
        peer = await server.AcceptAsync();
        await verbatim.SendAsync("ÿ").OrTimeout();
        (await peer.ReadBytesAsync(3)).Should().Equal(0xFF, 0x0D, 0x0A);
    }

    // ── validation ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("klingon-8")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ConnectAsync_UnknownEncoding_ThrowsArgumentExceptionBeforeConnecting(string encodingName)
    {
        await using var connection = new TelnetConnection();

        // Port 1 on loopback: if validation did not come first this would be a SocketException instead.
        var act = () => connection.ConnectAsync(new ConnectionConfig("127.0.0.1", 1) { TextEncoding = encodingName });

        (await act.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("encoding");
        connection.State.Should().Be(ConnectionState.Disconnected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public async Task ConnectAsync_PortOutOfRange_Throws(int port)
    {
        await using var connection = new TelnetConnection();

        var act = () => connection.ConnectAsync(new ConnectionConfig("127.0.0.1", port));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        connection.State.Should().Be(ConnectionState.Disconnected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task ConnectAsync_EmptyHost_Throws(string host)
    {
        await using var connection = new TelnetConnection();

        var act = () => connection.ConnectAsync(new ConnectionConfig(host, 4000));

        await act.Should().ThrowAsync<ArgumentException>();
        connection.State.Should().Be(ConnectionState.Disconnected);
    }

    [Fact]
    public async Task ConnectAsync_NullLineTerminator_Throws()
    {
        await using var connection = new TelnetConnection();

        var act = () => connection.ConnectAsync(new ConnectionConfig("127.0.0.1", 4000) { LineTerminator = null! });

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ConnectAsync_AlreadyConnected_Throws()
    {
        await using var server = new LoopbackServer();
        await using var connection = new TelnetConnection();
        await connection.ConnectAsync(Local(server.Port)).OrTimeout();

        var act = () => connection.ConnectAsync(Local(server.Port));

        await act.Should().ThrowAsync<InvalidOperationException>();
        connection.State.Should().Be(ConnectionState.Connected);
    }

    [Fact]
    public async Task ConnectAsync_NothingListening_ThrowsAndStaysReusable()
    {
        int closedPort;
        await using (var temp = new LoopbackServer())
            closedPort = temp.Port;

        await using var server = new LoopbackServer();
        await using var connection = new TelnetConnection();

        var act = () => connection.ConnectAsync(Local(closedPort)).OrTimeout();
        await act.Should().ThrowAsync<System.Net.Sockets.SocketException>();
        connection.State.Should().Be(ConnectionState.Disconnected);

        await connection.ConnectAsync(Local(server.Port)).OrTimeout();
        connection.State.Should().Be(ConnectionState.Connected);
    }

    // ── lifecycle ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ServerCloses_RaisesServerClosedExactlyOnce()
    {
        await using var server = new LoopbackServer();
        await using var connection = new TelnetConnection();
        var probe = new ConnectionProbe(connection);
        await connection.ConnectAsync(Local(server.Port)).OrTimeout();
        var peer = await server.AcceptAsync();

        peer.Close();

        (await probe.WaitForDisconnectAsync()).Should().Be(DisconnectReason.ServerClosed);
        connection.State.Should().Be(ConnectionState.Disconnected);

        // Nothing else may be reported, not even by an explicit disconnect or the disposal.
        await connection.DisconnectAsync().OrTimeout();
        await connection.DisposeAsync();
        probe.Disconnects.Should().Equal(DisconnectReason.ServerClosed);
    }

    [Fact]
    public async Task DisconnectAsync_RaisesUserRequestedOnceAndIsIdempotent()
    {
        await using var server = new LoopbackServer();
        await using var connection = new TelnetConnection();
        var probe = new ConnectionProbe(connection);
        await connection.ConnectAsync(Local(server.Port)).OrTimeout();
        var peer = await server.AcceptAsync();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => connection.DisconnectAsync()))).OrTimeout();
        await connection.DisconnectAsync().OrTimeout();

        connection.State.Should().Be(ConnectionState.Disconnected);
        probe.Disconnects.Should().Equal(DisconnectReason.UserRequested);
        (await peer.ReadToEndAsync()).Should().BeEmpty("the server must see the connection closed");
    }

    [Fact]
    public async Task DisconnectAsync_NeverConnected_DoesNothing()
    {
        await using var connection = new TelnetConnection();
        var probe = new ConnectionProbe(connection);

        await connection.DisconnectAsync().OrTimeout();

        probe.Disconnects.Should().BeEmpty();
    }

    [Fact]
    public async Task DisconnectAsync_FromDataReceivedHandler_DoesNotDeadlock()
    {
        await using var server = new LoopbackServer();
        await using var connection = new TelnetConnection();
        var probe = new ConnectionProbe(connection);
        connection.DataReceived += _ => connection.DisconnectAsync();
        await connection.ConnectAsync(Local(server.Port)).OrTimeout();
        var peer = await server.AcceptAsync();

        await peer.WriteAsync("quit\r\n"u8.ToArray());

        (await probe.WaitForDisconnectAsync()).Should().Be(DisconnectReason.UserRequested);
        probe.Disconnects.Should().HaveCount(1);
    }

    [Fact]
    public async Task ConnectAsync_AfterDisconnections_ReusesTheSameInstance()
    {
        await using var server = new LoopbackServer();
        await using var connection = new TelnetConnection();
        var probe = new ConnectionProbe(connection);

        // 1st connection: closed by us.
        await connection.ConnectAsync(Local(server.Port)).OrTimeout();
        await server.AcceptAsync();
        await connection.DisconnectAsync().OrTimeout();

        // 2nd connection: closed by the server.
        await connection.ConnectAsync(Local(server.Port)).OrTimeout();
        var second = await server.AcceptAsync();
        second.Close();
        await probe.WaitForDisconnectAsync(occurrences: 2);

        // 3rd connection still works in both directions.
        await connection.ConnectAsync(Local(server.Port) with { LineTerminator = "\n" }).OrTimeout();
        var third = await server.AcceptAsync();
        await connection.SendAsync("again").OrTimeout();
        (await third.ReadBytesAsync(6)).Should().Equal("again\n"u8.ToArray());
        await third.WriteAsync("ok"u8.ToArray());
        (await probe.WaitForBytesAsync(2)).Should().Equal("ok"u8.ToArray());

        probe.Disconnects.Should().Equal(DisconnectReason.UserRequested, DisconnectReason.ServerClosed);
    }

    [Fact]
    public async Task ConnectAsync_FromDisconnectedHandler_Reconnects()
    {
        await using var server = new LoopbackServer();
        await using var connection = new TelnetConnection();
        var probe = new ConnectionProbe(connection);
        var reconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Disconnected += async reason =>
        {
            if (reason != DisconnectReason.ServerClosed)
                return;
            try
            {
                await connection.ConnectAsync(Local(server.Port));
                reconnected.TrySetResult();
            }
            catch (Exception ex)
            {
                reconnected.TrySetException(ex);
            }
        };

        await connection.ConnectAsync(Local(server.Port)).OrTimeout();
        (await server.AcceptAsync()).Close();

        await reconnected.Task.OrTimeout("reconnection from the handler");
        var peer = await server.AcceptAsync();
        await peer.WriteAsync("back"u8.ToArray());
        (await probe.WaitForBytesAsync(4)).Should().Equal("back"u8.ToArray());
        connection.State.Should().Be(ConnectionState.Connected);
    }

    [Fact]
    public async Task DataReceived_HandlerThrows_ConnectionSurvives()
    {
        await using var server = new LoopbackServer();
        await using var connection = new TelnetConnection();
        connection.DataReceived += _ => throw new InvalidOperationException("faulty subscriber");
        var probe = new ConnectionProbe(connection);
        await connection.ConnectAsync(Local(server.Port)).OrTimeout();
        var peer = await server.AcceptAsync();

        await peer.WriteAsync("one"u8.ToArray());
        await probe.WaitForBytesAsync(3);
        await peer.WriteAsync("two"u8.ToArray());
        await probe.WaitForBytesAsync(6);

        connection.State.Should().Be(ConnectionState.Connected);
        probe.Disconnects.Should().BeEmpty();
    }

    // ── timeouts ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConnectAsync_HandshakeNeverCompletes_ThrowsTimeoutException()
    {
        // The listener's backlog completes the TCP handshake, but nobody ever answers the TLS hello:
        // a deterministic way of making the connection attempt hang without touching the network.
        await using var server = new LoopbackServer();
        await using var connection = new TelnetConnection();
        var probe = new ConnectionProbe(connection);
        var config = new ConnectionConfig("127.0.0.1", server.Port, UseTls: true, ConnectTimeout: TimeSpan.FromMilliseconds(300));

        var act = () => connection.ConnectAsync(config).OrTimeout();

        await act.Should().ThrowAsync<TimeoutException>();
        connection.State.Should().Be(ConnectionState.Disconnected);
        probe.Disconnects.Should().BeEmpty("a connection that never existed cannot be disconnected");
    }

    [Fact]
    public async Task ConnectAsync_CallerCancels_ThrowsOperationCanceled()
    {
        await using var server = new LoopbackServer();
        await using var connection = new TelnetConnection();
        using var cts = new CancellationTokenSource();
        var config = new ConnectionConfig("127.0.0.1", server.Port, UseTls: true);

        var connecting = connection.ConnectAsync(config, cts.Token);
        await server.AcceptAsync();
        await cts.CancelAsync();

        var act = () => connecting.OrTimeout();
        await act.Should().ThrowAsync<OperationCanceledException>();
        connection.State.Should().Be(ConnectionState.Disconnected);
    }

    [Fact]
    public async Task DisconnectAsync_WhileConnecting_AbortsTheAttempt()
    {
        await using var server = new LoopbackServer();
        await using var connection = new TelnetConnection();
        var probe = new ConnectionProbe(connection);
        var config = new ConnectionConfig("127.0.0.1", server.Port, UseTls: true);

        var connecting = connection.ConnectAsync(config);
        await server.AcceptAsync();
        connection.State.Should().Be(ConnectionState.Connecting);

        await connection.DisconnectAsync().OrTimeout();

        connection.State.Should().Be(ConnectionState.Disconnected);
        var act = () => connecting.OrTimeout();
        await act.Should().ThrowAsync<OperationCanceledException>();
        probe.Disconnects.Should().BeEmpty();
    }

    [Fact]
    public async Task IdleTimeout_SilentServer_RaisesTimeoutOnce()
    {
        await using var server = new LoopbackServer();
        await using var connection = new TelnetConnection();
        var probe = new ConnectionProbe(connection);
        await connection.ConnectAsync(Local(server.Port) with { IdleTimeout = TimeSpan.FromMilliseconds(250) }).OrTimeout();
        var peer = await server.AcceptAsync();

        (await probe.WaitForDisconnectAsync()).Should().Be(DisconnectReason.Timeout);

        connection.State.Should().Be(ConnectionState.Disconnected);
        (await peer.ReadToEndAsync()).Should().BeEmpty();
        probe.Disconnects.Should().Equal(DisconnectReason.Timeout);
    }

    [Fact]
    public async Task IdleTimeout_NotConfigured_NeverFires()
    {
        await using var server = new LoopbackServer();
        await using var connection = new TelnetConnection();
        var probe = new ConnectionProbe(connection);
        await connection.ConnectAsync(Local(server.Port)).OrTimeout();
        var peer = await server.AcceptAsync();

        // A round trip proves the receive loop is alive; with no idle timeout nothing else may happen.
        await peer.WriteAsync("x"u8.ToArray());
        await probe.WaitForBytesAsync(1);

        probe.Disconnects.Should().BeEmpty();
        connection.State.Should().Be(ConnectionState.Connected);
    }

    // ── concurrency ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendAsync_ConcurrentCallers_NeverInterleave(bool useTls)
    {
        const int writers = 8;
        const int perWriter = 60;

        using var certificate = TestCertificates.CreateSelfSigned("localhost");
        await using var server = new LoopbackServer();
        await using var connection = new TelnetConnection();
        var config = new ConnectionConfig("127.0.0.1", server.Port, useTls, new TlsConfig(ValidateCertificate: false));

        var accepting = useTls ? server.AcceptTlsAsync(certificate) : server.AcceptAsync();
        await connection.ConnectAsync(config).OrTimeout();
        var peer = await accepting;
        var reading = peer.ReadToEndAsync();

        // Long lines of a single repeated letter: any interleaving shows up as a mixed line.
        await Task.WhenAll(Enumerable.Range(0, writers).Select(w => Task.Run(async () =>
        {
            var payload = new string((char)('a' + w), 3000 + w);
            for (var i = 0; i < perWriter; i++)
                await connection.SendAsync(payload);
        }))).OrTimeout("concurrent sends");
        await connection.DisconnectAsync().OrTimeout();

        var lines = Encoding.ASCII.GetString(await reading).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(writers * perWriter);
        foreach (var group in lines.GroupBy(l => l[0]))
        {
            var expected = new string(group.Key, 3000 + (group.Key - 'a'));
            group.Should().HaveCount(perWriter).And.OnlyContain(l => l == expected);
        }
    }
}
