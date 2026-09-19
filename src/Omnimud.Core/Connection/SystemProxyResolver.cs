using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Omnimud.Core.Options;

namespace Omnimud.Core.Connection;

/// <summary>The proxy the operating system wants for one destination.</summary>
public sealed record SystemProxy(string Host, int Port, ProxyProtocol Protocol);

/// <summary>Decides which proxy of the system, if any, a raw TCP connection (the MUD) must go through.</summary>
public interface ISystemProxyResolver
{
    /// <summary>Null = connect directly.</summary>
    SystemProxy? Resolve(string host, int port);
}

/// <summary>
/// The static proxy configuration of the user ("Use a proxy server" in Windows), as stored by WinINet.
/// </summary>
/// <param name="ProxyEnabled">ProxyEnable ≠ 0.</param>
/// <param name="ProxyServer">"host:port", or per protocol "http=h:p;https=h:p;socks=h:p".</param>
/// <param name="ProxyOverride">Exclusions separated by ';': host patterns with '*', and "&lt;local&gt;".</param>
public sealed record SystemProxySettings(bool ProxyEnabled, string? ProxyServer, string? ProxyOverride)
{
    public static SystemProxySettings None { get; } = new(false, null, null);
}

/// <summary>Where <see cref="SystemProxyResolver"/> gets its facts from. Read-only by contract.</summary>
public interface ISystemProxySource
{
    SystemProxySettings ReadSettings();

    /// <summary>
    /// What the platform's own resolution (.NET default proxy: PAC script, auto-detection, environment
    /// variables) says for that URI; null when it says "direct" or cannot tell.
    /// </summary>
    Uri? GetWebProxy(Uri destination);
}

/// <summary>
/// Pure decision logic, in this order:
/// <list type="number">
/// <item>The local machine (localhost, 127.x.x.x, ::1) never goes through a proxy.</item>
/// <item>A static proxy that is switched on: the exclusion list is honoured (<c>&lt;local&gt;</c> = names without a
/// dot, patterns with <c>*</c>); then <c>socks=</c> wins (SOCKS is what WinINet itself uses for a protocol without
/// an entry of its own, and it is made for arbitrary TCP) → SOCKS5; else <c>https=</c>, else <c>http=</c>, else a
/// bare "host:port" → HTTP CONNECT. A list that only names other protocols (ftp=) means direct.</item>
/// <item>No static proxy: whatever the platform resolves for <c>http://host:port/</c> (PAC, WPAD, environment
/// variables) → HTTP CONNECT, or SOCKS5 when the answer has a socks scheme. Nothing resolved → direct.</item>
/// </list>
/// Limits: WinINet's <c>socks=</c> historically means SOCKS4; Omnimud speaks SOCKS5 to it (nearly every SOCKS
/// server speaks both). With a PAC script and a static proxy configured at once, the static one decides for the
/// MUD. A PAC script is asked as if the MUD were a web address, since there is no PAC notion of "telnet".
/// </summary>
public sealed class SystemProxyResolver : ISystemProxyResolver
{
    private const int DefaultHttpPort = 80;
    private const int DefaultSocksPort = 1080;

    private readonly ISystemProxySource _source;

    public SystemProxyResolver(ISystemProxySource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    public SystemProxy? Resolve(string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535)
            return null;

        host = host.Trim();
        if (IsLoopback(host))
            return null;

        SystemProxySettings settings;
        try
        {
            settings = _source.ReadSettings() ?? SystemProxySettings.None;
        }
        catch (Exception)
        {
            // A system that cannot be asked is a system without proxy; the connection itself will tell.
            settings = SystemProxySettings.None;
        }

        if (settings.ProxyEnabled && !string.IsNullOrWhiteSpace(settings.ProxyServer))
        {
            return IsBypassed(host, settings.ProxyOverride)
                ? null
                : FromProxyServer(settings.ProxyServer);
        }

        return FromWebProxy(host, port);
    }

    /// <summary>localhost, *.localhost, any 127.0.0.0/8 address and ::1 (also as IPv4-mapped).</summary>
    public static bool IsLoopback(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        host = host.Trim().TrimEnd('.');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        var literal = host.StartsWith('[') && host.EndsWith(']') ? host[1..^1] : host;
        if (!IPAddress.TryParse(literal, out var address)) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return IPAddress.IsLoopback(address);
    }

