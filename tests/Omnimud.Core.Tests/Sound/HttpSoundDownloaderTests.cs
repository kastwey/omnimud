using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Omnimud.Core.Sound;

namespace Omnimud.Core.Tests.Sound;

public sealed class HttpSoundDownloaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"omnimud_dl_{Guid.NewGuid():N}");
    private readonly StubHandler _handler = new();
    private readonly HttpSoundDownloader _sut;

    public HttpSoundDownloaderTests()
    {
        _sut = new HttpSoundDownloader(new HttpClient(_handler), maxFileSizeBytes: 1000);
    }

    public void Dispose()
    {
        _sut.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Target(string name) => Path.Combine(_dir, "sub", name);

    [Fact]
    public async Task DownloadAsync_HttpUrl_ThrowsSecurityError()
    {
        var act = () => _sut.DownloadAsync("http://insecure.com/file.wav", Target("test.wav"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*HTTPS*");
        _handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task DownloadAsync_HttpUrlWithAllowHttp_Downloads()
    {
        await _sut.DownloadAsync("http://old.example/file.wav", Target("file.wav"), new SoundDownloadOptions { AllowHttp = true });

        File.ReadAllBytes(Target("file.wav")).Should().Equal(StubHandler.DefaultBody);
    }

    [Theory]
    [InlineData("ftp://example.com/file.wav")]
    [InlineData("file:///c:/file.wav")]
    public async Task DownloadAsync_OtherSchemes_AreRejectedEvenWithAllowHttp(string url)
    {
        var act = () => _sut.DownloadAsync(url, Target("file.wav"), new SoundDownloadOptions { AllowHttp = true });

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DownloadAsync_InvalidExtension_ThrowsError()
    {
        var act = () => _sut.DownloadAsync("https://example.com/evil.exe", Target("evil.exe"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*extension*");
        _handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task DownloadAsync_InvalidUrl_ThrowsError()
    {
        var act = () => _sut.DownloadAsync("not-a-url", Target("test.wav"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(".wav")]
    [InlineData(".mp3")]
    [InlineData(".ogg")]
    [InlineData(".mid")]
    [InlineData(".flac")]
    public async Task AllowedExtensions_AreAccepted(string ext)
    {
        await _sut.DownloadAsync($"https://example.com/file{ext}", Target($"test{ext}"));

        File.Exists(Target($"test{ext}")).Should().BeTrue();
    }

    [Fact]
    public async Task DownloadAsync_Success_CreatesFoldersAndLeavesNoTemporaryFile()
    {
        await _sut.DownloadAsync("https://example.com/a.wav", Target("a.wav"));

        Directory.GetFiles(Path.GetDirectoryName(Target("a.wav"))!).Should().Equal(Target("a.wav"));
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("TEXT/HTML")]
    [InlineData("text/plain")]
    public async Task DownloadAsync_TextResponse_IsRejected(string contentType)
    {
        _handler.ContentType = contentType;

        var act = () => _sut.DownloadAsync("https://example.com/a.wav", Target("a.wav"));

        await act.Should().ThrowAsync<InvalidOperationException>();
        File.Exists(Target("a.wav")).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadAsync_RefreshHeader_IsRejected()
    {
        _handler.ExtraHeader = ("Refresh", "0; url=https://example.com/404");

        var act = () => _sut.DownloadAsync("https://example.com/a.wav", Target("a.wav"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DownloadAsync_EmptyBody_IsRejected()
    {
        _handler.Body = [];

        var act = () => _sut.DownloadAsync("https://example.com/a.wav", Target("a.wav"));

        await act.Should().ThrowAsync<InvalidOperationException>();
        File.Exists(Target("a.wav")).Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DownloadAsync_LargerThanTheLimit_IsRejectedAndLeavesNothing(bool announcesLength)
    {
        _handler.Body = new byte[5000];
        _handler.HideLength = !announcesLength;

        var act = () => _sut.DownloadAsync("https://example.com/a.wav", Target("a.wav"));

        await act.Should().ThrowAsync<InvalidOperationException>();
        if (Directory.Exists(Path.GetDirectoryName(Target("a.wav"))))
            Directory.GetFiles(Path.GetDirectoryName(Target("a.wav"))!).Should().BeEmpty();
    }

    [Fact]
    public async Task DownloadAsync_NotFound_Throws()
    {
        _handler.Status = HttpStatusCode.NotFound;

        var act = () => _sut.DownloadAsync("https://example.com/a.wav", Target("a.wav"));

        await act.Should().ThrowAsync<HttpRequestException>();
        File.Exists(Target("a.wav")).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadAsync_WithProxy_UsesAClientBuiltForThatProxy_AndReusesIt()
    {
        var proxyHandler = new StubHandler();
        var created = new List<Uri>();
        using var sut = new HttpSoundDownloader(new HttpClient(_handler), proxy =>
        {
            created.Add(proxy);
            return new HttpClient(proxyHandler);
        });
        var options = new SoundDownloadOptions { Proxy = new Uri("http://proxy.local:3128") };

        await sut.DownloadAsync("https://example.com/a.wav", Target("a.wav"), options);
        await sut.DownloadAsync("https://example.com/b.wav", Target("b.wav"), options);

        created.Should().Equal(new Uri("http://proxy.local:3128"));
        proxyHandler.Requests.Should().HaveCount(2);
        _handler.Requests.Should().BeEmpty();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public static readonly byte[] DefaultBody = [82, 73, 70, 70, 1, 2, 3];

        public List<Uri> Requests { get; } = [];
        public byte[] Body { get; set; } = DefaultBody;
        public string ContentType { get; set; } = "audio/wav";
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public (string Name, string Value)? ExtraHeader { get; set; }
        public bool HideLength { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);

            HttpContent content = HideLength ? new StreamContent(new NoLengthStream(Body)) : new ByteArrayContent(Body);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(ContentType);
            var response = new HttpResponseMessage(Status) { Content = content };
            if (ExtraHeader is { } header)
                response.Headers.TryAddWithoutValidation(header.Name, header.Value);
            return Task.FromResult(response);
        }
    }

    /// <summary>Non-seekable stream, so that HttpContent cannot work out a Content-Length.</summary>
    private sealed class NoLengthStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
}
