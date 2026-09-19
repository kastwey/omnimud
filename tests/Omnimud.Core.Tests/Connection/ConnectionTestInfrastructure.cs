using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Omnimud.Core.Connection;

namespace Omnimud.Core.Tests.Connection;

internal static class TestTimeouts
{
    /// <summary>Safety net only: every wait completes as soon as its condition holds.</summary>
    public static readonly TimeSpan Safety = TimeSpan.FromSeconds(15);

    public static async Task<T> OrTimeout<T>(this Task<T> task, string what = "operation")
    {
        try
        {
            return await task.WaitAsync(Safety);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"Timed out waiting for: {what}");
        }
    }

    public static async Task OrTimeout(this Task task, string what = "operation")
    {
        try
        {
            await task.WaitAsync(Safety);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"Timed out waiting for: {what}");
        }
    }
}

/// <summary>A MUD stand-in: TCP listener on 127.0.0.1 with an ephemeral port, optionally speaking TLS.</summary>
internal sealed class LoopbackServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly List<IDisposable> _owned = [];

    public LoopbackServer()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    public int Port { get; }

    public async Task<ServerPeer> AcceptAsync()
    {
        var client = await _listener.AcceptTcpClientAsync().OrTimeout("server accept");
        client.NoDelay = true;
        lock (_owned) _owned.Add(client);
        return new ServerPeer(client, client.GetStream());
    }

    public async Task<ServerPeer> AcceptTlsAsync(X509Certificate2 certificate, bool requireClientCertificate = false)
    {
        var plain = await AcceptAsync();
        var ssl = new SslStream(plain.Stream, leaveInnerStreamOpen: false);
        X509Certificate2? seenClientCertificate = null;

        await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        {
            ServerCertificate = certificate,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            ClientCertificateRequired = requireClientCertificate,
            RemoteCertificateValidationCallback = (_, cert, _, _) =>
            {
                if (cert is not null)
                    seenClientCertificate = new X509Certificate2(cert);
                return true;
            }
        }).OrTimeout("server TLS handshake");

        return new ServerPeer(plain.Client, ssl)
        {
            SniHostName = ssl.TargetHostName,
            ClientCertificateThumbprint = seenClientCertificate?.Thumbprint
        };
    }

    public ValueTask DisposeAsync()
    {
        _listener.Stop();
        lock (_owned)
        {
            foreach (var item in _owned)
                item.Dispose();
        }
        return ValueTask.CompletedTask;
    }
}

internal sealed class ServerPeer(TcpClient client, Stream stream)
{
    public TcpClient Client { get; } = client;
    public Stream Stream { get; } = stream;
    public string? SniHostName { get; init; }
    public string? ClientCertificateThumbprint { get; init; }

    public async Task<byte[]> ReadBytesAsync(int count)
    {
        var buffer = new byte[count];
        await Stream.ReadExactlyAsync(buffer).AsTask().OrTimeout($"server read of {count} bytes");
        return buffer;
    }

    /// <summary>Reads until the client closes the connection.</summary>
    public async Task<byte[]> ReadToEndAsync()
    {
        using var all = new MemoryStream();
        var buffer = new byte[16384];
        while (true)
        {
            int read;
            try
            {
                read = await Stream.ReadAsync(buffer).AsTask().OrTimeout("server read to end");
            }
            catch (IOException)
            {
                break; // an abortive close is still "the client went away"
            }
            if (read == 0)
                break;
            all.Write(buffer, 0, read);
        }
        return all.ToArray();
    }

    public async Task WriteAsync(byte[] data)
    {
        await Stream.WriteAsync(data).AsTask().OrTimeout("server write");
        await Stream.FlushAsync().OrTimeout("server flush");
    }

    public void Close()
    {
        Stream.Dispose();
        Client.Dispose();
    }
}

/// <summary>Collects what the connection under test reports, with waits that never poll on a fixed delay.</summary>
internal sealed class ConnectionProbe
{
    private readonly object _sync = new();
    private readonly List<byte> _received = [];
    private readonly List<DisconnectReason> _disconnects = [];
    private readonly List<(Func<bool> Condition, TaskCompletionSource Done)> _waiters = [];

    public ConnectionProbe(IConnection connection)
    {
        connection.DataReceived += data =>
        {
            lock (_sync) _received.AddRange(data.ToArray());
            Pulse();
            return Task.CompletedTask;
        };
        connection.Disconnected += reason =>
        {
            lock (_sync) _disconnects.Add(reason);
            Pulse();
            return Task.CompletedTask;
        };
    }

    public byte[] Received { get { lock (_sync) return _received.ToArray(); } }
    public DisconnectReason[] Disconnects { get { lock (_sync) return _disconnects.ToArray(); } }

