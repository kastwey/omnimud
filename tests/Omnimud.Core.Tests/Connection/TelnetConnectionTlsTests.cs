using System.Net.Security;
using FluentAssertions;
using Omnimud.Core.Connection;

namespace Omnimud.Core.Tests.Connection;

public sealed class TelnetConnectionTlsTests
{
    [Fact]
    public async Task ConnectAsync_SelfSignedWithValidation_ThrowsReadableTlsHandshakeException()
    {
        using var certificate = TestCertificates.CreateSelfSigned("localhost");
        await using var server = new LoopbackServer();
        await using var connection = new TelnetConnection();
        var probe = new ConnectionProbe(connection);
        var config = new ConnectionConfig("127.0.0.1", server.Port, UseTls: true, new TlsConfig(ValidateCertificate: true) { TargetHost = "localhost" });

        var serverSide = server.AcceptTlsAsync(certificate);
        var act = () => connection.ConnectAsync(config).OrTimeout();

        var error = (await act.Should().ThrowAsync<TlsHandshakeException>()).Which;
        error.IsCertificateError.Should().BeTrue();
        error.PolicyErrors.Should().HaveFlag(SslPolicyErrors.RemoteCertificateChainErrors);
        error.PolicyErrors.Should().NotHaveFlag(SslPolicyErrors.RemoteCertificateNameMismatch);
        error.ChainStatus.Should().NotBeEmpty();
        error.CertificateSubject.Should().Be("CN=localhost");
        error.Message.Should().Contain("Invalid server certificate").And.Contain("localhost");

        connection.State.Should().Be(ConnectionState.Disconnected);
        probe.Disconnects.Should().BeEmpty();
        await serverSide.ContinueWith(_ => { }); // the server handshake fails or not depending on TLS version
    }

    [Fact]
    public async Task ConnectAsync_DefaultTlsOptions_ValidateTheCertificate()
    {
        using var certificate = TestCertificates.CreateSelfSigned("localhost");
        await using var server = new LoopbackServer();
        await using var connection = new TelnetConnection();
        var config = new ConnectionConfig("127.0.0.1", server.Port, UseTls: true);

        var serverSide = server.AcceptTlsAsync(certificate);
        var act = () => connection.ConnectAsync(config).OrTimeout();

        await act.Should().ThrowAsync<TlsHandshakeException>();
        await serverSide.ContinueWith(_ => { });
    }

    [Fact]
    public async Task ConnectAsync_NameMismatch_IsReported()
    {
        using var certificate = TestCertificates.CreateSelfSigned("other.example.test");
        await using var server = new LoopbackServer();
        await using var connection = new TelnetConnection();
        var config = new ConnectionConfig("127.0.0.1", server.Port, UseTls: true, new TlsConfig { TargetHost = "localhost" });

        var serverSide = server.AcceptTlsAsync(certificate);
        var act = () => connection.ConnectAsync(config).OrTimeout();

        var error = (await act.Should().ThrowAsync<TlsHandshakeException>()).Which;
        error.PolicyErrors.Should().HaveFlag(SslPolicyErrors.RemoteCertificateNameMismatch);
        error.Message.Should().Contain("not issued for 'localhost'");
        await serverSide.ContinueWith(_ => { });
    }

    [Fact]
    public async Task ConnectAsync_ValidationDisabled_ConnectsAndExchangesData()
    {
        using var certificate = TestCertificates.CreateSelfSigned("localhost");
        await using var server = new LoopbackServer();
        await using var connection = new TelnetConnection();
        var probe = new ConnectionProbe(connection);
        var config = new ConnectionConfig("127.0.0.1", server.Port, UseTls: true, new TlsConfig(ValidateCertificate: false) { TargetHost = "localhost" })
        {
            TextEncoding = "iso-8859-1"
        };
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        var serverSide = server.AcceptTlsAsync(certificate);
        await connection.ConnectAsync(config).OrTimeout();
        var peer = await serverSide;

        peer.SniHostName.Should().Be("localhost");

        await connection.SendAsync("señor").OrTimeout();
        (await peer.ReadBytesAsync(7)).Should().Equal(0x73, 0x65, 0xF1, 0x6F, 0x72, 0x0D, 0x0A);

        await peer.WriteAsync("hola"u8.ToArray());
        (await probe.WaitForBytesAsync(4)).Should().Equal("hola"u8.ToArray());

        peer.Close();
        (await probe.WaitForDisconnectAsync()).Should().Be(DisconnectReason.ServerClosed);
        probe.Disconnects.Should().HaveCount(1);
    }