    /// <summary>WinINet's ProxyOverride: ';' or blank separated patterns, '*' wildcards, optional scheme and port, &lt;local&gt;.</summary>
    public static bool IsBypassed(string host, string? proxyOverride)
    {
        if (string.IsNullOrWhiteSpace(proxyOverride)) return false;
        host = host.Trim().TrimEnd('.');

        foreach (var raw in proxyOverride.Split([';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var entry = raw.Trim();
            if (entry.Equals("<local>", StringComparison.OrdinalIgnoreCase))
            {
                // "Bypass proxy server for local addresses": plain names, without a dot (and not IPv6 literals).
                if (!host.Contains('.') && !host.Contains(':')) return true;
                continue;
            }
            if (entry.StartsWith('<')) continue; // <-loopback> and friends

            var pattern = StripSchemeAndPort(entry);
            if (pattern.Length == 0) continue;

            var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
            if (Regex.IsMatch(host, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)))
                return true;
        }
        return false;
    }

    private static SystemProxy? FromProxyServer(string proxyServer)
    {
        string? bare = null, socks = null, https = null, http = null;
        foreach (var raw in proxyServer.Split([';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var entry = raw.Trim();
            var equals = entry.IndexOf('=');
            if (equals < 0)
            {
                bare ??= entry;
                continue;
            }

            var value = entry[(equals + 1)..].Trim();
            switch (entry[..equals].Trim().ToLowerInvariant())
            {
                case "socks": socks ??= value; break;
                case "https": https ??= value; break;
                case "http": http ??= value; break;
            }
        }

        if (socks is not null && TryParseEndpoint(socks, DefaultSocksPort, out var socksHost, out var socksPort))
            return new SystemProxy(socksHost, socksPort, ProxyProtocol.Socks5);

        foreach (var candidate in new[] { https, http, bare })
        {
            if (candidate is null) continue;
            if (candidate.StartsWith("socks", StringComparison.OrdinalIgnoreCase) && candidate.Contains("://") &&
                TryParseEndpoint(candidate, DefaultSocksPort, out var h, out var p))
                return new SystemProxy(h, p, ProxyProtocol.Socks5);
            if (TryParseEndpoint(candidate, DefaultHttpPort, out var httpHost, out var httpPort))
                return new SystemProxy(httpHost, httpPort, ProxyProtocol.HttpConnect);
        }
        return null;
    }

    private SystemProxy? FromWebProxy(string host, int port)
    {
        var authority = host.Contains(':') && !host.StartsWith('[') ? $"[{host}]" : host;
        if (!Uri.TryCreate($"http://{authority}:{port.ToString(CultureInfo.InvariantCulture)}/", UriKind.Absolute, out var destination))
            return null;

        Uri? proxy;
        try
        {
            proxy = _source.GetWebProxy(destination);
        }
        catch (Exception)
        {
            return null;
        }

        if (proxy is null || !proxy.IsAbsoluteUri || string.IsNullOrEmpty(proxy.Host))
            return null;
        // Some IWebProxy implementations answer "direct" by returning the destination itself.
        if (proxy.Host.Equals(destination.Host, StringComparison.OrdinalIgnoreCase) && proxy.Port == destination.Port)
            return null;

        var isSocks = proxy.Scheme.StartsWith("socks", StringComparison.OrdinalIgnoreCase);
        var proxyPort = proxy.Port > 0 ? proxy.Port : isSocks ? DefaultSocksPort : DefaultHttpPort;
        return new SystemProxy(proxy.IdnHost, proxyPort, isSocks ? ProxyProtocol.Socks5 : ProxyProtocol.HttpConnect);
    }

    private static string StripSchemeAndPort(string entry)
    {
        var scheme = entry.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) entry = entry[(scheme + 3)..];
        var slash = entry.IndexOf('/');
        if (slash >= 0) entry = entry[..slash];

        if (entry.StartsWith('['))
        {
            var close = entry.IndexOf(']');
            return close > 0 ? entry[1..close] : entry;
        }

        // One colon = host:port; more than one = a bare IPv6 address.
        var colon = entry.IndexOf(':');
        if (colon >= 0 && colon == entry.LastIndexOf(':')) entry = entry[..colon];
        return entry.Trim();
    }

    private static bool TryParseEndpoint(string value, int defaultPort, out string host, out int port)
    {
        host = string.Empty;
        port = defaultPort;

        value = value.Trim();
        var scheme = value.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) value = value[(scheme + 3)..];
        var slash = value.IndexOf('/');
        if (slash >= 0) value = value[..slash];
        var at = value.LastIndexOf('@');
        if (at >= 0) value = value[(at + 1)..];
        if (value.Length == 0) return false;

        string portText;
        if (value.StartsWith('['))
        {
            var close = value.IndexOf(']');
            if (close < 0) return false;
            host = value[1..close];
            portText = value.Length > close + 1 && value[close + 1] == ':' ? value[(close + 2)..] : string.Empty;
        }
        else
        {
            var colon = value.LastIndexOf(':');
            if (colon >= 0 && colon == value.IndexOf(':'))
            {
                host = value[..colon];
                portText = value[(colon + 1)..];
            }
            else
            {
                host = value;
                portText = string.Empty;
            }
        }

        host = host.Trim();
        if (host.Length == 0) return false;
        if (portText.Length == 0) return true;
        return int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is >= 1 and <= 65535;
    }
}

/// <summary>Never finds a proxy. For platforms and tests without a system configuration.</summary>
public sealed class NoSystemProxyResolver : ISystemProxyResolver
{
    public static NoSystemProxyResolver Instance { get; } = new();
    public SystemProxy? Resolve(string host, int port) => null;
}