    public async Task<byte[]> WaitForBytesAsync(int count)
    {
        await WaitAsync(() => _received.Count >= count, $"{count} received bytes");
        return Received;
    }

    public async Task<DisconnectReason> WaitForDisconnectAsync(int occurrences = 1)
    {
        await WaitAsync(() => _disconnects.Count >= occurrences, "Disconnected event");
        return Disconnects[occurrences - 1];
    }

    private Task WaitAsync(Func<bool> condition, string what)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            if (condition())
                return Task.CompletedTask;
            _waiters.Add((condition, done));
        }
        return done.Task.OrTimeout(what);
    }

    private void Pulse()
    {
        List<TaskCompletionSource> ready = [];
        lock (_sync)
        {
            for (var i = _waiters.Count - 1; i >= 0; i--)
            {
                if (!_waiters[i].Condition())
                    continue;
                ready.Add(_waiters[i].Done);
                _waiters.RemoveAt(i);
            }
        }
        foreach (var done in ready)
            done.TrySetResult();
    }
}

internal static class TestCertificates
{
    /// <summary>
    /// Self-signed certificate round-tripped through PFX: on Windows SChannel cannot use the ephemeral key
    /// that CreateSelfSigned returns.
    /// </summary>
    public static X509Certificate2 CreateSelfSigned(string subjectName, params string[] dnsNames)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={subjectName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var san = new SubjectAlternativeNameBuilder();
        foreach (var name in dnsNames.DefaultIfEmpty(subjectName))
            san.AddDnsName(name);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: false));

        using var ephemeral = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        return X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pfx), password: null, X509KeyStorageFlags.Exportable);
    }

    public static string ExportToTempPfx(X509Certificate2 certificate, string? password)
    {
        var path = Path.Combine(Path.GetTempPath(), $"omnimud-test-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
        return path;
    }
}

