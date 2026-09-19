using NSubstitute;
using System.Globalization;
using Omnimud.Core.Options;
using Omnimud.Core.Updates;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Tests.Presenters;

public sealed class UpdateCheckPresenterTests
{
    private static readonly SemanticVersion Current = new(2, 0, 0);
    private static readonly Uri Page = new("https://github.com/kastwey/omnimud/releases/tag/v2.1.0");

    private readonly FakeOptionsService _options = new();
    private readonly ScriptedPrompts _prompts = new();
    private readonly RecordingLauncher _launcher = new();

    public UpdateCheckPresenterTests() => Strings.Culture = CultureInfo.GetCultureInfo("es");

    private static UpdateAvailable Update(Uri? page) => new(new SemanticVersion(2, 1, 0), "Omnimud 2.1", "Notas", page);

    private (UpdateCheckPresenter Presenter, ScriptedUpdateChecker Checker, ScriptedUpdateNotice Notice) Create(
        UpdateCheckResult result, bool openPage = false)
    {
        var checker = new ScriptedUpdateChecker(result);
        var notice = new ScriptedUpdateNotice(openPage);
        return (new UpdateCheckPresenter(checker, _options, _prompts, notice, _launcher), checker, notice);
    }

    private void EnableOnStartup() => _options.With(OptionScope.Global, null, new OmnimudOptions { CheckUpdatesOnStartup = true });

    private void NothingWasSaid()
    {
        _prompts.Infos.Should().BeEmpty();
        _prompts.Warnings.Should().BeEmpty();
        _prompts.Errors.Should().BeEmpty();
        _prompts.Confirms.Should().BeEmpty();
    }

    // ── Startup ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ByDefault_NothingIsCheckedOnStartup_NotEvenARequest()
    {
        var (presenter, checker, notice) = Create(Update(Page));

        var result = await presenter.CheckOnStartupAsync();

        result.Should().BeNull();
        checker.Calls.Should().Be(0, "the option is off by default: Omnimud never goes to the network on its own");
        notice.Shown.Should().BeEmpty();
        NothingWasSaid();
    }

    [Fact]
    public async Task OnlyTheGlobalOptionCounts()
    {
        _options.With(OptionScope.Mud, 1, new OmnimudOptions { CheckUpdatesOnStartup = true });
        _options.With(OptionScope.Character, 1, new OmnimudOptions { CheckUpdatesOnStartup = true });
        var (presenter, checker, _) = Create(Update(Page));

        await presenter.CheckOnStartupAsync();

        checker.Calls.Should().Be(0);
    }

    [Fact]
    public async Task OnStartup_WhenEnabled_ANewVersionIsAnnounced()
    {
        EnableOnStartup();
        var (presenter, checker, notice) = Create(Update(Page));

        await presenter.CheckOnStartupAsync();

        checker.Calls.Should().Be(1);
        notice.Shown.Should().ContainSingle().Which.Version.ToString().Should().Be("2.1.0");
        _launcher.Opened.Should().BeEmpty("the user did not ask to open the page");
    }

    public static TheoryData<UpdateCheckResult> QuietResults() => new()
    {
        new UpToDate(Current),
        new UpToDate(Current, NoReleasesYet: true),
        new UpdateCheckFailed(UpdateCheckFailure.Network, "No such host"),
        new UpdateCheckFailed(UpdateCheckFailure.Timeout),
        new UpdateCheckFailed(UpdateCheckFailure.RateLimited, "HTTP 403"),
        new UpdateCheckFailed(UpdateCheckFailure.UnexpectedResponse, "HTTP 500"),
    };

    [Theory]
    [MemberData(nameof(QuietResults))]
    public async Task OnStartup_AnythingButANewVersion_IsSilent(UpdateCheckResult result)
    {
        EnableOnStartup();
        var (presenter, _, notice) = Create(result);

        (await presenter.CheckOnStartupAsync()).Should().Be(result);

        notice.Shown.Should().BeEmpty();
        NothingWasSaid();
    }

    [Fact]
    public async Task OnStartup_ACheckerThatThrows_IsSilentToo()
    {
        EnableOnStartup();
        var presenter = new UpdateCheckPresenter(new ScriptedUpdateChecker(() => throw new InvalidOperationException("boom")),
            _options, _prompts, new ScriptedUpdateNotice(false), _launcher);

        var result = await presenter.CheckOnStartupAsync();

        result.Should().BeOfType<UpdateCheckFailed>();
        NothingWasSaid();
    }

    // ── Manual ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Manual_WorksEvenWithTheStartupOptionOff()
    {
        var (presenter, checker, notice) = Create(Update(Page));

        await presenter.CheckNowAsync();

        checker.Calls.Should().Be(1);
        notice.Shown.Should().ContainSingle();
    }

    [Fact]
    public async Task Manual_UpToDate_IsSaid_WithTheVersion()
    {
        var (presenter, _, _) = Create(new UpToDate(Current));

        await presenter.CheckNowAsync();

        _prompts.Infos.Should().ContainSingle().Which.Should().Contain("2.0.0").And.Contain("última versión");
    }

