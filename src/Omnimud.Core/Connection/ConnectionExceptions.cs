using System.Net.Security;
using Omnimud.Core.Options;

namespace Omnimud.Core.Connection;

/// <summary>
/// The TLS handshake failed or the client certificate could not be loaded. When the server certificate was
/// rejected, <see cref="PolicyErrors"/> and <see cref="ChainStatus"/> say why, so the UI can explain it.
/// </summary>
public sealed class TlsHandshakeException : Exception
{
    public TlsHandshakeException(
        string message,
        SslPolicyErrors policyErrors = SslPolicyErrors.None,
        IReadOnlyList<string>? chainStatus = null,
        string? certificateSubject = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        PolicyErrors = policyErrors;
        ChainStatus = chainStatus ?? [];
        CertificateSubject = certificateSubject;
    }

    /// <summary><see cref="SslPolicyErrors.None"/> when the failure was not a certificate rejection.</summary>
    public SslPolicyErrors PolicyErrors { get; }

    /// <summary>One entry per X.509 chain problem, e.g. "UntrustedRoot: ...".</summary>
    public IReadOnlyList<string> ChainStatus { get; }

    public string? CertificateSubject { get; }

    public bool IsCertificateError => PolicyErrors != SslPolicyErrors.None;
}

public enum ProxyErrorKind
{
    /// <summary>The proxy itself could not be reached.</summary>
    ConnectionFailed,
    /// <summary>The proxy wants credentials, or rejected the ones given.</summary>
    AuthenticationFailed,
    /// <summary>The proxy answered but refused to open the tunnel (or could not reach the MUD).</summary>
    Rejected,
    /// <summary>The answer was not valid for the configured protocol.</summary>
    ProtocolError
}

public sealed class ProxyException : Exception
{
    public ProxyException(
        ProxyProtocol protocol,
        ProxyErrorKind kind,
        string message,
        int? statusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Protocol = protocol;
        Kind = kind;
        StatusCode = statusCode;
    }

    public ProxyProtocol Protocol { get; }
    public ProxyErrorKind Kind { get; }

    /// <summary>HTTP status code, or the SOCKS5 REP field; null when there was no such code.</summary>
    public int? StatusCode { get; }
}