    [Fact]
    public async Task ConnectAsync_TargetHostOverride_IsSentAsSni()
    {
        using var certificate = TestCertificates.CreateSelfSigned("mud.example.test");
        await using var server = new LoopbackServer();
        await using var connection = new TelnetConnection();
        var config = new ConnectionConfig("127.0.0.1", server.Port, UseTls: true,
            new TlsConfig(ValidateCertificate: false) { TargetHost = "mud.example.test" });

        var serverSide = server.AcceptTlsAsync(certificate);
        await connection.ConnectAsync(config).OrTimeout();

        (await serverSide).SniHostName.Should().Be("mud.example.test");
    }

    [Fact]
    public async Task ConnectAsync_ServerIsNotTls_ThrowsTlsHandshakeExceptionWithoutPolicyErrors()
    {
        await using var server = new LoopbackServer();
        await using var connection = new TelnetConnection();
        var config = new ConnectionConfig("127.0.0.1", server.Port, UseTls: true);

        var connecting = connection.ConnectAsync(config);
        var peer = await server.AcceptAsync();
        await peer.WriteAsync("Welcome to a plain text MUD, no TLS here!\r\n"u8.ToArray());

        var act = () => connecting.OrTimeout();
        var error = (await act.Should().ThrowAsync<TlsHandshakeException>()).Which;
        error.IsCertificateError.Should().BeFalse();
        connection.State.Should().Be(ConnectionState.Disconnected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("s3cret")]
    public async Task ConnectAsync_ClientCertificate_IsPresentedToTheServer(string? password)
    {
        using var serverCertificate = TestCertificates.CreateSelfSigned("localhost");
        using var clientCertificate = TestCertificates.CreateSelfSigned("omnimud-player");
        var pfxPath = TestCertificates.ExportToTempPfx(clientCertificate, password);
        try
        {
            await using var server = new LoopbackServer();
            await using var connection = new TelnetConnection();
            var config = new ConnectionConfig("127.0.0.1", server.Port, UseTls: true,
                new TlsConfig(ValidateCertificate: false, ClientCertificatePath: pfxPath) { ClientCertificatePassword = password });

            var serverSide = server.AcceptTlsAsync(serverCertificate, requireClientCertificate: true);
            await connection.ConnectAsync(config).OrTimeout();
            var peer = await serverSide;

            peer.ClientCertificateThumbprint.Should().Be(clientCertificate.Thumbprint);

            await connection.SendAsync("hi").OrTimeout();
            (await peer.ReadBytesAsync(4)).Should().Equal("hi\r\n"u8.ToArray());
        }
        finally
        {
            File.Delete(pfxPath);
        }
    }

    [Fact]
    public async Task ConnectAsync_ClientCertificateMissingOrWrongPassword_ThrowsTlsHandshakeException()
    {
        using var clientCertificate = TestCertificates.CreateSelfSigned("omnimud-player");
        var pfxPath = TestCertificates.ExportToTempPfx(clientCertificate, "right");
        try
        {
            await using var server = new LoopbackServer();
            await using var connection = new TelnetConnection();

            var missing = () => connection.ConnectAsync(new ConnectionConfig("127.0.0.1", server.Port, UseTls: true,
                new TlsConfig(ClientCertificatePath: pfxPath + ".nope"))).OrTimeout();
            var wrongPassword = () => connection.ConnectAsync(new ConnectionConfig("127.0.0.1", server.Port, UseTls: true,
                new TlsConfig(ClientCertificatePath: pfxPath) { ClientCertificatePassword = "wrong" })).OrTimeout();

            (await missing.Should().ThrowAsync<TlsHandshakeException>()).Which.Message.Should().Contain("client certificate");
            (await wrongPassword.Should().ThrowAsync<TlsHandshakeException>()).Which.Message.Should().Contain("client certificate");
            connection.State.Should().Be(ConnectionState.Disconnected);
        }
        finally
        {
            File.Delete(pfxPath);
        }
    }
}
