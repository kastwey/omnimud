using Omnimud.Core.Connection;
using Omnimud.Core.Options;
using Omnimud.Core.Sound;

namespace Omnimud.Core.Updates;

/// <summary>
/// The update check as the application runs it: every check reads the GLOBAL options, asks
/// <see cref="IProxySettingsResolver"/> what downloads go through (direct, the system proxy or the manual one,
/// with its credentials) and makes the request with a client built for exactly that decision. The client lives
/// for one check, so a change in the proxy options is honoured by the next one.
/// </summary>
public sealed class ProxyAwareUpdateChecker : IUpdateChecker
{
    private readonly IOptionsService _options;
    private readonly IProxySettingsResolver _proxy;
    private readonly Func<DownloadProxySettings, HttpClient> _clientFactory;
    private readonly SemanticVersion _current;
    private readonly GitHubUpdateCheckerSettings _settings;

    /// <param name="clientFactory">Creates the client for a proxy decision (the checker disposes it); null = <see cref="HttpSoundDownloader.CreateClient"/>.</param>
    public ProxyAwareUpdateChecker(IOptionsService options, IProxySettingsResolver proxy, SemanticVersion current,
        Func<DownloadProxySettings, HttpClient>? clientFactory = null, GitHubUpdateCheckerSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(proxy);
        ArgumentNullException.ThrowIfNull(current);
        _options = options;
        _proxy = proxy;
        _current = current;
        _clientFactory = clientFactory ?? HttpSoundDownloader.CreateClient;
        _settings = settings ?? GitHubUpdateCheckerSettings.Default;
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        var options = await _options.ResolveAsync(null, null, ct).ConfigureAwait(false);
        using var client = _clientFactory(_proxy.ForDownloads(options));
        return await new GitHubUpdateChecker(client, _current, _settings).CheckAsync(ct).ConfigureAwait(false);
    }
}