/// <summary>Base for the fake proxies: accepts one client, runs the handshake, then pumps bytes to the target.</summary>
internal abstract class FakeProxy : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    private readonly int _targetPort;

    protected FakeProxy(int targetPort)
    {
        _targetPort = targetPort;
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        Completion = Task.Run(RunAsync);
    }

    public int Port { get; }

    /// <summary>Faults if the fake saw something that violates the protocol, so tests can surface it.</summary>
    public Task Completion { get; }

    /// <summary>When true the proxy accepts the TCP connection and then says nothing at all.</summary>
    public bool Silent { get; init; }

    /// <summary>Returns true if the tunnel must be opened.</summary>
    protected abstract Task<bool> HandshakeAsync(NetworkStream client, CancellationToken ct);

    private async Task RunAsync()
    {
        try
        {
            using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
            client.NoDelay = true;
            var clientStream = client.GetStream();

            if (Silent)
            {
                await Task.Delay(Timeout.Infinite, _cts.Token);
                return;
            }

            if (!await HandshakeAsync(clientStream, _cts.Token))
                return;

            using var target = new TcpClient { NoDelay = true };
            await target.ConnectAsync(IPAddress.Loopback, _targetPort, _cts.Token);
            var targetStream = target.GetStream();

            await Task.WhenAny(
                PumpAsync(clientStream, targetStream, _cts.Token),
                PumpAsync(targetStream, clientStream, _cts.Token));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task PumpAsync(Stream from, Stream to, CancellationToken ct)
    {
        try
        {
            await from.CopyToAsync(to, ct);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener.Stop();
        try { await Completion.WaitAsync(TestTimeouts.Safety); }
        catch { /* surfaced by the tests that care through Completion */ }
    }
}

internal sealed class FakeSocks5Proxy(int targetPort) : FakeProxy(targetPort)
{
    public string? RequiredUser { get; init; }
    public string? RequiredPassword { get; init; }

    /// <summary>REP code for the CONNECT request; 0 = success.</summary>
    public byte ReplyCode { get; init; }

    public byte[]? OfferedMethods { get; private set; }
    public string? SeenUser { get; private set; }
    public string? SeenPassword { get; private set; }
    public byte RequestedAddressType { get; private set; }
    public string? RequestedHost { get; private set; }
    public int RequestedPort { get; private set; }

    protected override async Task<bool> HandshakeAsync(NetworkStream client, CancellationToken ct)
    {
        var head = await ReadAsync(client, 2, ct);
        Require(head[0] == 0x05, "greeting version");
        OfferedMethods = await ReadAsync(client, head[1], ct);

        if (RequiredUser is not null)
        {
            if (!OfferedMethods.Contains((byte)0x02))
            {
                await client.WriteAsync(new byte[] { 0x05, 0xFF }, ct);
                return false;
            }

            await client.WriteAsync(new byte[] { 0x05, 0x02 }, ct);
            var auth = await ReadAsync(client, 2, ct);
            Require(auth[0] == 0x01, "auth sub-negotiation version");
            SeenUser = Encoding.UTF8.GetString(await ReadAsync(client, auth[1], ct));
            var passwordLength = (await ReadAsync(client, 1, ct))[0];
            SeenPassword = Encoding.UTF8.GetString(await ReadAsync(client, passwordLength, ct));

            var ok = SeenUser == RequiredUser && SeenPassword == RequiredPassword;
            await client.WriteAsync(new byte[] { 0x01, ok ? (byte)0x00 : (byte)0x01 }, ct);
            if (!ok)
                return false;
        }
        else
        {
            Require(OfferedMethods.Contains((byte)0x00), "no-auth method offered");
            await client.WriteAsync(new byte[] { 0x05, 0x00 }, ct);
        }

        var request = await ReadAsync(client, 4, ct);
        Require(request[0] == 0x05 && request[1] == 0x01 && request[2] == 0x00, "CONNECT request header");
        RequestedAddressType = request[3];
        switch (request[3])
        {
            case 0x01:
                RequestedHost = new IPAddress(await ReadAsync(client, 4, ct)).ToString();
                break;
            case 0x04:
                RequestedHost = new IPAddress(await ReadAsync(client, 16, ct)).ToString();
                break;
            case 0x03:
                var length = (await ReadAsync(client, 1, ct))[0];
                RequestedHost = Encoding.ASCII.GetString(await ReadAsync(client, length, ct));
                break;
            default:
                throw new InvalidOperationException($"Fake SOCKS5: bad ATYP {request[3]}");
        }
        var port = await ReadAsync(client, 2, ct);
        RequestedPort = (port[0] << 8) | port[1];

        // Reply with a domain-typed bound address on success so the client has to parse the variable form.
        byte[] reply = ReplyCode == 0
            ? [0x05, 0x00, 0x00, 0x03, 0x04, (byte)'b', (byte)'n', (byte)'d', (byte)'!', 0x12, 0x34]
            : [0x05, ReplyCode, 0x00, 0x01, 0, 0, 0, 0, 0, 0];
        await client.WriteAsync(reply, ct);
        return ReplyCode == 0;
    }

    private static async Task<byte[]> ReadAsync(Stream stream, int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        await stream.ReadExactlyAsync(buffer, ct);
        return buffer;
    }

    private static void Require(bool condition, string what)
    {
        if (!condition)
            throw new InvalidOperationException($"Fake SOCKS5: unexpected {what}");
    }
}

internal sealed class FakeHttpProxy(int targetPort) : FakeProxy(targetPort)
{
    /// <summary>When set, requests without exactly these Basic credentials get 407.</summary>
    public string? RequiredCredentials { get; init; }

    /// <summary>When set, every request is answered with this status line (e.g. "403 Forbidden").</summary>
    public string? ForcedStatus { get; init; }

    public string? RequestLine { get; private set; }
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

    protected override async Task<bool> HandshakeAsync(NetworkStream client, CancellationToken ct)
    {
        var head = new List<byte>();
        var one = new byte[1];
        while (!EndsWithBlankLine(head))
        {
            if (await client.ReadAsync(one, ct) == 0)
                throw new InvalidOperationException("Fake HTTP proxy: client closed before finishing the request");
            head.Add(one[0]);
        }

        var lines = Encoding.ASCII.GetString(head.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        RequestLine = lines[0];
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            Headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        string status;
        if (ForcedStatus is not null)
        {
            status = ForcedStatus;
        }
        else if (RequiredCredentials is not null &&
                 (!Headers.TryGetValue("Proxy-Authorization", out var authorization) ||
                  authorization != "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(RequiredCredentials))))
        {
            status = "407 Proxy Authentication Required";
        }
        else
        {
            status = "200 Connection established";
        }

        var ok = status.StartsWith('2');
        var response = ok
            ? $"HTTP/1.1 {status}\r\nProxy-Agent: fake\r\n\r\n"
            : $"HTTP/1.1 {status}\r\nProxy-Authenticate: Basic realm=\"fake\"\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
        await client.WriteAsync(Encoding.ASCII.GetBytes(response), ct);
        return ok;
    }

    private static bool EndsWithBlankLine(List<byte> data)
    {
        var n = data.Count;
        return n >= 4 && data[n - 4] == '\r' && data[n - 3] == '\n' && data[n - 2] == '\r' && data[n - 1] == '\n';
    }
}
