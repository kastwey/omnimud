using System.Globalization;
using NSubstitute;
using Omnimud.Core.Options;
using Omnimud.Core.Reports;
using Omnimud.Core.Session;
using Omnimud.Core.Sound;
using Omnimud.Core.Updates;
using Omnimud.Data.Exchange;
using Omnimud.Data.Options;
using Omnimud.Data.Repositories;
using Omnimud.UI.Forms;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;
using Omnimud.UI.Services.Accessibility;
using Omnimud.UI.Tests.Accessibility;
using Omnimud.UI.Tests.Presenters;

namespace Omnimud.UI.Tests.Forms;

/// <summary>Tools and Help menus of the launcher and Help menu of the game window: updates, reports, personal information, manual.</summary>
public sealed class Phase7MenusTests : IDisposable
{
    private static readonly Uri Page = new("https://github.com/kastwey/omnimud/releases/tag/v2.1.0");

    private readonly TempDatabase _db = new();
    private readonly ScriptedPrompts _prompts = new();
    private readonly FakeAppDialogs _app = new();
    private readonly RecordingLauncher _launcher = new();
    private readonly ScriptedUpdateNotice _notice = new(openPage: true);
    private readonly SqliteOptionRepository _optionRepository;
    private readonly OptionsService _options;

    public Phase7MenusTests()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("es");
        _optionRepository = new SqliteOptionRepository(_db.Factory);
        _options = new OptionsService(_optionRepository);
    }

    public void Dispose() => _db.Dispose();

    private FrmLauncher CreateLauncher(IUpdateChecker? checker)
    {
        var form = new FrmLauncher(_db.Muds, _db.Characters, new FakeProtector(), _db.Rules, _db.Exchange, _optionRepository, _prompts,
            () => Substitute.For<IGameWindowFactory>(), new ScriptedLauncherDialogs(), new ScriptedConflicts(ImportDecision.Skip),
            _app, checker, _options, _launcher, _notice);
        UiPump.Wait(form.LoadAsync());
        return form;
    }

    private static ToolStripMenuItem Item(Form form, string name) =>
        (ToolStripMenuItem)form.MainMenuStrip!.Items.Find(name, searchAllChildren: true).Single();

    private static IEnumerable<ToolStripMenuItem> AllItems(ToolStripItemCollection items) =>
        items.OfType<ToolStripMenuItem>().SelectMany(i => new[] { i }.Concat(AllItems(i.DropDownItems)));

    private static ToolStripMenuItem ByText(Form form, string text) => AllItems(form.MainMenuStrip!.Items).Single(i => i.Text == text);

    // ── Launcher ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void Launcher_WithTheNewEntries_PassesTheAudit(string culture) => Sta.Run(() =>
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);
        using var form = CreateLauncher(new ScriptedUpdateChecker(new UpToDate(new SemanticVersion(2))));

        AccessibilityAudit.Check(form, isDialog: false).Should().BeEmpty();
    });

    [Fact]
    public void Launcher_ToolsAndHelp_HaveTheNewEntries() => Sta.Run(() =>
    {
        using var form = CreateLauncher(new ScriptedUpdateChecker(new UpToDate(new SemanticVersion(2))));

        Item(form, "_miCheckUpdates").Text.Should().Be("&Comprobar actualizaciones ahora");
        Item(form, "_miCheckOnStartup").Text.Should().Be("Comprobar actualizaciones al i&niciar");
        Item(form, "_miPersonalInfo").Text.Should().Be("In&formación personal...");
        Item(form, "_miSuggestion").Text.Should().Be("Enviar &sugerencia...");
        Item(form, "_miReportError").Text.Should().Be("&Informar de un error...");
    });

    [Fact]
    public void Launcher_BuiltWithoutAChecker_SimplyHasNoUpdateEntries() => Sta.Run(() =>
    {
        using var form = CreateLauncher(checker: null);

        form.MainMenuStrip!.Items.Find("_miCheckUpdates", true).Should().BeEmpty();
        form.Updates.Should().BeNull();
        var act = () => UiPump.Wait(form.CheckUpdatesOnStartupAsync());
        act.Should().NotThrow();
    });

    [Fact]
    public void Launcher_ByDefault_DoesNotCheckOnStartup_AndTheBoxIsUnticked() => Sta.Run(() =>
    {
        var checker = new ScriptedUpdateChecker(new UpdateAvailable(new SemanticVersion(9), "9", "", Page));
        using var form = CreateLauncher(checker);

        UiPump.Wait(form.CheckUpdatesOnStartupAsync());
        form.RefreshCheckOnStartup();

        checker.Calls.Should().Be(0);
        _notice.Shown.Should().BeEmpty();
        Item(form, "_miCheckOnStartup").Checked.Should().BeFalse();
    });

    [Fact]
    public void Launcher_TheBox_WritesTheGlobalOption_AndThenTheStartupCheckRuns() => Sta.Run(() =>
    {
        var checker = new ScriptedUpdateChecker(new UpdateAvailable(new SemanticVersion(9), "Omnimud 9", "notas", Page));
        using var form = CreateLauncher(checker);

        Item(form, "_miCheckOnStartup").PerformClick();
        UiPump.Wait(WaitUntilAsync(() => _options.ResolveAsync(null, null).GetAwaiter().GetResult().CheckUpdatesOnStartup));

        Item(form, "_miCheckOnStartup").Checked.Should().BeTrue();
        checker.Calls.Should().Be(0, "ticking the box does not check by itself");

        UiPump.Wait(form.CheckUpdatesOnStartupAsync());

        checker.Calls.Should().Be(1);
        _notice.Shown.Should().ContainSingle().Which.Title.Should().Be("Omnimud 9");
        _launcher.Opened.Should().ContainSingle().Which.Should().Be(Page);
    });

    [Fact]
    public void Launcher_TheBox_FollowsWhatTheGlobalOptionsDialogStored() => Sta.Run(() =>
    {
        using var form = CreateLauncher(new ScriptedUpdateChecker(new UpToDate(new SemanticVersion(2))));
        _options.SaveAsync(OptionScope.Global, null, new OmnimudOptions { CheckUpdatesOnStartup = true }).GetAwaiter().GetResult();

        form.RefreshCheckOnStartup();

        Item(form, "_miCheckOnStartup").Checked.Should().BeTrue();
    });

    [Fact]
    public void Launcher_StartupCheck_ThatFails_SaysNothing() => Sta.Run(() =>
    {
        _options.SaveAsync(OptionScope.Global, null, new OmnimudOptions { CheckUpdatesOnStartup = true }).GetAwaiter().GetResult();
        using var form = CreateLauncher(new ScriptedUpdateChecker(new UpdateCheckFailed(UpdateCheckFailure.Network, "sin red")));

        UiPump.Wait(form.CheckUpdatesOnStartupAsync());

        _prompts.Infos.Concat(_prompts.Warnings).Concat(_prompts.Errors).Should().BeEmpty();
        _notice.Shown.Should().BeEmpty();
    });

    [Fact]
    public void Launcher_CheckNow_AlwaysTellsTheResult() => Sta.Run(() =>
    {
        using var form = CreateLauncher(new ScriptedUpdateChecker(new UpdateCheckFailed(UpdateCheckFailure.RateLimited, "HTTP 403")));

        UiPump.Wait(form.Updates!.CheckNowAsync());

        _prompts.Warnings.Should().ContainSingle().Which.Should().Contain("HTTP 403");
    });

    [Fact]
    public void Launcher_HelpAndToolsEntries_OpenTheRightThing() => Sta.Run(() =>
    {
        using var form = CreateLauncher(checker: null);

        Item(form, "_miSuggestion").PerformClick();
        Item(form, "_miReportError").PerformClick();
        Item(form, "_miPersonalInfo").PerformClick();
        ByText(form, Strings.Menu_HelpManual).PerformClick();
        ByText(form, Strings.Menu_HelpLua).PerformClick();

        _app.Reports.Should().Equal((ReportKind.Suggestion, null), (ReportKind.Error, null));
        _app.PersonalInfo.Should().Be(1);
        (_app.FakeHelp.Manual, _app.FakeHelp.Lua).Should().Be((1, 1));
        ByText(form, Strings.Menu_HelpManual).ShortcutKeys.Should().Be(Keys.F1);
    });

    [Fact]
    public void Launcher_About_ShowsTheRealVersion() => Sta.Run(() =>
    {
        using var form = CreateLauncher(checker: null);

        ByText(form, Strings.Menu_HelpAbout).PerformClick();

        _prompts.Infos.Should().ContainSingle().Which.Should().Contain(AppInfo.Version);
    });

    // ── Game window ────────────────────────────────────────────────────────

    private FrmGame CreateGame()
    {
        var session = Substitute.For<IMudSession>();
        session.Profile.Returns(new SessionProfile { Title = "Reinos", Host = "mud.example.org", Port = 23, MudId = 1, MudName = "Reinos" });
        session.Options.Returns(OmnimudOptions.Default with { ConfirmBeforeExit = false });
        session.State.Returns(SessionState.Connected);
        session.Messages.Returns(new List<SessionMessage>());
        session.History.Returns(new List<string>());
        session.ActionMenu.Returns(Omnimud.Core.Actions.ActionMenu.Empty);
        return new FrmGame(session, Substitute.For<ISessionSound>(), Substitute.For<ISessionDialogs>(), TimeProvider.System, Substitute.For<IAnnouncer>(), _app);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void Game_WithTheNewHelpEntries_PassesTheAudit(string culture) => Sta.Run(() =>
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);
        using var form = CreateGame();

        AccessibilityAudit.Check(form, isDialog: false).Should().BeEmpty();
    });

    [Fact]
    public void Game_HelpMenu_OpensManualReferenceAndReports() => Sta.Run(() =>
    {
        using var form = CreateGame();

        ByText(form, Strings.Menu_HelpManual).PerformClick();
        ByText(form, Strings.Menu_HelpLua).PerformClick();
        ByText(form, Strings.Menu_HelpSuggestion).PerformClick();
        ByText(form, Strings.Menu_HelpReportError).PerformClick();

        (_app.FakeHelp.Manual, _app.FakeHelp.Lua).Should().Be((1, 1));
        _app.Reports.Should().Equal((ReportKind.Suggestion, null), (ReportKind.Error, null));
        ByText(form, Strings.Menu_HelpManual).ShortcutKeys.Should().Be(Keys.F1);
    });

    [Fact]
    public void Game_TheReportEntries_NeverPassSessionData()
    {
        // The interface itself is the guarantee: a report can be given an exception and nothing else.
        typeof(IAppDialogs).GetMethod(nameof(IAppDialogs.ShowReport))!.GetParameters().Select(p => p.ParameterType)
            .Should().Equal(typeof(IWin32Window), typeof(ReportKind), typeof(Exception));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var limit = Environment.TickCount64 + 10_000;
        while (!condition())
        {
            if (Environment.TickCount64 > limit) throw new TimeoutException();
            await Task.Delay(10);
        }
    }
}
