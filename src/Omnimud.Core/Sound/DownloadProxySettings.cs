namespace Omnimud.Core.Sound;

public enum DownloadProxyKind
{
    /// <summary>No proxy at all, whatever the system says.</summary>
    Direct,
    /// <summary>The proxy of the operating system (static, PAC or auto-detected), as .NET resolves it per URL.</summary>
    System,
    /// <summary>The proxy in <see cref="DownloadProxySettings.Address"/>.</summary>
    Manual
}

/// <summary>
/// How HTTP downloads reach the network. A value type on purpose: the downloader keeps one HttpClient per
/// distinct value. The password is only ever handed to <see cref="System.Net.NetworkCredential"/>.
/// </summary>
public sealed record DownloadProxySettings(DownloadProxyKind Kind, Uri? Address = null, string? Username = null, string? Password = null)
{
    public static DownloadProxySettings Direct { get; } = new(DownloadProxyKind.Direct);

    public bool HasCredentials => !string.IsNullOrEmpty(Username);

    // Keeps the password out of logs and debugger tooltips that print the record.
    public override string ToString() =>
        $"DownloadProxySettings {{ Kind = {Kind}, Address = {Address}, Username = {Username}, Password = {(Password is null ? "null" : "***")} }}";
}
