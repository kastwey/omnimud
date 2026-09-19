using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Models;
using Titanium.Web.Proxy.Network;

namespace Omnimud.Core.Tests.Connection;

/// <summary>
/// A REAL proxy server (Titanium.Web.Proxy, MIT) listening on 127.0.0.1 with ephemeral ports: an HTTP proxy
/// with CONNECT and Basic authentication, and a SOCKS5 server with user/password authentication.
/// <para>
/// It is set up so that it cannot touch the machine: TLS is never decrypted (tunnels only), so no root
/// certificate is created, trusted or installed; <c>Start(false)</c> leaves the system proxy settings alone and
/// nothing here ever calls SetAsSystemProxy.
/// </para>
/// <para>
/// One quirk to keep in mind when writing tests: after accepting a tunnel, Titanium waits for the first bytes
/// of the CLIENT (it peeks for a TLS ClientHello) before it connects to the destination. So the client must
/// speak first; what the destination says on its own arrives right after that. (With TLS inside the tunnel the
/// client always speaks first.) Server-first behaviour stays covered by the hand-written fakes.
/// </para>
/// </summary>
internal sealed class RealProxy : IAsyncDisposable
{
    private readonly ProxyServer _server;
    private readonly ExplicitProxyEndPoint _http;
    private readonly SocksProxyEndPoint _socks;

    static RealProxy()
    {
        // Belt and braces: even if some code path wanted to trust a certificate, it may not ask or install.
        CertificateManager.SuppressInteractiveRootStoreMutations = true;
    }

    /// <param name="user">Null = the proxy asks for no credentials.</param>
    public RealProxy(string? user = null, string? password = null)
    {
        _server = new ProxyServer(userTrustRootCertificate: false, machineTrustRootCertificate: false, trustRootCertificateAsAdmin: false);
        _server.CertificateManager.SaveFakeCertificates = false;

        _http = new ExplicitProxyEndPoint(IPAddress.Loopback, 0, decryptSsl: false);
        _http.BeforeTunnelConnectRequest += OnTunnelConnect;
        _server.BeforeRequest += OnRequest;

        _socks = new SocksProxyEndPoint(IPAddress.Loopback, 0, decryptSsl: false);

        if (user is not null)
        {
            _server.ProxyBasicAuthenticateFunc = (_, seenUser, seenPassword) =>
            {
                HttpLogins.Enqueue(seenUser);
                return Task.FromResult(seenUser == user && seenPassword == password);
            };
            _socks.AuthenticateUserFunc = args =>
            {
                SocksLogins.Enqueue(args.UserName);
                return Task.FromResult(args.UserName == user && args.Password == password);
            };
        }

        _server.AddEndPoint(_http);
        _server.AddEndPoint(_socks);
        _server.Start(changeSystemProxySettings: false);
    }

    public int HttpPort => _http.Port;
    public int SocksPort => _socks.Port;

    /// <summary>"host:port" of every CONNECT the HTTP endpoint received (before authentication is checked).</summary>
    public ConcurrentQueue<string> Connects { get; } = new();

    /// <summary>URL of every plain HTTP request that was proxied (after authentication).</summary>
    public ConcurrentQueue<string> Requests { get; } = new();

    /// <summary>User names that tried to authenticate. Passwords are deliberately not kept.</summary>
    public ConcurrentQueue<string> HttpLogins { get; } = new();
    public ConcurrentQueue<string> SocksLogins { get; } = new();

    private Task OnTunnelConnect(object sender, TunnelConnectSessionEventArgs e)
    {
        var uri = e.HttpClient.Request.RequestUri;
        Connects.Enqueue($"{uri.Host}:{uri.Port}");
        return Task.CompletedTask;
    }

    private Task OnRequest(object sender, SessionEventArgs e)
    {
        Requests.Enqueue(e.HttpClient.Request.Url);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _http.BeforeTunnelConnectRequest -= OnTunnelConnect;
        _server.BeforeRequest -= OnRequest;
        try
        {
            _server.Stop();
        }
        catch (Exception)
        {
            // Stopping a proxy with a half-closed tunnel may complain; the test has already decided.
        }
        _server.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>The smallest HTTP/1.1 file server: answers every GET with the same bytes. Loopback, ephemeral port.</summary>
internal sealed class TinyHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    private readonly byte[] _body;
    private readonly string _contentType;
    private readonly Task _loop;

    public TinyHttpServer(byte[] body, string contentType = "audio/wav")
    {
        _body = body;
        _contentType = contentType;
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _loop = Task.Run(AcceptLoopAsync);
    }

    public int Port { get; }

    /// <summary>First line of every request that was understood ("GET /s/a.wav HTTP/1.1").</summary>
    public ConcurrentQueue<string> RequestLines { get; } = new();

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _ = Task.Run(() => ServeAsync(client));
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var head = new List<byte>();
                var one = new byte[1];
                while (head.Count < 4 || !(head[^4] == '\r' && head[^3] == '\n' && head[^2] == '\r' && head[^1] == '\n'))
                {
                    if (head.Count > 16 * 1024 || await stream.ReadAsync(one, _cts.Token) == 0)
                        return;
                    // Not HTTP (the 0x16 of a TLS ClientHello, when https is tried first): hang up at once.
                    if (head.Count == 0 && !char.IsAsciiLetter((char)one[0]))
                        return;
                    head.Add(one[0]);
                }

                var requestLine = Encoding.ASCII.GetString(head.ToArray()).Split("\r\n")[0];
                RequestLines.Enqueue(requestLine);

                var header = $"HTTP/1.1 200 OK\r\nContent-Type: {_contentType}\r\nContent-Length: {_body.Length}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header), _cts.Token);
                await stream.WriteAsync(_body, _cts.Token);
                await stream.FlushAsync(_cts.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener.Stop();
        try { await _loop.WaitAsync(TestTimeouts.Safety); }
        catch (Exception) { /* shutting down */ }
        _cts.Dispose();
    }
}
