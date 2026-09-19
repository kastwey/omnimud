using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Omnimud.Core.Updates;

public sealed record GitHubUpdateCheckerSettings
{
    public static GitHubUpdateCheckerSettings Default { get; } = new();

    /// <summary>The public, anonymous endpoint with the latest published release of the project.</summary>
    public Uri LatestReleaseEndpoint { get; init; } = new("https://api.github.com/repos/kastwey/omnimud/releases/latest");

    /// <summary>Pre-releases are ignored unless the user opts in.</summary>
    public bool IncludePreReleases { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>An answer larger than this is not read: it cannot be what was asked for.</summary>
    public int MaxResponseBytes { get; init; } = 512 * 1024;

    /// <summary>Release notes are cut here; the page of the release has the rest.</summary>
    public int MaxNotesLength { get; init; } = 20_000;
}

/// <summary>
/// One anonymous GET to the public GitHub API. The request carries a User-Agent with the version of Omnimud
/// and nothing else: no cookies, no credentials, no identifier of the user or the machine. The answer is
/// untrusted: it is size-capped, parsed tolerantly, cleaned, and its link is only passed on when
/// <see cref="GitHubReleasePage.IsTrusted"/> says so.
/// </summary>
public sealed partial class GitHubUpdateChecker : IUpdateChecker
{
    private readonly HttpClient _http;
    private readonly SemanticVersion _current;
    private readonly GitHubUpdateCheckerSettings _settings;

    public GitHubUpdateChecker(HttpClient http, SemanticVersion current, GitHubUpdateCheckerSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(current);
        _http = http;
        _current = current;
        _settings = settings ?? GitHubUpdateCheckerSettings.Default;
        if (_settings.LatestReleaseEndpoint.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Updates are only checked over HTTPS.", nameof(settings));
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_settings.Timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _settings.LatestReleaseEndpoint);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Omnimud", UserAgentVersion(_current)));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound:
                    // The repository exists but nothing has been published yet.
                    return new UpToDate(_current, NoReleasesYet: true);
                case HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests:
                    return new UpdateCheckFailed(UpdateCheckFailure.RateLimited, Status(response));
            }
            if (!response.IsSuccessStatusCode)
                return new UpdateCheckFailed(UpdateCheckFailure.UnexpectedResponse, Status(response));

            var body = await ReadCappedAsync(response, timeout.Token).ConfigureAwait(false);
            return body is null
                ? new UpdateCheckFailed(UpdateCheckFailure.UnexpectedResponse, "The answer is too large.")
                : Interpret(body);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new UpdateCheckFailed(UpdateCheckFailure.Timeout);
        }
        catch (HttpRequestException ex)
        {
            return new UpdateCheckFailed(UpdateCheckFailure.Network, ex.Message);
        }
        catch (IOException ex)
        {
            return new UpdateCheckFailed(UpdateCheckFailure.Network, ex.Message);
        }
    }

    /// <summary>What a release document means for the running version. Public so it can be tested without HTTP.</summary>
    public UpdateCheckResult Interpret(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new UpdateCheckFailed(UpdateCheckFailure.UnexpectedResponse, "Not a release.");

            if (!TryParseTag(Text(root, "tag_name"), out var version))
                return new UpdateCheckFailed(UpdateCheckFailure.UnexpectedResponse, "The release has no recognizable version.");

            if (Flag(root, "draft")) return new UpToDate(_current);
            if (!_settings.IncludePreReleases && (Flag(root, "prerelease") || version.IsPreRelease)) return new UpToDate(_current);
            if (version <= _current) return new UpToDate(_current);

            var title = Clean(Text(root, "name"), singleLine: true, 200);
            if (title.Length == 0) title = version.ToString();
            var notes = Clean(Text(root, "body"), singleLine: false, _settings.MaxNotesLength);
            return new UpdateAvailable(version, title, notes, GitHubReleasePage.Trusted(Text(root, "html_url")));
        }
        catch (JsonException ex)
        {
            return new UpdateCheckFailed(UpdateCheckFailure.UnexpectedResponse, ex.Message);
        }
    }

    /// <summary>"v2.1.0", "2.1", "2.1.0-beta.1", and as a last resort the first version found inside ("release-2.1.0").</summary>
    internal static bool TryParseTag(string? tag, out SemanticVersion version)
    {
        if (SemanticVersion.TryParse(tag, out version)) return true;
        if (string.IsNullOrWhiteSpace(tag) || tag.Length > 100) return false;
        var match = VersionInsideText().Match(tag);
        return match.Success && SemanticVersion.TryParse(match.Value, out version);
    }

    private async Task<string?> ReadCappedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > _settings.MaxResponseBytes) return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > _settings.MaxResponseBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>Text from the server: no control characters (line breaks kept for the notes), no bidirectional overrides, capped.</summary>
    internal static string Clean(string? text, bool singleLine, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var builder = new StringBuilder(Math.Min(text.Length, maxLength));
        foreach (var c in text.ReplaceLineEndings("\n"))
        {
            if (builder.Length >= maxLength) { builder.Append('…'); break; }
            if (c == '\n') { builder.Append(singleLine ? " " : Environment.NewLine); continue; }
            if (c == '\t') { builder.Append(' '); continue; }
            if (char.IsControl(c) || c is (>= '‪' and <= '‮') or (>= '⁦' and <= '⁩') or '‎' or '‏') continue;
            builder.Append(c);
        }
        return builder.ToString().Trim();
    }

    private static string Status(HttpResponseMessage response) => $"HTTP {(int)response.StatusCode}";

    /// <summary>A product version must be a token: "2.1.0-beta.1" is fine, anything stranger falls back to major.minor.patch.</summary>
    private static string UserAgentVersion(SemanticVersion version)
    {
        var text = version.ToString();
        return text.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-') ? text : $"{version.Major}.{version.Minor}.{version.Patch}";
    }

    [GeneratedRegex(@"\d{1,9}(\.\d{1,9}){1,3}(-[0-9A-Za-z][0-9A-Za-z.\-]*)?", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex VersionInsideText();
}

/// <summary>The only kind of link an update notice may offer to open: an https page of the project at github.com.</summary>
public static class GitHubReleasePage
{
    public const string Host = "github.com";
    public const string ProjectPath = "/kastwey/omnimud/";

    /// <summary>All releases of the project: where the user is sent when a release has no usable page of its own.</summary>
    public static Uri AllReleases { get; } = new("https://github.com/kastwey/omnimud/releases");

    public static bool IsTrusted(Uri? uri) =>
        uri is { IsAbsoluteUri: true }
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort
        && string.IsNullOrEmpty(uri.UserInfo)
        && uri.Host.Equals(Host, StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith(ProjectPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>The address as a URI when it can be trusted, else null.</summary>
    public static Uri? Trusted(string? address) =>
        !string.IsNullOrWhiteSpace(address) && address.Length <= 500
        && Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri) && IsTrusted(uri) ? uri : null;
}
