using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Omnimud.Core.Options;

namespace Omnimud.Core.Connection;

/// <summary>
/// Opens a tunnel to the MUD over an already connected proxy stream. After <see cref="EstablishAsync"/>
/// returns, the stream carries the MUD's bytes verbatim, so TLS can be layered on top.
/// </summary>
internal static class ProxyTunnel
{
    private const int MaxHttpHeaderBytes = 16 * 1024;

    public static void Validate(ProxyConfig proxy, string targetHost)
    {
        if (string.IsNullOrWhiteSpace(proxy.Host))
            throw new ArgumentException("The proxy host is empty.", nameof(proxy));
        if (proxy.Port is < 1 or > 65535)
            throw new ArgumentException($"The proxy port {proxy.Port} is out of range (1-65535).", nameof(proxy));
        if (!Enum.IsDefined(proxy.Protocol))
            throw new ArgumentException($"Unknown proxy protocol '{proxy.Protocol}'.", nameof(proxy));
        if (proxy.Username is null && !string.IsNullOrEmpty(proxy.Password))
            throw new ArgumentException("A proxy password was given without a user name.", nameof(proxy));

        if (proxy.Protocol == ProxyProtocol.Socks5)
        {
            if (proxy.Username is not null &&
                (Encoding.UTF8.GetByteCount(proxy.Username) is < 1 or > 255 ||
                 Encoding.UTF8.GetByteCount(proxy.Password ?? "") > 255))
                throw new ArgumentException("SOCKS5 user name and password must be 1-255 and 0-255 bytes long.", nameof(proxy));

            if (!IPAddress.TryParse(targetHost, out _) && ToAsciiHost(targetHost).Length > 255)
                throw new ArgumentException("The host name is too long for SOCKS5 (255 bytes).", nameof(proxy));
        }
        else if (proxy.Username is not null && proxy.Username.Contains(':'))
        {
            throw new ArgumentException("An HTTP proxy user name cannot contain ':'.", nameof(proxy));
        }
    }

    public static Task EstablishAsync(Stream stream, ProxyConfig proxy, string targetHost, int targetPort, CancellationToken ct) =>
        proxy.Protocol switch
        {
            ProxyProtocol.Socks5 => Socks5Async(stream, proxy, targetHost, targetPort, ct),
            ProxyProtocol.HttpConnect => HttpConnectAsync(stream, proxy, targetHost, targetPort, ct),
            _ => throw new ArgumentException($"Unknown proxy protocol '{proxy.Protocol}'.", nameof(proxy))
        };

    // ── SOCKS5 (RFC 1928) with optional user/password (RFC 1929) ─────────────

