using FluentAssertions;
using Omnimud.Core.Sound;
using Omnimud.Core.Tests.Connection;
using Omnimud.Core.Text;

namespace Omnimud.Core.Tests.Sound;

/// <summary>
/// Sound downloads through a real HTTP proxy with user and password (<see cref="RealProxy"/>), from a local
/// HTTP server. The local server speaks plain HTTP, so the tests allow HTTP downloads; the last test goes through
/// <see cref="SessionSound"/>, which tries https first and only then falls back to http.
/// </summary>
public sealed class SoundDownloadRealProxyTests : IAsyncDisposable
{
    private const string User = "juan";
    private const string Password = "s3cret-Passw0rd!";
    private static readonly byte[] Wav = "RIFF....WAVEfmt fake sound bytes"u8.ToArray();

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"omnimud_dlproxy_{Guid.NewGuid():N}");
    private readonly TinyHttpServer _web = new(Wav);
    private readonly RealProxy _proxy = new(User, Password);
    private readonly HttpClient _direct = new();
    private readonly HttpSoundDownloader _sut;

    public SoundDownloadRealProxyTests()
    {
        Directory.CreateDirectory(_root);
        _sut = new HttpSoundDownloader(_direct);
    }

    public async ValueTask DisposeAsync()
    {
        _sut.Dispose();
        _direct.Dispose();
        await _proxy.DisposeAsync();
        await _web.DisposeAsync();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private string Url(string path = "s/thunder.wav") => $"http://127.0.0.1:{_web.Port}/{path}";

    private DownloadProxySettings Manual(string? user, string? password) =>
        new(DownloadProxyKind.Manual, new Uri($"http://127.0.0.1:{_proxy.HttpPort}"), user, password);

    private static SoundDownloadOptions Options(DownloadProxySettings proxy) => new() { AllowHttp = true, ProxySettings = proxy };

    [Fact]
    public async Task ManualProxy_WithCredentials_DownloadsThroughTheProxy()
    {
        var target = Path.Combine(_root, "thunder.wav");

        await _sut.DownloadAsync(Url(), target, Options(Manual(User, Password))).OrTimeout();

        File.ReadAllBytes(target).Should().Equal(Wav);
        // Twice: HttpClient asks without credentials, gets the 407 challenge and repeats the request with them.
        _proxy.Requests.Should().NotBeEmpty().And.OnlyContain(url => url == Url());
        _proxy.HttpLogins.Should().Contain(User);
        _web.RequestLines.Should().ContainSingle();
    }

    [Fact]
    public async Task ManualProxy_WrongPassword_FailsWithoutLeavingAFile_AndWithoutLeakingThePassword()
    {
        const string wrong = "wr0ng-and-very-recognisable";
        var target = Path.Combine(_root, "thunder.wav");

        var act = () => _sut.DownloadAsync(Url(), target, Options(Manual(User, wrong))).OrTimeout();

        var error = (await act.Should().ThrowAsync<HttpRequestException>()).Which;
        error.ToString().Should().NotContain(wrong);
        File.Exists(target).Should().BeFalse();
        Directory.GetFiles(_root).Should().BeEmpty();
        _web.RequestLines.Should().BeEmpty("the proxy never let the request through");
    }

    [Fact]
    public async Task ManualProxy_WithoutCredentials_WhenTheProxyDemandsThem_Fails()
    {
        var target = Path.Combine(_root, "thunder.wav");

        var act = () => _sut.DownloadAsync(Url(), target, Options(Manual(null, null))).OrTimeout();

        await act.Should().ThrowAsync<HttpRequestException>();
        _web.RequestLines.Should().BeEmpty();
    }

    [Fact]
    public async Task DirectSettings_DoNotTouchTheProxy()
    {
        var target = Path.Combine(_root, "thunder.wav");

        await _sut.DownloadAsync(Url(), target, Options(DownloadProxySettings.Direct)).OrTimeout();

        File.ReadAllBytes(target).Should().Equal(Wav);
        _proxy.Requests.Should().BeEmpty();
        _proxy.Connects.Should().BeEmpty();
    }

    [Fact]
    public void ProxySettings_ToString_HidesThePassword()
    {
        Manual(User, Password).ToString().Should().Contain(User).And.NotContain(Password);
    }

    [Fact]
    public async Task ThroughSessionSound_HttpsIsTriedFirst_ThenHttp_BothThroughTheProxy_AndTheSoundPlays()
    {
        var player = new FakeSoundPlayer();
        using var sound = new SessionSound(player, _sut);
        sound.Configure(new SoundSettings
        {
            MudSoundDirectory = Path.Combine(_root, "mud"),
            AppSoundDirectory = Path.Combine(_root, "app"),
            AllowHttpDownloads = true,
            DownloadProxySettings = Manual(User, Password)
        });

        await sound.HandleMspAsync(new SoundCommand { Type = SoundType.Sound, FileName = "thunder.wav", Url = $"http://127.0.0.1:{_web.Port}/s" });
        await sound.WhenDownloadsCompleteAsync().OrTimeout();

        File.ReadAllBytes(Path.Combine(_root, "mud", "thunder.wav")).Should().Equal(Wav);
        player.Played.Should().ContainSingle();
        _proxy.Connects.Should().NotBeEmpty("https was tried first, as a tunnel through the proxy")
            .And.OnlyContain(target => target == $"127.0.0.1:{_web.Port}");
        _proxy.Requests.Should().NotBeEmpty().And.OnlyContain(url => url == $"http://127.0.0.1:{_web.Port}/s/thunder.wav");
        _web.RequestLines.Should().Equal("GET /s/thunder.wav HTTP/1.1");
    }
}