    [Fact]
    public async Task Manual_NoReleasesYet_IsSaidAsSuch()
    {
        var (presenter, _, _) = Create(new UpToDate(Current, NoReleasesYet: true));

        await presenter.CheckNowAsync();

        _prompts.Infos.Should().ContainSingle().Which.Should().Contain("Todavía no se ha publicado");
        _prompts.Warnings.Should().BeEmpty();
    }

    [Theory]
    [InlineData(UpdateCheckFailure.Network, "conexión")]
    [InlineData(UpdateCheckFailure.Timeout, "a tiempo")]
    [InlineData(UpdateCheckFailure.RateLimited, "demasiadas peticiones")]
    [InlineData(UpdateCheckFailure.UnexpectedResponse, "respuesta inesperada")]
    public async Task Manual_EveryFailure_IsExplained(UpdateCheckFailure reason, string expected)
    {
        var (presenter, _, notice) = Create(new UpdateCheckFailed(reason, "HTTP 418"));

        await presenter.CheckNowAsync();

        _prompts.Warnings.Should().ContainSingle().Which.Should().Contain(expected).And.Contain("HTTP 418");
        notice.Shown.Should().BeEmpty();
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void EveryFailure_HasItsOwnText_InBothLanguages(string culture)
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);
        var texts = Enum.GetValues<UpdateCheckFailure>().Select(r => UpdateCheckPresenter.Describe(new UpdateCheckFailed(r))).ToList();

        texts.Should().OnlyHaveUniqueItems();
        texts.Should().AllSatisfy(t => t.Should().NotBeNullOrWhiteSpace());
    }

    // ── Opening the page ───────────────────────────────────────────────────

    [Fact]
    public async Task WhenTheUserAsks_TheReleasePageIsOpened_AndNothingElse()
    {
        var (presenter, _, _) = Create(Update(Page), openPage: true);

        await presenter.CheckNowAsync();

        _launcher.Opened.Should().ContainSingle().Which.Should().Be(Page);
    }

    [Fact]
    public async Task WithoutATrustedPage_NothingIsOpened_EvenIfTheNoticeSaysYes()
    {
        var (presenter, _, notice) = Create(Update(null), openPage: true);

        await presenter.CheckNowAsync();

        notice.Shown.Should().ContainSingle("the new version is still announced");
        _launcher.Opened.Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://evil.example/kastwey/omnimud/releases")]
    [InlineData("http://github.com/kastwey/omnimud/releases")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("https://github.com/someoneelse/omnimud/releases")]
    public async Task APageThatIsNotTheProjectAtGitHub_IsNeverOpened_WhateverTheCheckerSaid(string url)
    {
        // A checker other than the real one might let a bad link through: the presenter checks again.
        var (presenter, _, _) = Create(Update(new Uri(url)), openPage: true);

        await presenter.CheckNowAsync();

        _launcher.Opened.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenTheBrowserCannotBeOpened_TheUserIsToldTheAddress()
    {
        _launcher.Succeeds = false;
        var (presenter, _, _) = Create(Update(Page), openPage: true);

        await presenter.CheckNowAsync();

        _prompts.Warnings.Should().ContainSingle().Which.Should().Contain(Page.AbsoluteUri);
    }

    // ── The option ─────────────────────────────────────────────────────────

    [Fact]
    public async Task TheMenuBox_ReadsAndWritesTheGlobalOption_KeepingTheRestOfTheBlock()
    {
        _options.With(OptionScope.Global, null, new OmnimudOptions { Volume = 35, Language = "en" });
        var (presenter, _, _) = Create(new UpToDate(Current));

        (await presenter.GetCheckOnStartupAsync()).Should().BeFalse();
        await presenter.SetCheckOnStartupAsync(true);

        (await presenter.GetCheckOnStartupAsync()).Should().BeTrue();
        var saved = _options.Saved.Should().ContainSingle().Subject;
        saved.Scope.Should().Be(OptionScope.Global);
        saved.Options.Should().Be(new OmnimudOptions { Volume = 35, Language = "en", CheckUpdatesOnStartup = true });
    }

    [Fact]
    public async Task SettingTheSameValue_WritesNothing()
    {
        var (presenter, _, _) = Create(new UpToDate(Current));

        await presenter.SetCheckOnStartupAsync(false);

        _options.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task SwitchingTheOptionOn_DoesNotCheckByItself()
    {
        var (presenter, checker, _) = Create(Update(Page));

        await presenter.SetCheckOnStartupAsync(true);

        checker.Calls.Should().Be(0);
    }

    [Fact]
    public async Task AManualCheckWhileAnotherRuns_SaysSo_InsteadOfEndingInSilence()
    {
        var gate = new TaskCompletionSource<UpdateCheckResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = Substitute.For<IUpdateChecker>();
        slow.CheckAsync(Arg.Any<CancellationToken>()).Returns(gate.Task);
        var presenter = new UpdateCheckPresenter(slow, _options, _prompts, new ScriptedUpdateNotice(false), _launcher);

        var first = presenter.CheckNowAsync();
        await presenter.CheckNowAsync();
        _prompts.Infos.Should().ContainSingle().Which.Should().Contain("en marcha");

        gate.SetResult(new UpToDate(Current));
        await first;
        await slow.Received(1).CheckAsync(Arg.Any<CancellationToken>());
    }
}