    private static async Task Socks5Async(Stream stream, ProxyConfig proxy, string host, int port, CancellationToken ct)
    {
        var useAuth = proxy.Username is not null;

        byte[] greeting = useAuth ? [0x05, 0x02, 0x00, 0x02] : [0x05, 0x01, 0x00];
        await stream.WriteAsync(greeting, ct).ConfigureAwait(false);

        var choice = await ReadExactAsync(stream, 2, ProxyProtocol.Socks5, ct).ConfigureAwait(false);
        if (choice[0] != 0x05)
            throw new ProxyException(ProxyProtocol.Socks5, ProxyErrorKind.ProtocolError,
                $"The proxy did not answer as a SOCKS5 server (version byte 0x{choice[0]:X2}).");

        switch (choice[1])
        {
            case 0x00:
                break;
            case 0x02 when useAuth:
                await Socks5AuthenticateAsync(stream, proxy, ct).ConfigureAwait(false);
                break;
            case 0xFF:
                throw new ProxyException(ProxyProtocol.Socks5, ProxyErrorKind.AuthenticationFailed,
                    useAuth
                        ? "The SOCKS5 proxy accepts none of the offered authentication methods."
                        : "The SOCKS5 proxy requires authentication and no user name was configured.");
            default:
                throw new ProxyException(ProxyProtocol.Socks5, ProxyErrorKind.ProtocolError,
                    $"The SOCKS5 proxy selected an authentication method that was not offered (0x{choice[1]:X2}).");
        }

        var request = new List<byte>(300) { 0x05, 0x01, 0x00 };
        if (IPAddress.TryParse(host, out var ip))
        {
            request.Add(ip.AddressFamily == AddressFamily.InterNetworkV6 ? (byte)0x04 : (byte)0x01);
            request.AddRange(ip.GetAddressBytes());
        }
        else
        {
            // ATYP 3: the proxy resolves the name, so nothing about the MUD leaks to the local DNS.
            var name = Encoding.ASCII.GetBytes(ToAsciiHost(host));
            request.Add(0x03);
            request.Add((byte)name.Length);
            request.AddRange(name);
        }
        request.Add((byte)(port >> 8));
        request.Add((byte)port);
        await stream.WriteAsync(request.ToArray(), ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        var reply = await ReadExactAsync(stream, 4, ProxyProtocol.Socks5, ct).ConfigureAwait(false);
        if (reply[0] != 0x05)
            throw new ProxyException(ProxyProtocol.Socks5, ProxyErrorKind.ProtocolError,
                $"Malformed SOCKS5 reply (version byte 0x{reply[0]:X2}).");
        if (reply[1] != 0x00)
            throw new ProxyException(ProxyProtocol.Socks5, ProxyErrorKind.Rejected,
                $"The SOCKS5 proxy could not connect to {host}:{port}: {DescribeSocks5Reply(reply[1])}.", reply[1]);

        // The bound address must be consumed so it is not mistaken for MUD data.
        var addressLength = reply[3] switch
        {
            0x01 => 4,
            0x04 => 16,
            0x03 => (await ReadExactAsync(stream, 1, ProxyProtocol.Socks5, ct).ConfigureAwait(false))[0],
            _ => throw new ProxyException(ProxyProtocol.Socks5, ProxyErrorKind.ProtocolError,
                $"Malformed SOCKS5 reply (address type 0x{reply[3]:X2}).")
        };
        await ReadExactAsync(stream, addressLength + 2, ProxyProtocol.Socks5, ct).ConfigureAwait(false);
    }

    private static async Task Socks5AuthenticateAsync(Stream stream, ProxyConfig proxy, CancellationToken ct)
    {
        var user = Encoding.UTF8.GetBytes(proxy.Username!);
        var password = Encoding.UTF8.GetBytes(proxy.Password ?? "");

        var message = new byte[3 + user.Length + password.Length];
        message[0] = 0x01;
        message[1] = (byte)user.Length;
        user.CopyTo(message, 2);
        message[2 + user.Length] = (byte)password.Length;
        password.CopyTo(message, 3 + user.Length);
        await stream.WriteAsync(message, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        var status = await ReadExactAsync(stream, 2, ProxyProtocol.Socks5, ct).ConfigureAwait(false);
        if (status[1] != 0x00)
            throw new ProxyException(ProxyProtocol.Socks5, ProxyErrorKind.AuthenticationFailed,
                "The SOCKS5 proxy rejected the user name or password.", status[1]);
    }

    private static string DescribeSocks5Reply(byte code) => code switch
    {
        0x01 => "general SOCKS server failure",
        0x02 => "connection not allowed by the proxy rules",
        0x03 => "network unreachable",
        0x04 => "host unreachable",
        0x05 => "connection refused",
        0x06 => "TTL expired",
        0x07 => "command not supported",
        0x08 => "address type not supported",
        _ => $"unknown error 0x{code:X2}"
    };

    // ── HTTP CONNECT ─────────────────────────────────────────────────────────

    private static async Task HttpConnectAsync(Stream stream, ProxyConfig proxy, string host, int port, CancellationToken ct)
    {
        var authority = IPAddress.TryParse(host, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6
            ? $"[{host}]:{port}"
            : $"{ToAsciiHost(host)}:{port}";

        var request = new StringBuilder()
            .Append("CONNECT ").Append(authority).Append(" HTTP/1.1\r\n")
            .Append("Host: ").Append(authority).Append("\r\n")
            .Append("Proxy-Connection: Keep-Alive\r\n");
        if (proxy.Username is not null)
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{proxy.Username}:{proxy.Password}"));
            request.Append("Proxy-Authorization: Basic ").Append(token).Append("\r\n");
        }
        request.Append("\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(request.ToString()), ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        var statusLine = await ReadHttpStatusLineAsync(stream, ct).ConfigureAwait(false);

        // "HTTP/1.1 200 Connection established"
        var parts = statusLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[0].StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var status))
            throw new ProxyException(ProxyProtocol.HttpConnect, ProxyErrorKind.ProtocolError,
                $"The proxy did not answer with an HTTP status line: \"{Truncate(statusLine)}\".");

        if (status is >= 200 and < 300)
            return;

        var reason = parts.Length == 3 ? parts[2] : "";
        if (status == 407)
            throw new ProxyException(ProxyProtocol.HttpConnect, ProxyErrorKind.AuthenticationFailed,
                proxy.Username is null
                    ? "The HTTP proxy requires authentication (407) and no user name was configured."
                    : "The HTTP proxy rejected the user name or password (407).", status);

        throw new ProxyException(ProxyProtocol.HttpConnect, ProxyErrorKind.Rejected,
            $"The HTTP proxy refused to connect to {host}:{port}: {status} {reason}".TrimEnd() + ".", status);
    }

    /// <summary>
    /// Reads the response head one byte at a time: anything after the blank line already belongs to the MUD
    /// and must stay in the stream.
    /// </summary>
    private static async Task<string> ReadHttpStatusLineAsync(Stream stream, CancellationToken ct)
    {
        var head = new List<byte>(256);
        var one = new byte[1];

        while (true)
        {
            var read = await stream.ReadAsync(one, ct).ConfigureAwait(false);
            if (read == 0)
                throw new ProxyException(ProxyProtocol.HttpConnect, ProxyErrorKind.ProtocolError,
                    "The HTTP proxy closed the connection before answering.");

            head.Add(one[0]);
            var n = head.Count;
            if (one[0] == (byte)'\n' &&
                ((n >= 4 && head[n - 2] == '\r' && head[n - 3] == '\n' && head[n - 4] == '\r') ||
                 (n >= 2 && head[n - 2] == '\n')))
                break;

            if (n > MaxHttpHeaderBytes)
                throw new ProxyException(ProxyProtocol.HttpConnect, ProxyErrorKind.ProtocolError,
                    "The HTTP proxy sent an oversized response header.");
        }

        var text = Encoding.Latin1.GetString(head.ToArray());
        var end = text.IndexOf('\n');
        return text[..end].TrimEnd('\r');
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static async Task<byte[]> ReadExactAsync(Stream stream, int count, ProxyProtocol protocol, CancellationToken ct)
    {
        var buffer = new byte[count];
        try
        {
            await stream.ReadExactlyAsync(buffer, ct).ConfigureAwait(false);
        }
        catch (EndOfStreamException ex)
        {
            throw new ProxyException(protocol, ProxyErrorKind.ProtocolError,
                "The proxy closed the connection during the handshake.", innerException: ex);
        }
        return buffer;
    }

    private static string ToAsciiHost(string host)
    {
        try { return new IdnMapping().GetAscii(host); }
        catch (ArgumentException) { return host; }
    }

    private static string Truncate(string text) => text.Length <= 80 ? text : text[..80] + "…";
}
