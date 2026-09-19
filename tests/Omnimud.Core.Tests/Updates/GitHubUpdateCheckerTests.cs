using System.Net;
using System.Text;
using FluentAssertions;
using Omnimud.Core.Connection;
using Omnimud.Core.Options;
using Omnimud.Core.Security;
using Omnimud.Core.Sound;
using Omnimud.Core.Updates;

namespace Omnimud.Core.Tests.Updates;

/// <summary>No test here touches the network: every request ends in <see cref="FakeHandler"/>.</summary>
public sealed class GitHubUpdateCheckerTests
{
    private static readonly SemanticVersion Current = new(2, 0, 0);

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public FakeHandler(HttpStatusCode status, string body = "") : this((_, _) => Task.FromResult(Response(status, body))) { }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return respond(request, cancellationToken);
        }

        public static HttpResponseMessage Response(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static string Release(string tag, string? url = "https://github.com/kastwey/omnimud/releases/tag/v2.1.0",
        string name = "Omnimud 2.1", string body = "Notes", bool draft = false, bool prerelease = false) =>
        System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["tag_name"] = tag, ["html_url"] = url, ["name"] = name, ["body"] = body, ["draft"] = draft, ["prerelease"] = prerelease,
            ["assets"] = new[] { new { browser_download_url = "https://github.com/kastwey/omnimud/releases/download/v2.1.0/Omnimud.zip" } }
        });

    private static async Task<(UpdateCheckResult Result, FakeHandler Handler)> CheckAsync(FakeHandler handler,
        SemanticVersion? current = null, GitHubUpdateCheckerSettings? settings = null)
    {
        using var client = new HttpClient(handler);
        var result = await new GitHubUpdateChecker(client, current ?? Current, settings).CheckAsync();
        return (result, handler);
    }

    private static Task<(UpdateCheckResult Result, FakeHandler Handler)> CheckAsync(HttpStatusCode status, string body) =>
        CheckAsync(new FakeHandler(status, body));

    // ── 200 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NewerRelease_IsReportedWithVersionTitleNotesAndPage()
    {
        var (result, _) = await CheckAsync(HttpStatusCode.OK, Release("v2.1.0", body: "First line\r\nSecond line"));

        var update = result.Should().BeOfType<UpdateAvailable>().Subject;
        update.Version.Should().Be(new SemanticVersion(2, 1, 0));
        update.Title.Should().Be("Omnimud 2.1");
        update.Notes.Should().Be("First line" + Environment.NewLine + "Second line");
        update.ReleasePage.Should().Be(new Uri("https://github.com/kastwey/omnimud/releases/tag/v2.1.0"));
    }

    [Theory]
    [InlineData("v2.1.0")]
    [InlineData("2.1")]
    [InlineData("V2.0.1")]
    [InlineData("2.0.0.1")]
    [InlineData("release-2.1.0")]
    [InlineData("omnimud_v10.0")]
    public async Task TagSpellings_AreUnderstood(string tag)
    {
        var (result, _) = await CheckAsync(HttpStatusCode.OK, Release(tag));
        result.Should().BeOfType<UpdateAvailable>();
    }

    [Theory]
    [InlineData("v2.0.0")]
    [InlineData("2.0")]
    [InlineData("v1.9.9")]
    [InlineData("1.10.0")]
    public async Task SameOrOlderRelease_IsUpToDate(string tag)
    {
        var (result, _) = await CheckAsync(HttpStatusCode.OK, Release(tag));
        result.Should().Be(new UpToDate(Current));
    }

    /// <summary>With the comparison of the original client 1.10 equalled 1.1 and the user was never told.</summary>
    [Fact]
    public async Task OneDotTen_IsAnUpdateForOneDotNine()
    {
        var (result, _) = await CheckAsync(new FakeHandler(HttpStatusCode.OK, Release("v1.10.0")), current: new SemanticVersion(1, 9, 0));
        result.Should().BeOfType<UpdateAvailable>().Which.Version.ToString().Should().Be("1.10.0");
    }

    [Fact]
    public async Task Draft_IsIgnored()
    {
        var (result, _) = await CheckAsync(HttpStatusCode.OK, Release("v9.0.0", draft: true));
        result.Should().BeOfType<UpToDate>();
    }

    [Theory]
    [InlineData("v2.1.0", true)]
    [InlineData("v2.1.0-beta.1", false)]
    [InlineData("v2.1.0-rc.1", true)]
    public async Task PreRelease_IsIgnoredByDefault_WhetherFlaggedOrOnlyInTheTag(string tag, bool flagged)
    {
        var (result, _) = await CheckAsync(HttpStatusCode.OK, Release(tag, prerelease: flagged));
        result.Should().BeOfType<UpToDate>();
    }

    [Fact]
    public async Task PreRelease_IsOfferedWhenTheUserOptsIn()
    {
        var settings = new GitHubUpdateCheckerSettings { IncludePreReleases = true };
        var (result, _) = await CheckAsync(new FakeHandler(HttpStatusCode.OK, Release("v2.1.0-beta.1", prerelease: true)), settings: settings);
        result.Should().BeOfType<UpdateAvailable>().Which.Version.ToString().Should().Be("2.1.0-beta.1");
    }

    [Fact]
    public async Task ReleaseWithoutNameOrNotes_StillWorks()
    {
        var (result, _) = await CheckAsync(HttpStatusCode.OK, """{"tag_name":"v2.1.0","name":null,"body":null}""");

        var update = result.Should().BeOfType<UpdateAvailable>().Subject;
        update.Title.Should().Be("2.1.0");
        update.Notes.Should().BeEmpty();
        update.ReleasePage.Should().BeNull();
    }

    [Fact]
    public async Task NotesFromTheServer_AreCleanedAndCapped()
    {
        var notes = "Good\u0007 news\u202E!\tTabbed\0" + new string('x', 50_000);
        var (result, _) = await CheckAsync(HttpStatusCode.OK, Release("v2.1.0", body: notes, name: "Line one\nLine two"));

        var update = result.Should().BeOfType<UpdateAvailable>().Subject;
        update.Notes.Should().StartWith("Good news! Tabbed");
        update.Notes.Should().NotContain("\u202E").And.NotContain("\0").And.NotContain("\u0007");
        update.Notes.Length.Should().BeLessThanOrEqualTo(GitHubUpdateCheckerSettings.Default.MaxNotesLength + 1);
        update.Title.Should().Be("Line one Line two");
    }

    // ── The link is untrusted ──────────────────────────────────────────────

    [Theory]
    [InlineData("http://github.com/kastwey/omnimud/releases/tag/v2.1.0")]
    [InlineData("https://evil.example/kastwey/omnimud/releases")]
    [InlineData("https://github.com.evil.example/kastwey/omnimud/releases")]
    [InlineData("https://evilgithub.com/kastwey/omnimud/releases")]
    [InlineData("https://github.com@evil.example/kastwey/omnimud/releases")]
    [InlineData("https://user:pass@github.com/kastwey/omnimud/releases")]
    [InlineData("https://github.com:8443/kastwey/omnimud/releases")]
    [InlineData("https://github.com/someoneelse/omnimud/releases")]
    [InlineData("https://github.com/kastwey/omnimud/../../evil/malware/releases")]
    [InlineData("https://github.com/kastwey/omnimud-evil/releases")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-settings:privacy")]
    [InlineData("\\\\evil\\share\\setup.exe")]
    [InlineData("C:\\Windows\\System32\\calc.exe")]
    [InlineData("not a url")]
    [InlineData("")]
    public async Task MaliciousOrForeignPage_IsNeverOffered_ButTheUpdateIsStillAnnounced(string url)
    {
        var (result, _) = await CheckAsync(HttpStatusCode.OK, Release("v2.1.0", url: url));

        result.Should().BeOfType<UpdateAvailable>().Which.ReleasePage.Should().BeNull();
        GitHubReleasePage.Trusted(url).Should().BeNull();
    }

    [Theory]
    [InlineData("https://github.com/kastwey/omnimud/releases/tag/v2.1.0")]
    [InlineData("https://GitHub.com/Kastwey/Omnimud/releases/latest")]
    [InlineData("https://github.com/kastwey/omnimud/releases")]
    public void PagesOfTheProject_AreTrusted(string url) => GitHubReleasePage.Trusted(url).Should().NotBeNull();

    [Fact]
    public void TheDefaultDestinations_AreTrusted() => GitHubReleasePage.IsTrusted(GitHubReleasePage.AllReleases).Should().BeTrue();

    [Fact]
    public void NullAndRelativeUris_AreNotTrusted()
    {
        GitHubReleasePage.IsTrusted(null).Should().BeFalse();
        GitHubReleasePage.IsTrusted(new Uri("/kastwey/omnimud/releases", UriKind.Relative)).Should().BeFalse();
    }

    // ── Failures ───────────────────────────────────────────────────────────

    [Fact]
    public async Task NotFound_MeansNoReleasesYet_WhichIsUpToDate()
    {
        var (result, _) = await CheckAsync(HttpStatusCode.NotFound, """{"message":"Not Found"}""");
        result.Should().Be(new UpToDate(Current, NoReleasesYet: true));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task ApiLimit_IsRateLimited(HttpStatusCode status)
    {
        var (result, _) = await CheckAsync(status, """{"message":"API rate limit exceeded"}""");
        result.Should().BeOfType<UpdateCheckFailed>().Which.Reason.Should().Be(UpdateCheckFailure.RateLimited);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.MovedPermanently)]
    public async Task OtherStatus_IsUnexpectedResponse_WithTheCode(HttpStatusCode status)
    {
        var (result, _) = await CheckAsync(status, "oops");
        var failed = result.Should().BeOfType<UpdateCheckFailed>().Subject;
        failed.Reason.Should().Be(UpdateCheckFailure.UnexpectedResponse);
        failed.Detail.Should().Contain(((int)status).ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("<html>captive portal</html>")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"tag_name":42}""")]
    [InlineData("""{"tag_name":"latest"}""")]
    [InlineData("""{"tag_name":""}""")]
    public async Task BrokenOrStrangeJson_IsUnexpectedResponse(string body)
    {
        var (result, _) = await CheckAsync(HttpStatusCode.OK, body);
        result.Should().BeOfType<UpdateCheckFailed>().Which.Reason.Should().Be(UpdateCheckFailure.UnexpectedResponse);
    }

    [Fact]
    public async Task DeeplyNestedJson_IsUnexpectedResponse_NotACrash()
    {
        var body = string.Concat(Enumerable.Repeat("{\"a\":", 200)) + "1" + new string('}', 200);
        var (result, _) = await CheckAsync(HttpStatusCode.OK, body);
        result.Should().BeOfType<UpdateCheckFailed>().Which.Reason.Should().Be(UpdateCheckFailure.UnexpectedResponse);
    }

    [Fact]
    public async Task HugeAnswer_IsNotRead()
    {
        var settings = new GitHubUpdateCheckerSettings { MaxResponseBytes = 1000 };
        var (result, _) = await CheckAsync(new FakeHandler(HttpStatusCode.OK, Release("v2.1.0", body: new string('x', 5000))), settings: settings);
        result.Should().BeOfType<UpdateCheckFailed>().Which.Reason.Should().Be(UpdateCheckFailure.UnexpectedResponse);
    }

    [Fact]
    public async Task NoNetwork_IsANetworkFailure()
    {
        var handler = new FakeHandler((_, _) => throw new HttpRequestException("No such host is known."));
        var (result, _) = await CheckAsync(handler);
        result.Should().BeOfType<UpdateCheckFailed>().Which.Reason.Should().Be(UpdateCheckFailure.Network);
    }

    [Fact]
    public async Task ConnectionCutWhileReading_IsANetworkFailure()
    {
        var handler = new FakeHandler((_, _) => throw new IOException("The connection was reset."));
        var (result, _) = await CheckAsync(handler);
        result.Should().BeOfType<UpdateCheckFailed>().Which.Reason.Should().Be(UpdateCheckFailure.Network);
    }

    [Fact]
    public async Task ServerThatNeverAnswers_TimesOut_Quickly()
    {
        var handler = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return FakeHandler.Response(HttpStatusCode.OK, "{}");
        });
        var settings = new GitHubUpdateCheckerSettings { Timeout = TimeSpan.FromMilliseconds(150) };

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var (result, _) = await CheckAsync(handler, settings: settings);

        result.Should().BeOfType<UpdateCheckFailed>().Which.Reason.Should().Be(UpdateCheckFailure.Timeout);
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void DefaultTimeout_IsShort() =>
        GitHubUpdateCheckerSettings.Default.Timeout.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(15));

    [Fact]
    public async Task CancellationByTheCaller_IsNotSwallowed()
    {
        var handler = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return FakeHandler.Response(HttpStatusCode.OK, "{}");
        });
        using var client = new HttpClient(handler);
        using var cts = new CancellationTokenSource(100);

        var act = () => new GitHubUpdateChecker(client, Current).CheckAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── What travels in the request ────────────────────────────────────────

    [Fact]
    public async Task Request_IsOneAnonymousHttpsGet_ToThePublicApi_WithTheUserAgent()
    {
        var (_, handler) = await CheckAsync(HttpStatusCode.OK, Release("v2.1.0"));

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.RequestUri.Should().Be(new Uri("https://api.github.com/repos/kastwey/omnimud/releases/latest"));
        request.RequestUri!.Query.Should().BeEmpty();
        request.Content.Should().BeNull();
        request.Headers.UserAgent.ToString().Should().Be("Omnimud/2.0.0");
        request.Headers.Accept.ToString().Should().Contain("application/vnd.github+json");
    }

    [Fact]
    public async Task Request_CarriesNothingAboutTheUser()
    {
        var (_, handler) = await CheckAsync(HttpStatusCode.OK, Release("v2.1.0"));

        var request = handler.Requests.Single();
        request.Headers.Select(h => h.Key).Should().BeEquivalentTo("User-Agent", "Accept", "X-GitHub-Api-Version");
        request.Headers.Authorization.Should().BeNull();
        request.Headers.Contains("Cookie").Should().BeFalse();

        var everything = request.RequestUri + " " + string.Join(" ", request.Headers.SelectMany(h => h.Value));
        foreach (var personal in new[] { Environment.UserName, Environment.MachineName, Environment.UserDomainName })
        {
            if (personal.Length >= 3)
                everything.Should().NotContainEquivalentOf(personal);
        }
    }

    [Fact]
    public async Task PreReleaseVersionOfTheClient_StillGivesAValidUserAgent()
    {
        var current = SemanticVersion.Parse("2.1.0-beta.1")!;
        var (result, handler) = await CheckAsync(new FakeHandler(HttpStatusCode.OK, Release("v2.1.0")), current);

        handler.Requests.Single().Headers.UserAgent.ToString().Should().Be("Omnimud/2.1.0-beta.1");
        result.Should().BeOfType<UpdateAvailable>("the final version is newer than its beta");
    }

    [Fact]
    public void PlainHttpEndpoint_IsRefused()
    {
        using var client = new HttpClient(new FakeHandler(HttpStatusCode.OK));
        var settings = new GitHubUpdateCheckerSettings { LatestReleaseEndpoint = new Uri("http://api.github.com/repos/kastwey/omnimud/releases/latest") };

        var act = () => new GitHubUpdateChecker(client, Current, settings);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task NothingIsDownloaded_OnlyTheOneRequest()
    {
        var (result, handler) = await CheckAsync(HttpStatusCode.OK, Release("v2.1.0"));

        result.Should().BeOfType<UpdateAvailable>();
        handler.Requests.Should().ContainSingle("the assets of the release are never fetched");
    }

    // ── Proxy ──────────────────────────────────────────────────────────────

    private sealed class FixedOptions(OmnimudOptions options) : IOptionsService
    {
        public int Resolved { get; private set; }
        public event EventHandler<OptionsChangedEventArgs>? Changed { add { } remove { } }

        public Task<OmnimudOptions> ResolveAsync(int? mudId, int? characterId, CancellationToken ct = default)
        {
            Resolved++;
            (mudId, characterId).Should().Be(((int?)null, (int?)null), "the update check is application-wide: only the global options count");
            return Task.FromResult(options);
        }

        public Task<bool> HasOwnOptionsAsync(OptionScope scope, int? scopeId, CancellationToken ct = default) => Task.FromResult(true);
        public Task SaveAsync(OptionScope scope, int? scopeId, OmnimudOptions options, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ResetToInheritedAsync(OptionScope scope, int? scopeId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static async Task<(UpdateCheckResult Result, DownloadProxySettings Proxy, FakeHandler Handler)> CheckThroughAsync(OmnimudOptions options, IProxyCredentialStore? credentials = null)
    {
        var handler = new FakeHandler(HttpStatusCode.OK, Release("v2.1.0"));
        DownloadProxySettings? seen = null;
        var resolver = new ProxySettingsResolver(credentials ?? NullProxyCredentialStore.Instance, NoSystemProxyResolver.Instance);
        var checker = new ProxyAwareUpdateChecker(new FixedOptions(options), resolver, Current, proxy =>
        {
            seen = proxy;
            return new HttpClient(handler, disposeHandler: false);
        });

        var result = await checker.CheckAsync();
        return (result, seen!, handler);
    }

    [Fact]
    public async Task WithoutProxy_TheRequestGoesDirect()
    {
        var (result, proxy, handler) = await CheckThroughAsync(new OmnimudOptions());

        result.Should().BeOfType<UpdateAvailable>();
        proxy.Should().Be(DownloadProxySettings.Direct);
        handler.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(ProxyProtocol.HttpConnect, "http://proxy.example.org:3128/")]
    [InlineData(ProxyProtocol.Socks5, "socks5://proxy.example.org:3128/")]
    public async Task ManualProxy_IsUsed(ProxyProtocol protocol, string expected)
    {
        var options = new OmnimudOptions { ProxyType = ProxyMode.Manual, ProxyHost = "proxy.example.org", ProxyPort = 3128, ProxyProtocol = protocol };

        var (_, proxy, _) = await CheckThroughAsync(options);

        proxy.Kind.Should().Be(DownloadProxyKind.Manual);
        proxy.Address.Should().Be(new Uri(expected));
    }

    [Fact]
    public async Task AutomaticProxy_UsesTheSystemOne()
    {
        var (_, proxy, _) = await CheckThroughAsync(new OmnimudOptions { ProxyType = ProxyMode.Automatic });
        proxy.Kind.Should().Be(DownloadProxyKind.System);
    }

    [Fact]
    public async Task ProxyCredentials_GoToTheProxy_NeverToGitHub()
    {
        var store = new ProxyCredentialStore(new AesPasswordProtector(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
        var options = store.WithPassword(new OmnimudOptions
        {
            ProxyType = ProxyMode.Manual, ProxyHost = "proxy.example.org", ProxyPort = 3128, ProxyProtocol = ProxyProtocol.HttpConnect, ProxyUsername = "ana"
        }, "s3cret");

        var (_, proxy, handler) = await CheckThroughAsync(options, store);

        (proxy.Username, proxy.Password).Should().Be(("ana", "s3cret"));
        var request = handler.Requests.Single();
        request.Headers.Authorization.Should().BeNull();
        string.Join(" ", request.Headers.SelectMany(h => h.Value)).Should().NotContain("ana").And.NotContain("s3cret");
    }

    [Fact]
    public async Task EveryCheck_ReadsTheOptionsAgain_SoAProxyChangeIsHonoured()
    {
        var options = new FixedOptions(new OmnimudOptions());
        var handler = new FakeHandler(HttpStatusCode.OK, Release("v2.0.0"));
        var checker = new ProxyAwareUpdateChecker(options, ProxySettingsResolver.Basic, Current, _ => new HttpClient(handler, disposeHandler: false));

        await checker.CheckAsync();
        await checker.CheckAsync();

        options.Resolved.Should().Be(2);
    }

    [Fact]
    public void TheDefaultOption_IsNotToCheckOnStartup()
    {
        OmnimudOptions.Default.CheckUpdatesOnStartup.Should().BeFalse();
        new OmnimudOptions().CheckUpdatesOnStartup.Should().BeFalse();
        OptionsSerializer.Deserialize(null).CheckUpdatesOnStartup.Should().BeFalse();
        OptionsSerializer.Deserialize([new("CheckUpdatesOnStartup", "garbage")]).CheckUpdatesOnStartup.Should().BeFalse();
    }
}
