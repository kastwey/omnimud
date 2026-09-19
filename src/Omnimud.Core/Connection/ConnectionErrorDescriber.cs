using Omnimud.Core.Options;
using Omnimud.Core.Resources;

namespace Omnimud.Core.Connection;

/// <summary>
/// What to tell the user when a connection attempt fails. Proxy failures get a localized explanation by
/// <see cref="ProxyException.Kind"/> that says where to fix it; nothing here ever contains credentials
/// (no <see cref="ProxyException"/> message does either). Other failures keep their own message.
/// </summary>
public static class ConnectionErrorDescriber
{
    public static string Describe(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);

        for (var e = error; e is not null; e = e.InnerException)
        {
            if (e is ProxyException proxy)
                return Describe(proxy);
        }
        return error.Message;
    }

    public static string Describe(ProxyException error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var protocol = error.Protocol == ProxyProtocol.HttpConnect ? Strings.Proxy_NameHttpConnect : Strings.Proxy_NameSocks5;

        return error.Kind switch
        {
            ProxyErrorKind.ConnectionFailed => string.Format(Strings.Proxy_ConnectionFailed, protocol),
            ProxyErrorKind.AuthenticationFailed => string.Format(Strings.Proxy_AuthenticationFailed, protocol),
            ProxyErrorKind.Rejected when error.StatusCode is { } code => string.Format(Strings.Proxy_RejectedWithCode, protocol, code),
            ProxyErrorKind.Rejected => string.Format(Strings.Proxy_Rejected, protocol),
            _ => string.Format(Strings.Proxy_ProtocolError, protocol)
        };
    }
}
