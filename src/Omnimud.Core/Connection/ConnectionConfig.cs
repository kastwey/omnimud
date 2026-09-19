using Omnimud.Core.Options;

namespace Omnimud.Core.Connection;

/// <summary>
/// Everything <see cref="IConnection.ConnectAsync"/> needs. The positional parameters keep their
/// historical order; newer settings are init-only properties so <c>new ConnectionConfig(host, port)</c>
/// keeps compiling.
/// </summary>
public sealed record ConnectionConfig(
    string Host,
    int Port,
    bool UseTls = false,
    TlsConfig? TlsOptions = null,
    ProxyConfig? Proxy = null,
    TimeSpan? ConnectTimeout = null
)
{
    /// <summary>
    /// Name of the encoding used by <see cref="IConnection.SendAsync"/>, e.g. "utf-8", "iso-8859-1",
    /// "windows-1252". Resolved with <see cref="System.Text.Encoding.GetEncoding(string)"/>, so code page
    /// encodings need <c>CodePagesEncodingProvider</c> registered (the application does it at start-up).
    /// </summary>
    public string TextEncoding { get; init; } = "utf-8";

    /// <summary>Appended to every command sent with <see cref="IConnection.SendAsync"/>. Telnet mandates CR LF.</summary>
    public string LineTerminator { get; init; } = "\r\n";

    /// <summary>
    /// Doubles the byte 0xFF (telnet IAC) in text sent with <see cref="IConnection.SendAsync"/>, as the telnet
    /// protocol requires. Only matters for single-byte encodings ('ÿ' is 0xFF in Latin-1); UTF-8 never
    /// produces 0xFF. <see cref="IConnection.SendRawAsync"/> is never escaped.
    /// </summary>
    public bool EscapeTelnetIac { get; init; } = true;

    /// <summary>
    /// When set, the connection is dropped with <see cref="DisconnectReason.Timeout"/> if nothing is received
    /// for this long. Disabled by default: an idle MUD is perfectly normal.
    /// </summary>
    public TimeSpan? IdleTimeout { get; init; }

    /// <summary>
    /// TCP keep-alive probes let the OS notice a dead peer (pulled cable, NAT entry expired); the resulting
    /// read failure is reported as <see cref="DisconnectReason.Timeout"/>.
    /// </summary>
    public bool TcpKeepAlive { get; init; } = true;
}

public sealed record TlsConfig(
    bool ValidateCertificate = true,
    string? ClientCertificatePath = null
)
{
    /// <summary>Password of the PFX in <see cref="ClientCertificatePath"/>; null or empty when it has none.</summary>
    public string? ClientCertificatePassword { get; init; }

    /// <summary>
    /// Name sent as SNI and checked against the certificate. Defaults to <see cref="ConnectionConfig.Host"/>.
    /// </summary>
    public string? TargetHost { get; init; }

    /// <summary>
    /// Off by default. With online checking .NET fails the handshake whenever the CA's CRL/OCSP endpoint is
    /// unreachable or slow, which for a game client means random connection failures and extra latency;
    /// browsers do not hard-fail on revocation either.
    /// </summary>
    public bool CheckCertificateRevocation { get; init; }
}

/// <summary>Proxy used to reach the MUD. With a proxy the MUD host name is resolved by the proxy.</summary>
public sealed record ProxyConfig(
    string Host,
    int Port,
    ProxyProtocol Protocol = ProxyProtocol.Socks5,
    string? Username = null,
    string? Password = null
)
{
    // Keeps the password out of logs and debugger tooltips that print the record.
    public override string ToString() =>
        $"ProxyConfig {{ Host = {Host}, Port = {Port}, Protocol = {Protocol}, Username = {Username}, Password = {(Password is null ? "null" : "***")} }}";
}
