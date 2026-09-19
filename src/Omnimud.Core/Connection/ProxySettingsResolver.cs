using Omnimud.Core.Options;
using Omnimud.Core.Security;
using Omnimud.Core.Sound;

namespace Omnimud.Core.Connection;

/// <summary>
/// The single place where the proxy options become something a connection can use.
/// </summary>
public interface IProxySettingsResolver
{
    /// <summary>
    /// Proxy for the connection to the MUD at <paramref name="host"/>:<paramref name="port"/>; null = direct.
    /// May take a moment in automatic mode (the system may run a PAC script): do not call it from the UI thread.
    /// </summary>
    ProxyConfig? ForMud(OmnimudOptions options, string host, int port);

    /// <summary>Proxy for HTTP downloads (sounds). Does not depend on <see cref="OmnimudOptions.UseProxyForMud"/>.</summary>
    DownloadProxySettings ForDownloads(OmnimudOptions options);
}

/// <summary>
/// Rules:
/// <list type="bullet">
/// <item><see cref="ProxyMode.Disabled"/>: everything connects directly.</item>
/// <item>The MUD only uses a proxy with <see cref="OmnimudOptions.UseProxyForMud"/> (the original client never
/// proxied the MUD). Manual: the configured host, port and protocol, always — also for a MUD on this very
/// machine, because the user said so. Automatic: what <see cref="ISystemProxyResolver"/> decides (which never
/// proxies the local machine and honours the system's exclusions).</item>
/// <item>Downloads follow <see cref="OmnimudOptions.ProxyType"/> in any case. Manual: <c>http://</c> or
/// <c>socks5://</c> according to the protocol. Automatic: the system proxy as .NET resolves it per URL.</item>
/// <item>User name and password apply to manual AND automatic (a system proxy may ask for them too). Without a
/// user name no credentials are sent, even if a password is stored. A password that cannot be decrypted counts
/// as no password.</item>
/// </list>
/// </summary>
public sealed class ProxySettingsResolver : IProxySettingsResolver
{
    private readonly IProxyCredentialStore _credentials;
    private readonly ISystemProxyResolver _system;

    public ProxySettingsResolver(IProxyCredentialStore credentials, ISystemProxyResolver system)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(system);
        _credentials = credentials;
        _system = system;
    }

    /// <summary>No stored passwords and no system proxy: what a session gets when nobody gives it a resolver.</summary>
    public static ProxySettingsResolver Basic { get; } = new(NullProxyCredentialStore.Instance, NoSystemProxyResolver.Instance);

    public ProxyConfig? ForMud(OmnimudOptions options, string host, int port)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.UseProxyForMud)
            return null;

        switch (options.ProxyType)
        {
            case ProxyMode.Manual:
                if (!HasManualProxy(options))
                    return null;
                var (user, password) = Credentials(options);
                return new ProxyConfig(options.ProxyHost!.Trim(), options.ProxyPort, options.ProxyProtocol, user, password);

            case ProxyMode.Automatic:
                if (_system.Resolve(host, port) is not { } system)
                    return null;
                (user, password) = Credentials(options);
                return new ProxyConfig(system.Host, system.Port, system.Protocol, user, password);

            default:
                return null;
        }
    }

    public DownloadProxySettings ForDownloads(OmnimudOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        switch (options.ProxyType)
        {
            case ProxyMode.Manual when HasManualProxy(options):
                var scheme = options.ProxyProtocol == ProxyProtocol.Socks5 ? "socks5" : "http";
                var host = options.ProxyHost!.Trim();
                if (host.Contains(':') && !host.StartsWith('[')) host = $"[{host}]";
                if (!Uri.TryCreate($"{scheme}://{host}:{options.ProxyPort}", UriKind.Absolute, out var address))
                    return DownloadProxySettings.Direct;
                var (user, password) = Credentials(options);
                return new DownloadProxySettings(DownloadProxyKind.Manual, address, user, password);

            case ProxyMode.Automatic:
                (user, password) = Credentials(options);
                return new DownloadProxySettings(DownloadProxyKind.System, null, user, password);

            default:
                return DownloadProxySettings.Direct;
        }
    }

    private static bool HasManualProxy(OmnimudOptions options) =>
        !string.IsNullOrWhiteSpace(options.ProxyHost) && options.ProxyPort is >= 1 and <= 65535;

    private (string? User, string? Password) Credentials(OmnimudOptions options)
    {
        var user = string.IsNullOrWhiteSpace(options.ProxyUsername) ? null : options.ProxyUsername.Trim();
        return user is null ? (null, null) : (user, _credentials.GetPassword(options));
    }
}
