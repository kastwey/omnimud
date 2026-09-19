using System.Collections.Concurrent;
using System.Net;
using Omnimud.Core.Resources;

namespace Omnimud.Core.Sound;

/// <summary>
/// Downloads sound files from a URL to local storage. Every failure is reported with an
/// exception and leaves no file behind.
/// </summary>
public interface ISoundDownloader
{
    /// <summary>Downloads a file from url to localPath. Only HTTPS is allowed.</summary>
    Task DownloadAsync(string url, string localPath, CancellationToken ct = default);

    /// <summary>Downloads a file from url to localPath with explicit options (plain HTTP, proxy).</summary>
    Task DownloadAsync(string url, string localPath, SoundDownloadOptions options, CancellationToken ct = default);
}

public sealed record SoundDownloadOptions
{
    public static SoundDownloadOptions Default { get; } = new();

    /// <summary>Accept http:// URLs. Old MUDs publish their sounds without TLS.</summary>
    public bool AllowHttp { get; init; }

    /// <summary>Proxy for this download; null = whatever the injected HttpClient does.</summary>
    public Uri? Proxy { get; init; }

    /// <summary>
    /// Complete proxy decision (direct, system or manual, with credentials). When set it wins over
    /// <see cref="Proxy"/>; null keeps the behaviour of callers that only know <see cref="Proxy"/>.
    /// </summary>
    public DownloadProxySettings? ProxySettings { get; init; }
}

/// <summary>
/// Secure implementation of ISoundDownloader using HttpClient: extension whitelist, size cap,
/// HTML and empty responses rejected, and the file only appears once it is complete.
/// </summary>
public sealed class HttpSoundDownloader : ISoundDownloader, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly long _maxFileSizeBytes;
    private readonly Func<Uri, HttpClient> _proxyClientFactory;
    private readonly Func<DownloadProxySettings, HttpClient> _settingsClientFactory;
    private readonly ConcurrentDictionary<Uri, Lazy<HttpClient>> _proxyClients = new();
    // Keyed by the whole record, so new credentials get a new client. The few stale ones die with the downloader.
    private readonly ConcurrentDictionary<DownloadProxySettings, Lazy<HttpClient>> _settingsClients = new();
    private static readonly HashSet<string> _allowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".mp3", ".ogg", ".mid", ".midi", ".flac", ".aac", ".wma"
    };

    public HttpSoundDownloader(HttpClient httpClient, long maxFileSizeBytes = 10 * 1024 * 1024)
        : this(httpClient, CreateProxyClient, maxFileSizeBytes)
    {
    }

    /// <param name="proxyClientFactory">Creates the client used for a given proxy; the downloader owns and disposes it.</param>
    public HttpSoundDownloader(HttpClient httpClient, Func<Uri, HttpClient> proxyClientFactory, long maxFileSizeBytes = 10 * 1024 * 1024)
        : this(httpClient, proxyClientFactory, CreateClient, maxFileSizeBytes)
    {
    }

    /// <param name="settingsClientFactory">Creates the client for a <see cref="SoundDownloadOptions.ProxySettings"/> value; the downloader owns and disposes it.</param>
    public HttpSoundDownloader(HttpClient httpClient, Func<Uri, HttpClient> proxyClientFactory,
        Func<DownloadProxySettings, HttpClient> settingsClientFactory, long maxFileSizeBytes = 10 * 1024 * 1024)
    {
        _httpClient = httpClient;
        _proxyClientFactory = proxyClientFactory;
        _settingsClientFactory = settingsClientFactory;
        _maxFileSizeBytes = maxFileSizeBytes;
    }

    public Task DownloadAsync(string url, string localPath, CancellationToken ct = default) =>
        DownloadAsync(url, localPath, SoundDownloadOptions.Default, ct);

    public async Task DownloadAsync(string url, string localPath, SoundDownloadOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Security: HTTPS, or HTTP only when the user opted in
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !(uri.Scheme == Uri.UriSchemeHttps || (options.AllowHttp && uri.Scheme == Uri.UriSchemeHttp)))
            throw new InvalidOperationException(Strings.Error_OnlyHttpsAllowed);

        // Validate file extension
        var ext = Path.GetExtension(localPath);
        if (!_allowedExtensions.Contains(ext))
            throw new InvalidOperationException(string.Format(Strings.Error_ExtensionNotAllowed, ext));

        var client = options.ProxySettings is { } settings
            ? _settingsClients.GetOrAdd(settings, s => new Lazy<HttpClient>(() => _settingsClientFactory(s))).Value
            : options.Proxy is null
                ? _httpClient
                : _proxyClients.GetOrAdd(options.Proxy, p => new Lazy<HttpClient>(() => _proxyClientFactory(p))).Value;

        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        // Servers that answer 200 with an error page, or with a meta refresh, instead of 404
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not null && mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The server answered with '{mediaType}' instead of an audio file.");
        if (response.Headers.Contains("Refresh") || response.Headers.Contains("Content-Refresh"))
            throw new InvalidOperationException("The server answered with a redirection page instead of an audio file.");

        // Check content length
        if (response.Content.Headers.ContentLength > _maxFileSizeBytes)
            throw new InvalidOperationException(string.Format(Strings.Error_FileTooLarge, _maxFileSizeBytes));
        if (response.Content.Headers.ContentLength == 0)
            throw new InvalidOperationException("The server answered with an empty file.");

        // Ensure directory exists
        var dir = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Written next to the target and renamed at the end, so a half-downloaded file is never played
        var tempPath = localPath + "." + Guid.NewGuid().ToString("N")[..8] + ".part";
        try
        {
            long totalRead = 0;
            await using (var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true))
            {
                var buffer = new byte[8192];
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    totalRead += bytesRead;
                    if (totalRead > _maxFileSizeBytes)
                        throw new InvalidOperationException(string.Format(Strings.Error_FileTooLarge, _maxFileSizeBytes));
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
                }
            }

            if (totalRead == 0)
                throw new InvalidOperationException("The server answered with an empty file.");

            File.Move(tempPath, localPath, overwrite: true);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    public void Dispose()
    {
        foreach (var client in _proxyClients.Values)
        {
            if (client.IsValueCreated)
                client.Value.Dispose();
        }
        _proxyClients.Clear();

        foreach (var client in _settingsClients.Values)
        {
            if (client.IsValueCreated)
                client.Value.Dispose();
        }
        _settingsClients.Clear();
    }

    /// <summary>
    /// The HttpClient for one proxy decision. With a user name the proxy gets those credentials (Basic, Digest,
    /// NTLM or SOCKS5 user/password, whatever it asks for); without one, the Windows credentials of the user, as
    /// the original client did.
    /// </summary>
    public static HttpClient CreateClient(DownloadProxySettings proxy)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        var handler = new HttpClientHandler();
        ICredentials credentials = proxy.HasCredentials
            ? new NetworkCredential(proxy.Username, proxy.Password ?? string.Empty)
            : CredentialCache.DefaultCredentials;

        switch (proxy.Kind)
        {
            case DownloadProxyKind.Manual when proxy.Address is not null:
                handler.UseProxy = true;
                handler.Proxy = new WebProxy(proxy.Address) { Credentials = credentials };
                break;
            case DownloadProxyKind.System:
                handler.UseProxy = true;
                handler.DefaultProxyCredentials = credentials;
                break;
            default:
                handler.UseProxy = false;
                break;
        }

        return new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(60) };
    }

    private static HttpClient CreateProxyClient(Uri proxy) =>
        new(new HttpClientHandler { Proxy = new WebProxy(proxy) { UseDefaultCredentials = true }, UseProxy = true }, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(60)
        };

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
