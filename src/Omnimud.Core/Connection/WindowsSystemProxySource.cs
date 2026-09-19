using System.Globalization;
using Microsoft.Win32;

namespace Omnimud.Core.Connection;

/// <summary>
/// The real system: the static proxy of the current user is READ (never written) from
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings</c> (ProxyEnable, ProxyServer,
/// ProxyOverride), which is the only reliable place to see a <c>socks=</c> entry: .NET's own default proxy only
/// understands the http and https ones. PAC scripts, auto-detection and the HTTP_PROXY family of variables are left to
/// <see cref="HttpClient.DefaultProxy"/>. Outside Windows only that second part applies.
/// </summary>
public sealed class WindowsSystemProxySource : ISystemProxySource
{
    private const string InternetSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    public SystemProxySettings ReadSettings()
    {
        if (!OperatingSystem.IsWindows())
            return SystemProxySettings.None;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, writable: false);
            if (key is null)
                return SystemProxySettings.None;

            var enabled = Convert.ToInt32(key.GetValue("ProxyEnable", 0), CultureInfo.InvariantCulture) != 0;
            return new SystemProxySettings(enabled, key.GetValue("ProxyServer") as string, key.GetValue("ProxyOverride") as string);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException
                                       or FormatException or InvalidCastException or OverflowException)
        {
            return SystemProxySettings.None;
        }
    }

    public Uri? GetWebProxy(Uri destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var proxy = HttpClient.DefaultProxy;
        if (proxy.IsBypassed(destination))
            return null;

        var address = proxy.GetProxy(destination);
        return address is null || address == destination ? null : address;
    }
}
