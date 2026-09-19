using System.Globalization;
using NSubstitute;
using Omnimud.Core.Reports;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;

namespace Omnimud.UI.Tests.Presenters;

public sealed class ReportPresenterTests
{
    private static readonly DiagnosticInfo Diagnostics = new("2.0.0", "Windows de prueba", ".NET de prueba", "X64", "es", "Nvda");

    private readonly ScriptedPrompts _prompts = new();
    private readonly RecordingLauncher _launcher = new();
    private readonly FakeClipboard _clipboard = new();
    private readonly MemoryPersonalInfoStore _personalInfo = new();

    public ReportPresenterTests()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("es");
        Omnimud.Core.Localization.SetCulture(CultureInfo.GetCultureInfo("es"));
    }

    private ReportPresenter Create(ReportKind kind = ReportKind.Error, Exception? exception = null, IClipboardService? clipboard = null,
        params string[] privateTerms) =>
        new(kind, exception is null ? null : ExceptionInfo.From(exception),
            new ReportBuilder(new ReportSanitizer("maria", @"C:\Users\maria", "EQUIPO", privateTerms)), () => Diagnostics,
            [new GitHubIssueReportSender(_launcher), new EmailReportSender(_launcher)], clipboard ?? _clipboard, _personalInfo, _prompts);

    private static string Decoded(Uri uri) => Uri.UnescapeDataString(uri.AbsoluteUri);

    // ── Preview ────────────────────────────────────────────────────────────

    [Fact]
    public void Preview_FollowsTheDescription_AndShowsExactlyWhatWillGo()
    {
        var presenter = Create();
        var raised = 0;
        presenter.PreviewChanged += () => raised++;

        presenter.Description = "Se cuelga al pulsar F5";

        raised.Should().Be(1);
        presenter.Title.Should().Be("[Error] Se cuelga al pulsar F5");
        presenter.Preview.Should().Contain("Se cuelga al pulsar F5").And.Contain("Windows de prueba").And.Contain("Nvda");

        presenter.OpenInGitHub().Close.Should().BeTrue();
        var sent = Decoded(_launcher.Opened.Single());
        sent.Should().Contain(presenter.Title).And.Contain(presenter.Preview.Trim());
    }

    [Fact]
    public void DiagnosticsBox_AddsAndRemovesTheDiagnosticData()
    {
        var presenter = Create();
        presenter.Description = "x";
        presenter.Preview.Should().Contain("Windows de prueba");

        presenter.IncludeDiagnostics = false;

        presenter.Preview.Should().NotContain("Windows de prueba").And.NotContain("Nvda").And.NotContain("X64");
    }

    [Fact]
    public void Kind_ChangesTitleOfTheReport_AndOfTheDialog()
    {
        var presenter = Create(ReportKind.Suggestion);
        presenter.Description = "Más sonidos";
        presenter.Title.Should().StartWith("[Sugerencia]");
        presenter.DialogTitle.Should().Be("Enviar una sugerencia");

        presenter.Kind = ReportKind.Error;

        presenter.Title.Should().StartWith("[Error]");
        presenter.DialogTitle.Should().Be("Informar de un error");
    }

    [Fact]
    public void WithAnException_ItIsAlwaysAnErrorReport_AndNeedsNoDescription()
    {
        var presenter = Create(ReportKind.Suggestion, new InvalidOperationException("algo falló"));

        presenter.Kind.Should().Be(ReportKind.Error);
        presenter.HasException.Should().BeTrue();
        presenter.Preview.Should().Contain("InvalidOperationException").And.Contain("algo falló");
        presenter.OpenInGitHub().Close.Should().BeTrue();
        _prompts.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void PrivateTerms_AndTheUserProfile_NeverReachThePreview()
    {
        var exception = new InvalidOperationException(@"No se pudo conectar con mud.ejemplo.org como Aldara; ver C:\Users\maria\omnimud\data\x.log");
        var presenter = Create(ReportKind.Error, exception, null, "mud.ejemplo.org", "Aldara");

        var everything = presenter.Title + presenter.Preview;
        everything.Should().NotContainEquivalentOf("mud.ejemplo.org").And.NotContainEquivalentOf("aldara").And.NotContainEquivalentOf("maria");
        everything.Should().Contain("%USERPROFILE%");
    }

    // ── Edits by hand ──────────────────────────────────────────────────────

    [Fact]
    public void WhatTheUserEdits_IsWhatGoesOut()
    {
        var presenter = Create();
        presenter.Description = "Texto original";
        presenter.Title = "Mi título";
        presenter.Preview = "Solo esto, sin nada más.";

        presenter.EditedByHand.Should().BeTrue();
        presenter.OpenInGitHub();

        var sent = Decoded(_launcher.Opened.Single());
        sent.Should().Contain("title=Mi título").And.Contain("body=Solo esto, sin nada más.");
        sent.Should().NotContain("Windows de prueba", "the user removed the diagnostic data from the preview");
    }

    [Fact]
    public void EditsByHand_AreNotOverwritten_WhenTheDescriptionChangesLater()
    {
        var presenter = Create();
        presenter.Description = "uno";
        presenter.Preview = "editado a mano";

        presenter.Description = "dos";

        presenter.Preview.Should().Be("editado a mano");
        presenter.PreviewIsStale.Should().BeTrue();
    }

    [Fact]
    public void StalePreview_TheUserChooses_Regenerate_AndNothingIsSentYet()
    {
        var presenter = Create();
        presenter.Description = "uno";
        presenter.Preview = "editado a mano";
        presenter.Description = "dos";
        _prompts.ConfirmAnswers.Enqueue(true);

        var result = presenter.OpenInGitHub();

        result.Should().Be(ReportActionResult.Stay(ReportField.Preview));
        presenter.Preview.Should().Contain("dos").And.NotContain("editado a mano");
        _launcher.Opened.Should().BeEmpty("the user must see the new preview before anything goes out");
    }

    [Fact]
    public void StalePreview_TheUserChooses_SendAsItIs()
    {
        var presenter = Create();
        presenter.Description = "uno";
        presenter.Preview = "editado a mano";
        presenter.Description = "dos";
        _prompts.ConfirmAnswers.Enqueue(false);

        presenter.OpenInGitHub().Close.Should().BeTrue();

        Decoded(_launcher.Opened.Single()).Should().Contain("editado a mano");
    }

    [Fact]
    public void LineEndingDifferences_AreNotEdits()
    {
        var presenter = Create();
        presenter.Description = "uno\r\ndos";
        presenter.Preview = presenter.Preview.ReplaceLineEndings("\n"); // what a text box may hand back

        presenter.EditedByHand.Should().BeFalse();
    }

    // ── Validation ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ReportKind.Error, "Describe el error")]
    [InlineData(ReportKind.Suggestion, "Escribe tu sugerencia")]
    public void WithoutDescription_NothingGoesOut_AndTheFocusGoesToTheDescription(ReportKind kind, string expected)
    {
        var presenter = Create(kind);

        presenter.OpenInGitHub().Should().Be(ReportActionResult.Stay(ReportField.Description));
        presenter.CopyToClipboard().Should().Be(ReportActionResult.Stay(ReportField.Description));

        _prompts.Warnings.Should().HaveCount(2).And.AllSatisfy(w => w.Should().Contain(expected));
        _launcher.Opened.Should().BeEmpty();
        _clipboard.Texts.Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutDescription_EmailDoesNotGoOutEither()
    {
        var presenter = Create();

        (await presenter.SendByEmailAsync()).Should().Be(ReportActionResult.Stay(ReportField.Description));

        _launcher.Opened.Should().BeEmpty();
    }

    [Fact]
    public void EmptyTitleOrPreview_AreRefused()
    {
        var presenter = Create();
        presenter.Description = "algo";
        presenter.Title = "  ";
        presenter.OpenInGitHub().Should().Be(ReportActionResult.Stay(ReportField.Title));

        presenter.Title = "t";
        presenter.Preview = "";
        presenter.OpenInGitHub().Should().Be(ReportActionResult.Stay(ReportField.Preview));

        _launcher.Opened.Should().BeEmpty();
    }

    // ── Channels ───────────────────────────────────────────────────────────

    [Fact]
    public void GitHub_OpensTheNewIssuePage_AndClosesTheDialog()
    {
        var presenter = Create();
        presenter.Description = "Falla";

        presenter.OpenInGitHub().Should().Be(ReportActionResult.Done);

        var uri = _launcher.Opened.Single();
        uri.AbsoluteUri.Should().StartWith("https://github.com/kastwey/omnimud/issues/new?title=");
        _clipboard.Texts.Should().BeEmpty("a short report needs no clipboard");
    }

    [Fact]
    public async Task GitHub_NeverCarriesThePersonalInformation()
    {
        await _personalInfo.SaveAsync(new PersonalInfo("María López", "maria@example.org"));
        var presenter = Create();
        presenter.Description = "Falla";

        presenter.OpenInGitHub();

        Decoded(_launcher.Opened.Single()).Should().NotContain("maria@example.org").And.NotContain("López");
        _prompts.Confirms.Should().BeEmpty("the signature is only offered for e-mail");
    }

    [Fact]
    public async Task Email_WithoutPersonalInformation_AsksNothing_AndCarriesNoAddressOfTheUser()
    {
        var presenter = Create();
        presenter.Description = "Falla";

        (await presenter.SendByEmailAsync()).Should().Be(ReportActionResult.Done);

        var uri = _launcher.Opened.Single();
        uri.Scheme.Should().Be("mailto");
        uri.AbsoluteUri.Count(c => c == '@').Should().Be(1, "only the author's address");
        _prompts.Confirms.Should().BeEmpty();
    }

    [Fact]
    public async Task Email_Signature_IsOnlyAddedWhenTheUserSaysYes_AndHeIsShownExactlyWhat()
    {
        await _personalInfo.SaveAsync(new PersonalInfo("María López", "maria@example.org"));
        var presenter = Create();
        presenter.Description = "Falla";

        _prompts.ConfirmAnswers.Enqueue(false);
        await presenter.SendByEmailAsync();
        _prompts.ConfirmAnswers.Enqueue(true);
        await presenter.SendByEmailAsync();

        _prompts.Confirms.Should().HaveCount(2).And.AllSatisfy(c => c.Should().Contain("María López <maria@example.org>"));
        Decoded(_launcher.Opened[0]).Should().NotContain("maria@example.org");
        Decoded(_launcher.Opened[1]).Should().EndWith("María López <maria@example.org>");
    }

    [Fact]
    public void LongReport_IsCopiedInFull_AndTheUserIsTold_BeforeTheBrowserOpens()
    {
        var events = new List<string>();
        var launcher = Substitute.For<IExternalLauncher>();
        launcher.Open(Arg.Any<Uri>()).Returns(_ => { events.Add("open"); return true; });
        var prompts = Substitute.For<IUserPrompts>();
        prompts.When(p => p.Info(Arg.Any<string>(), Arg.Any<string>())).Do(_ => events.Add("info"));
        var long_ = new ReportPresenter(ReportKind.Error, null, new ReportBuilder(new ReportSanitizer(null, null)), () => Diagnostics,
            [new GitHubIssueReportSender(launcher)], _clipboard, _personalInfo, prompts);
        long_.Description = "Falla";
        long_.Preview += Environment.NewLine + string.Join(Environment.NewLine, Enumerable.Range(0, 400).Select(i => $"   en Espacio.Tipo{i:D4}.Método() línea ñ"));

        long_.OpenInGitHub().Close.Should().BeTrue();

        events.Should().Equal("info", "open");
        _clipboard.Text.Should().Contain("Tipo0399", "the clipboard has the full report").And.StartWith(long_.Title);
        launcher.Received(1).Open(Arg.Is<Uri>(u => u.AbsoluteUri.Length <= GitHubIssueReportSender.MaxUrlLength));
    }

    [Fact]
    public void WhenTheBrowserCannotBeOpened_TheReportIsNotLost()
    {
        _launcher.Succeeds = false;
        var presenter = Create();
        presenter.Description = "Falla";

        presenter.OpenInGitHub().Close.Should().BeFalse();

        _clipboard.Text.Should().Contain("Falla");
        _prompts.Warnings.Should().ContainSingle().Which.Should().Contain("https://github.com/kastwey/omnimud/issues/new").And.Contain("portapapeles");
    }

    [Fact]
    public async Task WhenTheMailProgramCannotBeOpened_TheUserGetsTheAddress()
    {
        _launcher.Succeeds = false;
        var presenter = Create();
        presenter.Description = "Falla";

        (await presenter.SendByEmailAsync()).Close.Should().BeFalse();

        _prompts.Warnings.Should().ContainSingle().Which.Should().Contain(ReportDestinations.AuthorEmail);
    }

    [Fact]
    public void Copy_PutsTitleAndBodyOnTheClipboard_AndKeepsTheDialogOpen()
    {
        var presenter = Create();
        presenter.Description = "Falla";

        presenter.CopyToClipboard().Close.Should().BeFalse();

        _clipboard.Text.Should().StartWith("[Error] Falla").And.Contain("Windows de prueba");
        _prompts.Infos.Should().ContainSingle();
        _launcher.Opened.Should().BeEmpty();
    }

    [Fact]
    public void Copy_WhenTheClipboardFails_SaysSo()
    {
        var presenter = Create(clipboard: new FakeClipboard(works: false));
        presenter.Description = "Falla";

        presenter.CopyToClipboard();

        _prompts.Warnings.Should().ContainSingle().Which.Should().Contain("portapapeles");
    }

    [Fact]
    public void CreatingThePresenter_SendsNothing()
    {
        Create(ReportKind.Error, new InvalidOperationException("x"));

        _launcher.Opened.Should().BeEmpty();
        _clipboard.Texts.Should().BeEmpty();
    }
}
