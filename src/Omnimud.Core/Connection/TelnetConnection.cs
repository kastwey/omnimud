using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Omnimud.Core.Resources;

namespace Omnimud.Core.Connection;

/// <summary>
/// TCP connection to a MUD, optionally through a SOCKS5 / HTTP CONNECT proxy and optionally over TLS.
/// <para>
/// Guarantees: <see cref="Disconnected"/> fires exactly once per established connection; the instance can be
/// reconnected after a disconnection (also from inside the <see cref="Disconnected"/> handler);
/// <see cref="DisconnectAsync"/> is idempotent and safe to call from the event handlers; concurrent sends
/// are serialized and never interleave.
/// </para>
/// <para>
/// <see cref="DisconnectReason.Timeout"/> is reported when the optional <see cref="ConnectionConfig.IdleTimeout"/>
/// elapses without data, or when the OS gives the peer up for dead (keep-alive probes or retransmissions
/// unanswered). A connect timeout is not a disconnection: <see cref="ConnectAsync"/> throws
/// <see cref="TimeoutException"/> and no event is raised, because the connection never existed.
/// </para>
/// </summary>
public sealed class TelnetConnection : IConnection
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    // Lets DisconnectAsync know it is being called from a DataReceived/Disconnected handler, where
    // waiting for the receive loop would be waiting for itself.
    private static readonly AsyncLocal<Link?> CurrentReceiveLoop = new();

    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private volatile ConnectionState _state = ConnectionState.Disconnected;
    private volatile Link? _link;
    private CancellationTokenSource? _connectCts;
    private TaskCompletionSource? _connectFinished;
    private Task _disconnecting = Task.CompletedTask;

    public TelnetConnection(TimeProvider? timeProvider = null) => _time = timeProvider ?? TimeProvider.System;

    public ConnectionState State => _state;

    public event Func<ReadOnlyMemory<byte>, Task>? DataReceived;
    public event Func<DisconnectReason, Task>? Disconnected;

    public async Task ConnectAsync(ConnectionConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (_state != ConnectionState.Disconnected)
            throw new InvalidOperationException(string.Format(Strings.Error_CannotConnectInState, _state));

        var settings = Validate(config);

        CancellationTokenSource connectCts;
        TaskCompletionSource finished;
        lock (_gate)
        {
            if (_state != ConnectionState.Disconnected)
                throw new InvalidOperationException(string.Format(Strings.Error_CannotConnectInState, _state));

            _state = ConnectionState.Connecting;
            connectCts = _connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            finished = _connectFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        // One deadline for everything: TCP, proxy handshake and TLS handshake.
        var timeoutCts = new CancellationTokenSource(config.ConnectTimeout ?? DefaultTimeout, _time);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(connectCts.Token, timeoutCts.Token);

        TcpClient? tcp = null;
        Stream? stream = null;
        X509Certificate2? clientCertificate = null;

        try
        {
            if (config.UseTls)
                clientCertificate = LoadClientCertificate(config.TlsOptions);

            tcp = new TcpClient { NoDelay = true };
            if (config.TcpKeepAlive)
                EnableKeepAlive(tcp.Client);

            if (config.Proxy is { } proxy)
            {
                try
                {
                    await tcp.ConnectAsync(proxy.Host, proxy.Port, linked.Token).ConfigureAwait(false);
                }
                catch (SocketException ex)
                {
                    throw new ProxyException(proxy.Protocol, ProxyErrorKind.ConnectionFailed,
                        $"Cannot connect to the proxy {proxy.Host}:{proxy.Port}: {ex.Message}", innerException: ex);
                }

                stream = tcp.GetStream();
                try
                {
                    await ProxyTunnel.EstablishAsync(stream, proxy, config.Host, config.Port, linked.Token).ConfigureAwait(false);
                }
                catch (IOException ex) when (!linked.IsCancellationRequested)
                {
                    // A proxy that resets the connection instead of answering (wrong protocol, or its way of saying no).
                    throw new ProxyException(proxy.Protocol, ProxyErrorKind.ProtocolError,
                        $"The proxy {proxy.Host}:{proxy.Port} dropped the connection during the handshake: {ex.Message}", innerException: ex);
                }
            }
            else
            {
                await tcp.ConnectAsync(config.Host, config.Port, linked.Token).ConfigureAwait(false);
                stream = tcp.GetStream();
            }

            if (config.UseTls)
                stream = await AuthenticateTlsAsync(stream, config, clientCertificate, linked.Token).ConfigureAwait(false);

            var link = new Link(tcp, stream, clientCertificate, settings);
            lock (_gate)
            {
                // DisconnectAsync may have arrived while the last handshake step was completing.
                connectCts.Token.ThrowIfCancellationRequested();
                _link = link;
                _state = ConnectionState.Connected;
            }

            // Task.Run so that no DataReceived handler runs inside this call.
            link.ReceiveTask = Task.Run(() => ReceiveLoopAsync(link, config.IdleTimeout));
        }
        catch (Exception ex)
        {
            if (stream is not null)
                await stream.DisposeAsync().ConfigureAwait(false);
            tcp?.Dispose();
            clientCertificate?.Dispose();

            lock (_gate)
                _state = ConnectionState.Disconnected;

            if (timeoutCts.IsCancellationRequested && !connectCts.IsCancellationRequested)
                throw new TimeoutException(string.Format(Strings.Error_ConnectionTimedOut, config.Host, config.Port), ex);

            // Cancellation surfaces in many shapes (IOException, ObjectDisposedException...) depending on the step.
            if (connectCts.IsCancellationRequested && ex is not OperationCanceledException)
                throw new OperationCanceledException("The connection attempt was cancelled.", ex, connectCts.Token);

            throw;
        }
        finally
        {
            lock (_gate)
            {
                // By now the state is no longer Connecting, so another attempt may already own these fields.
                if (_connectCts == connectCts)
                {
                    _connectCts = null;
                    _connectFinished = null;
                }
            }
            linked.Dispose();
            timeoutCts.Dispose();
            connectCts.Dispose();
            finished.TrySetResult();
        }
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        Link? link = null;
        CancellationTokenSource? connecting = null;
        TaskCompletionSource? completion = null;
        Task pending;

        lock (_gate)
        {
            switch (_state)
            {
                case ConnectionState.Disconnected:
                    return Task.CompletedTask;

                case ConnectionState.Disconnecting:
                    // Same disconnection: just wait for it, unless we are inside one of its event handlers.
                    return CurrentReceiveLoop.Value is not null ? Task.CompletedTask : _disconnecting;

                case ConnectionState.Connecting:
                    connecting = _connectCts;
                    pending = _connectFinished?.Task ?? Task.CompletedTask;
                    break;

                default:
                    link = _link;
                    _link = null;
                    _state = ConnectionState.Disconnecting;
                    completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    pending = _disconnecting = completion.Task;
                    break;
            }
        }

        // Everything below runs outside the lock: cancellation callbacks and event handlers execute inline.
        if (completion is not null)
        {
            _ = RunAsync(completion);
        }
        else
        {
            // Aborts the attempt: ConnectAsync throws OperationCanceledException to its caller. No Disconnected
            // event, because the connection never got established.
            try { connecting?.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        return pending;

        async Task RunAsync(TaskCompletionSource completion)
        {
            try
            {
                if (link is not null)
                {
                    await link.CloseAsync().ConfigureAwait(false);

                    if (link.ReceiveTask is { } receive && CurrentReceiveLoop.Value != link)
                        await receive.ConfigureAwait(false);
                }

                lock (_gate)
                    _state = ConnectionState.Disconnected;

                await RaiseDisconnectedAsync(DisconnectReason.UserRequested).ConfigureAwait(false);
            }
            finally
            {
                lock (_gate)
                {
                    if (_state == ConnectionState.Disconnecting)
                        _state = ConnectionState.Disconnected;
                }
                completion.TrySetResult();
            }
        }
    }

    public Task SendAsync(string command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var link = _link;
        if (_state != ConnectionState.Connected || link is null)
            throw new InvalidOperationException(Strings.Error_NotConnected);

        var settings = link.Settings;
        var data = settings.Encoding.GetBytes(command + settings.LineTerminator);
        if (settings.EscapeIac && data.AsSpan().Contains((byte)0xFF))
            data = DoubleIac(data);

        return WriteAsync(link, data, ct);
    }

    public Task SendRawAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        var link = _link;
        if (_state != ConnectionState.Connected || link is null)
            throw new InvalidOperationException(Strings.Error_NotConnected);

        return WriteAsync(link, data, ct);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await DisconnectAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    // ── sending ──────────────────────────────────────────────────────────────

    private async Task WriteAsync(Link link, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        // SslStream forbids concurrent writes outright, and on a plain socket large writes could interleave.
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_link != link)
                throw new InvalidOperationException(Strings.Error_NotConnected);

            await link.Stream.WriteAsync(data, ct).ConfigureAwait(false);
            await link.Stream.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            throw new InvalidOperationException(Strings.Error_NotConnected);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static byte[] DoubleIac(byte[] data)
    {
        var result = new List<byte>(data.Length + 4);
        foreach (var b in data)
        {
            result.Add(b);
            if (b == 0xFF)
                result.Add(b);
        }
        return result.ToArray();
    }

    // ── receiving ────────────────────────────────────────────────────────────

    private async Task ReceiveLoopAsync(Link link, TimeSpan? idleTimeout)
    {
        CurrentReceiveLoop.Value = link;

        var buffer = new byte[8192];
        DisconnectReason reason;
        ITimer? idleTimer = null;
        var idleFired = 0;
        var lastActivity = _time.GetTimestamp();

        try
        {
            if (idleTimeout is { } idle)
            {
                idleTimer = _time.CreateTimer(_ =>
                {
                    // The timer may fire just as data arrives; only real silence counts.
                    var remaining = idle - _time.GetElapsedTime(Volatile.Read(ref lastActivity));
                    if (remaining > TimeSpan.Zero)
                    {
                        try { idleTimer?.Change(remaining, Timeout.InfiniteTimeSpan); }
                        catch (ObjectDisposedException) { }
                        return;
                    }

                    Volatile.Write(ref idleFired, 1);
                    link.Cancel();
                }, null, idle, Timeout.InfiniteTimeSpan);
            }

            while (true)
            {
                var bytesRead = await link.Stream.ReadAsync(buffer, link.Token).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    reason = DisconnectReason.ServerClosed;
                    break;
                }

                Volatile.Write(ref lastActivity, _time.GetTimestamp());
                idleTimer?.Change(idleTimeout!.Value, Timeout.InfiniteTimeSpan);

                await RaiseDataReceivedAsync(buffer.AsMemory(0, bytesRead)).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            if (Volatile.Read(ref idleFired) == 1)
                reason = DisconnectReason.Timeout;
            else if (ex is OperationCanceledException or ObjectDisposedException)
                reason = DisconnectReason.Error; // only used if nobody else owns the disconnection, see below
            else
                reason = IsPeerTimeout(ex) ? DisconnectReason.Timeout : DisconnectReason.Error;
        }
        finally
        {
            if (idleTimer is not null)
                await idleTimer.DisposeAsync().ConfigureAwait(false);
        }

        // Whoever takes the link out of _link owns the disconnection and raises the event. If
        // DisconnectAsync got there first, this loop has nothing left to do.
        lock (_gate)
        {
            if (_link != link)
                return;
            _link = null;
            _state = ConnectionState.Disconnected;
        }

        await link.CloseAsync().ConfigureAwait(false);
        await RaiseDisconnectedAsync(reason).ConfigureAwait(false);
    }

    private static bool IsPeerTimeout(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            // TimedOut: retransmissions unanswered. NetworkReset: Windows' error for failed keep-alive probes.
            if (e is SocketException { SocketErrorCode: SocketError.TimedOut or SocketError.NetworkReset })
                return true;
        }
        return false;
    }

    private async Task RaiseDataReceivedAsync(ReadOnlyMemory<byte> data)
    {
        var handlers = DataReceived;
        if (handlers is null)
            return;

        foreach (var handler in handlers.GetInvocationList().Cast<Func<ReadOnlyMemory<byte>, Task>>())
        {
            try
            {
                await handler(data).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // A faulty subscriber (trigger, script...) must not take the connection down.
                Trace.TraceError("TelnetConnection: DataReceived handler failed: {0}", ex);
            }
        }
    }

    private async Task RaiseDisconnectedAsync(DisconnectReason reason)
    {
        var handlers = Disconnected;
        if (handlers is null)
            return;

        foreach (var handler in handlers.GetInvocationList().Cast<Func<DisconnectReason, Task>>())
        {
            try
            {
                await handler(reason).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Trace.TraceError("TelnetConnection: Disconnected handler failed: {0}", ex);
            }
        }
    }

    // ── connecting ───────────────────────────────────────────────────────────

    private static LinkSettings Validate(ConnectionConfig config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Host);
        if (config.Port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(config), Strings.Error_PortOutOfRange);

        if (config.ConnectTimeout is { } timeout && timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(config), "ConnectTimeout must be positive.");
        if (config.IdleTimeout is { } idle && idle <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(config), "IdleTimeout must be positive.");

        if (config.LineTerminator is null)
            throw new ArgumentException("LineTerminator cannot be null (use an empty string for none).", nameof(config));

        if (string.IsNullOrWhiteSpace(config.TextEncoding))
            throw new ArgumentException("The text encoding name is empty.", nameof(config));

        Encoding encoding;
        try
        {
            encoding = Encoding.GetEncoding(config.TextEncoding.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            throw new ArgumentException($"Unknown text encoding '{config.TextEncoding}'.", nameof(config), ex);
        }

        if (config.Proxy is not null)
            ProxyTunnel.Validate(config.Proxy, config.Host);

        return new LinkSettings(encoding, config.LineTerminator, config.EscapeTelnetIac);
    }

    private static void EnableKeepAlive(Socket socket)
    {
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        try
        {
            // Defaults are two hours idle; a game wants to know within a couple of minutes.
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 60);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 10);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 5);
        }
        catch (SocketException)
        {
            // Fine tuning is not available everywhere; plain keep-alive stays on.
        }
    }

    private static X509Certificate2? LoadClientCertificate(TlsConfig? tls)
    {
        if (string.IsNullOrWhiteSpace(tls?.ClientCertificatePath))
            return null;

        try
        {
            return X509CertificateLoader.LoadPkcs12FromFile(tls.ClientCertificatePath, tls.ClientCertificatePassword);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            throw new TlsHandshakeException(
                $"Cannot load the client certificate '{tls.ClientCertificatePath}': {ex.Message}", innerException: ex);
        }
    }

    private static async Task<Stream> AuthenticateTlsAsync(
        Stream inner, ConnectionConfig config, X509Certificate2? clientCertificate, CancellationToken ct)
    {
        var tls = config.TlsOptions ?? new TlsConfig();
        var targetHost = string.IsNullOrWhiteSpace(tls.TargetHost) ? config.Host : tls.TargetHost;

        var policyErrors = SslPolicyErrors.None;
        string[] chainStatus = [];
        string? subject = null;

        var options = new SslClientAuthenticationOptions
        {
            // Also the SNI name (.NET omits SNI for IP literals, as the RFC demands).
            TargetHost = targetHost,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = tls.CheckCertificateRevocation
                ? X509RevocationMode.Online
                : X509RevocationMode.NoCheck,
            RemoteCertificateValidationCallback = (_, certificate, chain, errors) =>
            {
                if (!tls.ValidateCertificate)
                    return true;

                // 'errors' is the verdict of the default validation; we only record why it said no.
                policyErrors = errors;
                subject = certificate?.Subject;
                chainStatus = chain?.ChainStatus
                    .Where(s => s.Status != X509ChainStatusFlags.NoError)
                    .Select(s => $"{s.Status}: {s.StatusInformation.Trim()}")
                    .ToArray() ?? [];
                return errors == SslPolicyErrors.None;
            }
        };

        if (clientCertificate is not null)
        {
            options.ClientCertificates = [clientCertificate];
            // Without this the platform only offers the certificate if it matches the server's CA hints.
            options.LocalCertificateSelectionCallback = (_, _, _, _, _) => clientCertificate;
        }

        var ssl = new SslStream(inner, leaveInnerStreamOpen: false);
        try
        {
            await ssl.AuthenticateAsClientAsync(options, ct).ConfigureAwait(false);
            return ssl;
        }
        catch (Exception ex)
        {
            await ssl.DisposeAsync().ConfigureAwait(false);

            if (ct.IsCancellationRequested)
                throw;

            if (policyErrors != SslPolicyErrors.None)
                throw new TlsHandshakeException(
                    DescribeCertificateFailure(targetHost, policyErrors, chainStatus),
                    policyErrors, chainStatus, subject, ex);

            if (ex is AuthenticationException or IOException)
                throw new TlsHandshakeException(
                    $"TLS handshake with {targetHost} failed: {ex.Message}", innerException: ex);

            throw;
        }
    }

    private static string DescribeCertificateFailure(string host, SslPolicyErrors errors, string[] chainStatus)
    {
        var reasons = new List<string>();
        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
            reasons.Add("the server sent no certificate");
        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
            reasons.Add($"the certificate was not issued for '{host}'");
        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors))
            reasons.Add(chainStatus.Length > 0
                ? "the certificate chain is not trusted (" + string.Join("; ", chainStatus) + ")"
                : "the certificate chain is not trusted");

        return $"Invalid server certificate for {host}: {string.Join(", ", reasons)}.";
    }

    // ── per-connection state ─────────────────────────────────────────────────

    private sealed record LinkSettings(Encoding Encoding, string LineTerminator, bool EscapeIac);

    /// <summary>
    /// Everything that belongs to one established connection. A new one is created per ConnectAsync, so a
    /// late callback from a previous connection can never touch the current one.
    /// </summary>
    private sealed class Link(TcpClient tcp, Stream stream, X509Certificate2? clientCertificate, LinkSettings settings)
    {
        private readonly CancellationTokenSource _cts = new();
        private int _closed;

        public Stream Stream { get; } = stream;
        public LinkSettings Settings { get; } = settings;
        public CancellationToken Token => _cts.Token;
        public Task? ReceiveTask { get; set; }

        public void Cancel()
        {
            try { _cts.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        public async Task CloseAsync()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 1)
                return;

            Cancel();

            // Closing the socket also unblocks any read or write that ignores cancellation.
            try { await Stream.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or NotSupportedException) { }

            tcp.Dispose();
            clientCertificate?.Dispose();
        }
    }
}
