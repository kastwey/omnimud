using FluentAssertions;
using NSubstitute;
using Omnimud.Core.Sound;
using Omnimud.Core.Text;

namespace Omnimud.Core.Tests.Sound;

public sealed class SessionSoundTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"omnimud_ss_{Guid.NewGuid():N}");
    private readonly string _mudDir;
    private readonly string _appDir;
    private readonly FakeSoundPlayer _player = new();
    private readonly FakeSoundDownloader _downloader = new();
    private readonly SessionSound _sut;

    public SessionSoundTests()
    {
        _mudDir = Path.Combine(_root, "mud");
        _appDir = Path.Combine(_root, "app");
        Directory.CreateDirectory(_mudDir);
        Directory.CreateDirectory(_appDir);
        _sut = new SessionSound(_player, _downloader);
        _sut.Configure(Settings());
    }

    public void Dispose()
    {
        _sut.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private SoundSettings Settings() => new() { MudSoundDirectory = _mudDir, AppSoundDirectory = _appDir };

    private string MudFile(string relative) => Touch(Path.Combine(_mudDir, relative));
    private string AppFile(string relative) => Touch(Path.Combine(_appDir, relative));

    private static string Touch(string path)
    {
        FakeSoundDownloader.WriteFile(path);
        return path;
    }

    private static SoundCommand Sound(string name, int priority = 50, int volume = 100, int loop = 1,
        bool cont = false, string? category = null, string? url = null) =>
        new() { Type = SoundType.Sound, FileName = name, Priority = priority, Volume = volume, Loop = loop, Continue = cont, SoundCategory = category, Url = url };

    private static SoundCommand Music(string name, int priority = 50, bool cont = false, int loop = 1) =>
        new() { Type = SoundType.Music, FileName = name, Priority = priority, Continue = cont, Loop = loop };

    private static SoundCommand Off(SoundType type) => new() { Type = type, FileName = string.Empty, IsStop = true };

    // ---- Path resolution ---------------------------------------------------------------------

    [Fact]
    public async Task HandleMspAsync_ExistingFile_PlaysItFromMudDirectory()
    {
        var file = MudFile("thunder.wav");

        await _sut.HandleMspAsync(Sound("thunder.wav"));

        _player.Played.Should().ContainSingle();
        _player.LastRequest.FilePath.Should().Be(file);
        _player.LastRequest.Type.Should().Be(SoundType.Sound);
    }

    [Fact]
    public async Task HandleMspAsync_Category_IsASubfolder()
    {
        var file = MudFile(@"weather\rain.ogg");

        await _sut.HandleMspAsync(Sound("rain.ogg", category: "weather"));

        _player.LastRequest.FilePath.Should().Be(file);
    }

    [Fact]
    public async Task HandleMspAsync_NameWithForwardSlashSubfolder_IsAllowed()
    {
        var file = MudFile(@"combat\hit.wav");

        await _sut.HandleMspAsync(Sound("combat/hit.wav"));

        _player.LastRequest.FilePath.Should().Be(file);
    }

    [Fact]
    public async Task HandleMspAsync_NoExtension_AssumesWav()
    {
        var file = MudFile("bell.wav");

        await _sut.HandleMspAsync(Sound("bell"));

        _player.LastRequest.FilePath.Should().Be(file);
    }

    [Theory]
    [InlineData("loud.MP3")]
    [InlineData("loud.Ogg")]
    [InlineData("loud.WAV")]
    public async Task HandleMspAsync_ExtensionInAnyCase_IsAccepted(string name)
    {
        MudFile(name);

        await _sut.HandleMspAsync(Sound(name));

        _player.Played.Should().ContainSingle();
    }

    [Fact]
    public async Task HandleMspAsync_ExtensionNotPlayable_IsNotPlayed()
    {
        MudFile("virus.exe");

        await _sut.HandleMspAsync(Sound("virus.exe"));

        _player.Played.Should().BeEmpty();
    }

    [Theory]
    [InlineData(@"..\secret.wav")]
    [InlineData("../secret.wav")]
    [InlineData(@"sub\..\..\secret.wav")]
    [InlineData(@"\secret.wav")]
    [InlineData("/secret.wav")]
    public async Task HandleMspAsync_PathTraversal_IsBlocked(string name)
    {
        Touch(Path.Combine(_root, "secret.wav"));
        Directory.CreateDirectory(Path.Combine(_mudDir, "sub"));

        await _sut.HandleMspAsync(Sound(name, url: "https://example.com/s"));

        _player.Played.Should().BeEmpty();
        _downloader.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleMspAsync_AbsolutePathDriveAndUnc_AreBlocked()
    {
        var outside = Touch(Path.Combine(_root, "outside.wav"));

        await _sut.HandleMspAsync(Sound(outside));
        await _sut.HandleMspAsync(Sound("C:outside.wav"));
        await _sut.HandleMspAsync(Sound(@"\\server\share\outside.wav"));
        await _sut.HandleMspAsync(Sound("outside.wav", category: outside));
        await _sut.HandleMspAsync(Sound("outside.wav", category: ".."));

        _player.Played.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleMspAsync_BeforeConfigure_DoesNothing()
    {
        using var sut = new SessionSound(_player, _downloader);
        MudFile("a.wav");

        await sut.HandleMspAsync(Sound("a.wav"));

        _player.Played.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleMspAsync_EmptyMudDirectory_DoesNothing()
    {
        _sut.Configure(Settings() with { MudSoundDirectory = "" });

        await _sut.HandleMspAsync(Sound("a.wav", url: "https://example.com"));

        _player.Played.Should().BeEmpty();
        _downloader.Calls.Should().BeEmpty();
    }

    // ---- Wildcards ---------------------------------------------------------------------------

    [Theory]
    [InlineData(0, "growl1.wav")]
    [InlineData(1, "growl2.wav")]
    [InlineData(2, "growl3.wav")]
    public async Task HandleMspAsync_Wildcard_PicksOneOfTheMatchesAtRandom(int random, string expected)
    {
        MudFile("growl1.wav");
        MudFile("growl2.wav");
        MudFile("growl3.wav");
        MudFile("other.wav");
        MudFile("growl4.txt");
        using var sut = new SessionSound(_player, _downloader, new FixedRandom(random));
        sut.Configure(Settings());

        await sut.HandleMspAsync(Sound("growl*"));

        Path.GetFileName(_player.LastRequest.FilePath).Should().Be(expected);
    }

    [Fact]
    public async Task HandleMspAsync_QuestionMarkAndAnyExtension_Match()
    {
        MudFile("step1.mp3");
        MudFile("step22.mp3");

        await _sut.HandleMspAsync(Sound("step?.*"));

        Path.GetFileName(_player.LastRequest.FilePath).Should().Be("step1.mp3");
    }

    [Fact]
    public async Task HandleMspAsync_WildcardWithoutMatches_NeitherPlaysNorDownloads()
    {
        await _sut.HandleMspAsync(Sound("none*", url: "https://example.com"));
        await _sut.WhenDownloadsCompleteAsync();

        _player.Played.Should().BeEmpty();
        _downloader.Calls.Should().BeEmpty();
    }

    // ---- off ---------------------------------------------------------------------------------

    [Fact]
    public async Task HandleMspAsync_Off_StopsOnlyThatType()
    {
        MudFile("a.wav");
        MudFile("m.mp3");
        MudFile("t.wav");
        await _sut.HandleMspAsync(Sound("a.wav"));
        await _sut.HandleMspAsync(Music("m.mp3"));
        _sut.PlayTriggerSound("t.wav");

        await _sut.HandleMspAsync(Off(SoundType.Sound));
        _player.LiveFiles.Should().Equal("m.mp3", "t.wav");

        await _sut.HandleMspAsync(Off(SoundType.Music));
        _player.LiveFiles.Should().Equal("t.wav");
    }

    // ---- Switches and inactive window --------------------------------------------------------

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, true)]
    public async Task HandleMspAsync_Sound_RespectsSwitchAndWindow(bool enabled, bool active, bool background, bool plays)
    {
        MudFile("a.wav");
        _sut.Configure(Settings() with { EnableSounds = enabled, PlaySoundsInBackground = background, EnableMusic = true });
        _sut.IsWindowActive = active;

        await _sut.HandleMspAsync(Sound("a.wav"));

        _player.Played.Should().HaveCount(plays ? 1 : 0);
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, true)]
    [InlineData(true, false, false, false)]
    public async Task HandleMspAsync_Music_RespectsItsOwnSwitchAndWindow(bool enabled, bool active, bool background, bool plays)
    {
        MudFile("m.mp3");
        _sut.Configure(Settings() with { EnableMusic = enabled, PlayMusicInBackground = background, EnableSounds = false });
        _sut.IsWindowActive = active;

        await _sut.HandleMspAsync(Music("m.mp3"));

        _player.Played.Should().HaveCount(plays ? 1 : 0);
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, true)]
    [InlineData(true, false, false, false)]
    public void PlayTriggerSound_FollowsTheSoundSwitches(bool enabled, bool active, bool background, bool plays)
    {
        MudFile("t.wav");
        _sut.Configure(Settings() with { EnableSounds = enabled, PlaySoundsInBackground = background, EnableMusic = false });
        _sut.IsWindowActive = active;

        _sut.PlayTriggerSound("t.wav").Should().BeTrue("the file exists, whether it sounds or not");

        _player.Played.Should().HaveCount(plays ? 1 : 0);
    }

    [Fact]
    public async Task HandleMspAsync_DisabledSound_DoesNotDownloadEither()
    {
        _sut.Configure(Settings() with { EnableSounds = false });

        await _sut.HandleMspAsync(Sound("a.wav", url: "https://example.com"));
        await _sut.WhenDownloadsCompleteAsync();

        _downloader.Calls.Should().BeEmpty();
    }

    // ---- Priorities --------------------------------------------------------------------------

    [Fact]
    public async Task Priorities_HigherAlive_BlocksLower()
    {
        MudFile("high.wav");
        MudFile("low.wav");
        await _sut.HandleMspAsync(Sound("high.wav", priority: 80));

        await _sut.HandleMspAsync(Sound("low.wav", priority: 20));

        _player.LiveFiles.Should().Equal("high.wav");
    }

    [Fact]
    public async Task Priorities_NewHigher_StopsLower()
    {
        MudFile("high.wav");
        MudFile("low.wav");
        await _sut.HandleMspAsync(Sound("low.wav", priority: 20));
        var low = _player.LastHandle;

        await _sut.HandleMspAsync(Sound("high.wav", priority: 80));

        _player.Stopped.Should().Equal(low);
        _player.LiveFiles.Should().Equal("high.wav");
    }

    [Fact]
    public async Task Priorities_Equal_AreMixed()
    {
        MudFile("a.wav");
        MudFile("b.wav");

        await _sut.HandleMspAsync(Sound("a.wav"));
        await _sut.HandleMspAsync(Sound("b.wav"));

        _player.LiveFiles.Should().Equal("a.wav", "b.wav");
        _player.Stopped.Should().BeEmpty();
    }

    [Fact]
    public async Task Priorities_AfterTheHigherOneEnds_ALowerOnePlaysAgain()
    {
        MudFile("high.wav");
        MudFile("low.wav");
        await _sut.HandleMspAsync(Sound("high.wav", priority: 80));
        await _sut.HandleMspAsync(Sound("low.wav", priority: 20));
        _player.LiveFiles.Should().Equal("high.wav");

        _player.Finish(_player.LastHandle);
        await _sut.HandleMspAsync(Sound("low.wav", priority: 20));

        _player.LiveFiles.Should().Equal("low.wav");
    }

    [Fact]
    public async Task Priorities_AfterOff_ALowerOnePlaysAgain()
    {
        MudFile("high.wav");
        MudFile("low.wav");
        await _sut.HandleMspAsync(Sound("high.wav", priority: 80));
        await _sut.HandleMspAsync(Off(SoundType.Sound));

        await _sut.HandleMspAsync(Sound("low.wav", priority: 20));

        _player.LiveFiles.Should().Equal("low.wav");
    }

    [Fact]
    public async Task Priorities_TablesAreSeparate_SoundMusicAndTriggerDoNotCompete()
    {
        MudFile("s.wav");
        MudFile("m.mp3");
        MudFile("t.wav");
        await _sut.HandleMspAsync(Music("m.mp3", priority: 90));
        await _sut.HandleMspAsync(Sound("s.wav", priority: 10));

        _sut.PlayTriggerSound("t.wav", priority: 50);

        _player.LiveFiles.Should().Equal("m.mp3", "s.wav", "t.wav");
        _player.Stopped.Should().BeEmpty();
    }

    [Fact]
    public void Priorities_TriggerTable_HasItsOwnRules()
    {
        MudFile("t1.wav");
        MudFile("t2.wav");
        MudFile("t3.wav");
        _sut.PlayTriggerSound("t1.wav", priority: 50);

        _sut.PlayTriggerSound("t2.wav", priority: 10);
        _player.LiveFiles.Should().Equal("t1.wav");

        _sut.PlayTriggerSound("t3.wav", priority: 70);
        _player.LiveFiles.Should().Equal("t3.wav");
    }

    [Fact]
    public async Task Priorities_PlayerCouldNotPlay_DoesNotBlockLaterSounds()
    {
        MudFile("high.wav");
        MudFile("low.wav");
        _player.FailPlay = true;
        await _sut.HandleMspAsync(Sound("high.wav", priority: 80));
        _player.FailPlay = false;

        await _sut.HandleMspAsync(Sound("low.wav", priority: 20));

        _player.LiveFiles.Should().Equal("low.wav");
    }

    [Fact]
    public async Task HandleMspAsync_SameSoundAgain_Restarts()
    {
        MudFile("a.wav");
        await _sut.HandleMspAsync(Sound("a.wav"));
        var first = _player.LastHandle;

        await _sut.HandleMspAsync(Sound("a.wav"));

        _player.Stopped.Should().Equal(first);
        _player.Played.Should().HaveCount(2);
        _player.LiveFiles.Should().Equal("a.wav");
    }

    // ---- Music and continue ------------------------------------------------------------------

    [Fact]
    public async Task Music_NewMusic_ReplacesThePreviousOne()
    {
        MudFile("one.mp3");
        MudFile("two.mp3");
        await _sut.HandleMspAsync(Music("one.mp3"));

        await _sut.HandleMspAsync(Music("two.mp3"));

        _player.LiveFiles.Should().Equal("two.mp3");
    }

    [Fact]
    public async Task Music_ContinueWithSameFile_DoesNotRestart()
    {
        MudFile("one.mp3");
        await _sut.HandleMspAsync(Music("one.mp3", loop: -1));

        await _sut.HandleMspAsync(Music("one.mp3", cont: true, loop: -1));

        _player.Played.Should().ContainSingle();
        _player.Stopped.Should().BeEmpty();
    }

    [Fact]
    public async Task Music_NoContinueWithSameFile_Restarts()
    {
        MudFile("one.mp3");
        await _sut.HandleMspAsync(Music("one.mp3"));

        await _sut.HandleMspAsync(Music("one.mp3", cont: false));

        _player.Played.Should().HaveCount(2);
        _player.LiveFiles.Should().Equal("one.mp3");
    }

    [Fact]
    public async Task Music_ContinueWithDifferentFile_Replaces()
    {
        MudFile("one.mp3");
        MudFile("two.mp3");
        await _sut.HandleMspAsync(Music("one.mp3"));

        await _sut.HandleMspAsync(Music("two.mp3", cont: true));

        _player.LiveFiles.Should().Equal("two.mp3");
    }

    [Fact]
    public async Task Music_ContinueAfterTheMusicEnded_PlaysAgain()
    {
        MudFile("one.mp3");
        await _sut.HandleMspAsync(Music("one.mp3"));
        _player.Finish(_player.LastHandle);

        await _sut.HandleMspAsync(Music("one.mp3", cont: true));

        _player.Played.Should().HaveCount(2);
    }

    // ---- Volume and loops --------------------------------------------------------------------

    [Theory]
    [InlineData(100, 100, 100)]
    [InlineData(50, 100, 50)]
    [InlineData(80, 50, 40)]
    [InlineData(100, 0, 0)]
    [InlineData(0, 100, 0)]
    public async Task Volume_IsCommandVolumeTimesMasterVolume(int commandVolume, int master, int expected)
    {
        MudFile("a.wav");
        _sut.Configure(Settings() with { Volume = master });

        await _sut.HandleMspAsync(Sound("a.wav", volume: commandVolume));

        _player.LastRequest.Volume.Should().Be(expected);
        _player.MasterVolume.Should().Be(100, "the player is shared: each session applies its own master volume");
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(-1, -1)]
    [InlineData(0, 1)]
    [InlineData(-7, -1)]
    public async Task Loop_IsPassedToThePlayer(int loop, int expected)
    {
        MudFile("a.wav");

        await _sut.HandleMspAsync(Sound("a.wav", loop: loop));

        _player.LastRequest.Loop.Should().Be(expected);
    }

    [Fact]
    public void PlayTriggerSound_PassesLoopVolumeAndPriority()
    {
        MudFile("t.wav");
        _sut.Configure(Settings() with { Volume = 50 });

        _sut.PlayTriggerSound("t.wav", loop: -1, volume: 60, priority: 70);

        _player.LastRequest.Should().BeEquivalentTo(new { Loop = -1, Volume = 30, Priority = 70, Type = SoundType.Trigger });
    }

    // ---- Downloads ---------------------------------------------------------------------------

    [Fact]
    public async Task Download_MissingFile_DownloadsThenPlaysWithTheOriginalParameters()
    {
        await _sut.HandleMspAsync(Sound("new", priority: 70, volume: 40, loop: 3, category: "fx", url: "https://mud.example/sounds/"));
        await _sut.WhenDownloadsCompleteAsync();

        var target = Path.Combine(_mudDir, "fx", "new.wav");
        _downloader.Calls.Should().ContainSingle();
        _downloader.Calls[0].Url.Should().Be("https://mud.example/sounds/fx/new.wav");
        _downloader.Calls[0].LocalPath.Should().Be(target);
        _player.LastRequest.Should().BeEquivalentTo(new { FilePath = target, Priority = 70, Volume = 40, Loop = 3 });
    }

    [Fact]
    public async Task Download_UrlAlreadyPointsToTheFile_IsUsedAsIs()
    {
        await _sut.HandleMspAsync(Sound("rain.ogg", url: "https://mud.example/snd/rain.ogg"));
        await _sut.WhenDownloadsCompleteAsync();

        _downloader.Calls.Single().Url.Should().Be("https://mud.example/snd/rain.ogg");
    }

    [Fact]
    public async Task Download_HttpUrl_TriesHttpsOnly_WhenHttpIsNotAllowed()
    {
        _downloader.Behaviour = (_, _) => throw new HttpRequestException("no TLS here");

        await _sut.HandleMspAsync(Sound("a.wav", url: "http://old.example/s"));
        await _sut.WhenDownloadsCompleteAsync();

        _downloader.Calls.Select(c => c.Url).Should().Equal("https://old.example/s/a.wav");
        _downloader.Calls[0].Options.AllowHttp.Should().BeFalse();
        _player.Played.Should().BeEmpty();
    }

    [Fact]
    public async Task Download_HttpUrl_FallsBackToHttp_WhenAllowed()
    {
        _sut.Configure(Settings() with { AllowHttpDownloads = true });
        _downloader.Behaviour = (url, path) =>
        {
            if (url.StartsWith("https:", StringComparison.Ordinal))
                throw new HttpRequestException("no TLS here");
            FakeSoundDownloader.WriteFile(path);
            return Task.CompletedTask;
        };

        await _sut.HandleMspAsync(Sound("a.wav", url: "http://old.example:8080/s"));
        await _sut.WhenDownloadsCompleteAsync();

        _downloader.Calls.Select(c => c.Url).Should().Equal("https://old.example:8080/s/a.wav", "http://old.example:8080/s/a.wav");
        _downloader.Calls[1].Options.AllowHttp.Should().BeTrue();
        _player.Played.Should().ContainSingle();
    }

    [Fact]
    public async Task Download_HttpsWorks_HttpIsNeverTried()
    {
        _sut.Configure(Settings() with { AllowHttpDownloads = true });

        await _sut.HandleMspAsync(Sound("a.wav", url: "http://old.example/s"));
        await _sut.WhenDownloadsCompleteAsync();

        _downloader.Calls.Select(c => c.Url).Should().Equal("https://old.example/s/a.wav");
    }

    [Theory]
    [InlineData("ftp://old.example/s")]
    [InlineData("file:///c:/windows")]
    [InlineData("not a url")]
    public async Task Download_OtherSchemes_AreIgnored(string url)
    {
        _sut.Configure(Settings() with { AllowHttpDownloads = true });

        await _sut.HandleMspAsync(Sound("a.wav", url: url));
        await _sut.WhenDownloadsCompleteAsync();

        _downloader.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Download_ProxyFromSettings_ReachesTheDownloader()
    {
        var proxy = new Uri("http://proxy.local:3128");
        _sut.Configure(Settings() with { DownloadProxy = proxy });

        await _sut.HandleMspAsync(Sound("a.wav", url: "https://mud.example"));
        await _sut.WhenDownloadsCompleteAsync();

        _downloader.Calls.Single().Options.Proxy.Should().Be(proxy);
    }

    [Fact]
    public async Task Download_OptionOff_DoesNotDownload()
    {
        _sut.Configure(Settings() with { DownloadSounds = false });

        await _sut.HandleMspAsync(Sound("a.wav", url: "https://mud.example"));
        await _sut.WhenDownloadsCompleteAsync();

        _downloader.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Download_NoUrl_DoesNotDownload()
    {
        await _sut.HandleMspAsync(Sound("a.wav"));
        await _sut.WhenDownloadsCompleteAsync();

        _downloader.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Download_DefaultUrlAnnouncedWithOff_IsUsedWhenTheCommandHasNone()
    {
        await _sut.HandleMspAsync(new SoundCommand { Type = SoundType.Sound, FileName = "", IsStop = true, Url = "https://mud.example/base" });

        await _sut.HandleMspAsync(Sound("a.wav"));
        await _sut.WhenDownloadsCompleteAsync();

        _downloader.Calls.Single().Url.Should().Be("https://mud.example/base/a.wav");
    }

    [Fact]
    public async Task Download_SameFileRequestedWhileDownloading_IsDownloadedOnce()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _downloader.Behaviour = async (_, path) =>
        {
            await gate.Task;
            FakeSoundDownloader.WriteFile(path);
        };

        for (var i = 0; i < 5; i++)
            await _sut.HandleMspAsync(Sound("a.wav", url: "https://mud.example"));
        var handled = _sut.HandleMspAsync(Sound("A.WAV", url: "https://mud.example"));
        handled.IsCompleted.Should().BeTrue("the receive loop must not wait for downloads");

        gate.SetResult();
        await _sut.WhenDownloadsCompleteAsync();

        _downloader.Calls.Should().ContainSingle();
        _player.Played.Should().ContainSingle();
    }

    [Fact]
    public async Task Download_TwoSessionsSameFolder_ShareOneDownloadAndBothPlay()
    {
        using var other = new SessionSound(_player, _downloader);
        other.Configure(Settings());
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _downloader.Behaviour = async (_, path) =>
        {
            started.TrySetResult();
            await gate.Task;
            FakeSoundDownloader.WriteFile(path);
        };

        await _sut.HandleMspAsync(Sound("a.wav", url: "https://mud.example"));
        await started.Task;
        await other.HandleMspAsync(Sound("a.wav", url: "https://mud.example"));
        gate.SetResult();
        await _sut.WhenDownloadsCompleteAsync();
        await other.WhenDownloadsCompleteAsync();

        _downloader.Calls.Should().ContainSingle();
        _player.Played.Should().HaveCount(2);
    }

    [Fact]
    public async Task Download_Rejected_PlaysNothingAndCanBeRetriedLater()
    {
        _downloader.Behaviour = (_, _) => throw new InvalidOperationException("text/html");
        await _sut.HandleMspAsync(Sound("a.wav", url: "https://mud.example"));
        await _sut.WhenDownloadsCompleteAsync();
        _player.Played.Should().BeEmpty();

        _downloader.Behaviour = null;
        await _sut.HandleMspAsync(Sound("a.wav", url: "https://mud.example"));
        await _sut.WhenDownloadsCompleteAsync();

        _downloader.Calls.Should().HaveCount(2);
        _player.Played.Should().ContainSingle();
    }

    [Fact]
    public async Task Download_EmptyFile_IsDeletedAndNotPlayed()
    {
        _downloader.Behaviour = (_, path) =>
        {
            File.WriteAllBytes(path, []);
            return Task.CompletedTask;
        };

        await _sut.HandleMspAsync(Sound("a.wav", url: "https://mud.example"));
        await _sut.WhenDownloadsCompleteAsync();

        _player.Played.Should().BeEmpty();
        File.Exists(Path.Combine(_mudDir, "a.wav")).Should().BeFalse();
    }

    [Fact]
    public async Task Download_SoundsDisabledMeanwhile_DoesNotPlayAfterDownloading()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _downloader.Behaviour = async (_, path) =>
        {
            await gate.Task;
            FakeSoundDownloader.WriteFile(path);
        };
        await _sut.HandleMspAsync(Sound("a.wav", url: "https://mud.example"));

        _sut.Configure(Settings() with { EnableSounds = false });
        gate.SetResult();
        await _sut.WhenDownloadsCompleteAsync();

        _player.Played.Should().BeEmpty();
    }

    [Fact]
    public async Task Download_StopAllWhileDownloading_DoesNotPlayLater()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _downloader.Behaviour = async (_, path) =>
        {
            started.TrySetResult();
            await gate.Task;
            FakeSoundDownloader.WriteFile(path);
        };
        await _sut.HandleMspAsync(Sound("a.wav", url: "https://mud.example"));
        await started.Task;

        _sut.StopAll();
        gate.SetResult();
        await _sut.WhenDownloadsCompleteAsync();

        _player.Played.Should().BeEmpty();
    }

    // ---- Trigger sounds ----------------------------------------------------------------------

    [Fact]
    public void PlayTriggerSound_FullPath_IsUsedAsGiven()
    {
        var file = Touch(Path.Combine(_root, "elsewhere", "alarm.mp3"));

        _sut.PlayTriggerSound(file).Should().BeTrue();

        _player.LastRequest.FilePath.Should().Be(file);
        _player.LastRequest.Type.Should().Be(SoundType.Trigger);
    }

    [Fact]
    public void PlayTriggerSound_FullPathWithoutExtension_AssumesWav()
    {
        var file = Touch(Path.Combine(_root, "elsewhere", "alarm.wav"));

        _sut.PlayTriggerSound(Path.Combine(_root, "elsewhere", "alarm")).Should().BeTrue();

        _player.LastRequest.FilePath.Should().Be(file);
    }

    [Fact]
    public void PlayTriggerSound_RelativeName_LooksInMudFolderFirst()
    {
        var inMud = MudFile("ding.wav");
        AppFile("ding.wav");

        _sut.PlayTriggerSound("ding").Should().BeTrue();

        _player.LastRequest.FilePath.Should().Be(inMud);
    }

    [Fact]
    public void PlayTriggerSound_NotInMudFolder_FallsBackToAppFolder()
    {
        var inApp = AppFile("ding.ogg");

        _sut.PlayTriggerSound("ding.ogg").Should().BeTrue();

        _player.LastRequest.FilePath.Should().Be(inApp);
    }

    [Fact]
    public void PlayTriggerSound_NotFoundAnywhere_ReturnsFalse()
    {
        _sut.PlayTriggerSound("ghost.wav").Should().BeFalse();
        _sut.PlayTriggerSound("").Should().BeFalse();
        _sut.PlayTriggerSound(Path.Combine(_root, "ghost.wav")).Should().BeFalse();

        _player.Played.Should().BeEmpty();
    }

    [Theory]
    [InlineData(@"..\secret.wav")]
    [InlineData(@"\\server\share\secret.wav")]
    [InlineData("//server/share/secret.wav")]
    public void PlayTriggerSound_TraversalOrNetworkPath_IsRejected(string name)
    {
        Touch(Path.Combine(_root, "secret.wav"));

        _sut.PlayTriggerSound(name).Should().BeFalse();

        _player.Played.Should().BeEmpty();
    }

    [Fact]
    public void PlayTriggerSound_NotAudioExtension_IsRejected()
    {
        var file = Touch(Path.Combine(_root, "notes.txt"));

        _sut.PlayTriggerSound(file).Should().BeFalse();
    }

    [Fact]
    public void StopTriggerSound_Playing_StopsItAndReturnsTrue()
    {
        MudFile("loop.wav");
        MudFile("keep.wav");
        _sut.PlayTriggerSound("loop", loop: -1);
        var handle = _player.LastHandle;
        _sut.PlayTriggerSound("keep.wav", loop: -1);

        _sut.StopTriggerSound("loop").Should().BeTrue();

        _player.Stopped.Should().Equal(handle);
        _player.LiveFiles.Should().Equal("keep.wav");
    }

    [Fact]
    public void StopTriggerSound_StartedWithAnotherSpelling_StillMatchesByFile()
    {
        var file = MudFile("loop.wav");
        _sut.PlayTriggerSound("loop", loop: -1);

        _sut.StopTriggerSound(file).Should().BeTrue();

        _player.LiveFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task StopTriggerSound_NotPlayingOrAlreadyEnded_ReturnsFalse()
    {
        MudFile("t.wav");
        MudFile("msp.wav");
        _sut.StopTriggerSound("t.wav").Should().BeFalse();

        _sut.PlayTriggerSound("t.wav");
        _player.Finish(_player.LastHandle);
        _sut.StopTriggerSound("t.wav").Should().BeFalse();

        await _sut.HandleMspAsync(Sound("msp.wav"));
        _sut.StopTriggerSound("msp.wav").Should().BeFalse("MSP sounds are not trigger sounds");
        _player.LiveFiles.Should().Equal("msp.wav");
    }

    // ---- UI sounds ---------------------------------------------------------------------------

    [Theory]
    [InlineData("click", "click.wav")]
    [InlineData("url", "url.mp3")]
    [InlineData("tick", "tick.ogg")]
    [InlineData("click.wav", "click.wav")]
    public void PlayUiSound_FindsTheFileInTheAppFolderWithAnyExtension(string name, string fileName)
    {
        var file = AppFile(fileName);
        MudFile(fileName);

        _sut.PlayUiSound(name);

        _player.LastRequest.FilePath.Should().Be(file);
    }

    [Fact]
    public void PlayUiSound_SoundsDisabled_IsSilent()
    {
        AppFile("click.wav");
        _sut.Configure(Settings() with { EnableSounds = false });

        _sut.PlayUiSound("click");

        _player.Played.Should().BeEmpty();
    }

    [Fact]
    public async Task PlayUiSound_IgnoresPrioritiesAndInactiveWindow()
    {
        AppFile("click.wav");
        MudFile("boss.wav");
        _sut.Configure(Settings() with { PlaySoundsInBackground = false });
        await _sut.HandleMspAsync(Sound("boss.wav", priority: 100));
        _sut.IsWindowActive = false;

        _sut.PlayUiSound("click");

        _player.LiveFiles.Should().Equal("boss.wav", "click.wav");
    }

    [Theory]
    [InlineData(@"..\click")]
    [InlineData("sub/click")]
    [InlineData("missing")]
    [InlineData("")]
    public void PlayUiSound_UnknownOrUnsafeName_DoesNothing(string name)
    {
        Touch(Path.Combine(_root, "click.wav"));
        AppFile(@"sub\click.wav");

        _sut.PlayUiSound(name);

        _player.Played.Should().BeEmpty();
    }

    // ---- Isolation, StopAll, Dispose, Configure ----------------------------------------------

    [Fact]
    public async Task TwoSessions_SharingThePlayer_DoNotTouchEachOther()
    {
        MudFile("a.wav");
        MudFile("low.wav");
        MudFile("m.mp3");
        using var other = new SessionSound(_player, _downloader);
        other.Configure(Settings());
        await other.HandleMspAsync(Sound("low.wav", priority: 10));
        await other.HandleMspAsync(Music("m.mp3"));

        await _sut.HandleMspAsync(Sound("a.wav", priority: 90));
        _player.LiveFiles.Should().Equal("a.wav", "low.wav", "m.mp3");

        // This session's priority 90 does not block the other session's priority 10
        await other.HandleMspAsync(Sound("low.wav", priority: 10));
        _player.Played.Should().HaveCount(4);

        await _sut.HandleMspAsync(Off(SoundType.Music));
        _sut.StopAll();
        _player.LiveFiles.Should().Equal("low.wav", "m.mp3");

        await other.HandleMspAsync(Sound("low.wav", priority: 10));
        _player.LiveFiles.Should().Equal("low.wav", "m.mp3");
    }

    [Fact]
    public async Task Dispose_StopsOnlyThisSession_AndIgnoresLaterCalls()
    {
        MudFile("a.wav");
        AppFile("click.wav");
        using var other = new SessionSound(_player, _downloader);
        other.Configure(Settings());
        await other.HandleMspAsync(Sound("a.wav"));
        var othersHandle = _player.LastHandle;
        await _sut.HandleMspAsync(Sound("a.wav"));
        _sut.PlayUiSound("click");

        _sut.Dispose();
        _sut.Dispose();

        _player.IsPlaying(othersHandle).Should().BeTrue();
        _player.Played.Should().HaveCount(3);
        _player.Stopped.Should().HaveCount(2);

        await _sut.HandleMspAsync(Sound("a.wav"));
        _sut.PlayTriggerSound("a.wav").Should().BeFalse();
        _sut.PlayUiSound("click");
        _sut.StopAll();
        _player.Played.Should().HaveCount(3);
    }

    [Fact]
    public async Task StopAll_StopsEveryCategoryOfThisSession()
    {
        MudFile("a.wav");
        MudFile("m.mp3");
        AppFile("click.wav");
        await _sut.HandleMspAsync(Sound("a.wav"));
        await _sut.HandleMspAsync(Music("m.mp3"));
        _sut.PlayTriggerSound("a.wav");
        _sut.PlayUiSound("click");

        _sut.StopAll();

        _player.LiveFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task Configure_Again_AppliesVolumeToWhatIsPlaying()
    {
        MudFile("a.wav");
        MudFile("m.mp3");
        await _sut.HandleMspAsync(Sound("a.wav", volume: 80));
        var sound = _player.LastHandle;
        await _sut.HandleMspAsync(Music("m.mp3"));
        var music = _player.LastHandle;

        _sut.Configure(Settings() with { Volume = 50 });

        _player.Volumes[sound].Should().Be(40);
        _player.Volumes[music].Should().Be(50);

        _sut.Configure(Settings() with { Volume = 100 });
        _player.Volumes[sound].Should().Be(80);
    }

    [Fact]
    public async Task Configure_Again_CutsWhatHasBeenDisabled()
    {
        MudFile("a.wav");
        MudFile("m.mp3");
        MudFile("t.wav");
        await _sut.HandleMspAsync(Sound("a.wav"));
        await _sut.HandleMspAsync(Music("m.mp3"));
        _sut.PlayTriggerSound("t.wav");

        _sut.Configure(Settings() with { EnableMusic = false });
        _player.LiveFiles.Should().Equal("a.wav", "t.wav");

        _sut.Configure(Settings() with { EnableSounds = false });
        _player.LiveFiles.Should().BeEmpty();

        _sut.Configure(Settings());
        await _sut.HandleMspAsync(Sound("a.wav"));
        _player.LiveFiles.Should().Equal("a.wav");
    }

    [Fact]
    public async Task HandleMspAsync_PlayerThatThrows_NeverReachesTheCaller()
    {
        MudFile("a.wav");
        var player = Substitute.For<ISoundPlayer>();
        player.Play(Arg.Any<SoundPlayRequest>()).Returns(_ => throw new InvalidOperationException("boom"));
        using var sut = new SessionSound(player, _downloader);
        sut.Configure(Settings());

        var act = async () =>
        {
            await sut.HandleMspAsync(Sound("a.wav"));
            sut.PlayTriggerSound("a.wav");
            sut.PlayUiSound("a");
            sut.StopAll();
        };

        await act.Should().NotThrowAsync();
    }
}
