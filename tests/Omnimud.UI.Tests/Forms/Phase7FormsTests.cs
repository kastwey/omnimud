using System.Globalization;
using NSubstitute;
using Omnimud.Core.Reports;
using Omnimud.Core.Session;
using Omnimud.Core.Updates;
using Omnimud.UI.Controls;
using Omnimud.UI.Forms;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;
using Omnimud.UI.Services.Accessibility;
using Omnimud.UI.Tests.Accessibility;
using Omnimud.UI.Tests.Presenters;

namespace Omnimud.UI.Tests.Forms;

/// <summary>The dialogs of phase 7: update notice, report, personal information and unexpected error. None is ever shown modally here.</summary>
public sealed class Phase7FormsTests
{
    private static readonly Uri Page = new("https://github.com/kastwey/omnimud/releases/tag/v2.1.0");
    private static readonly DiagnosticInfo Diagnostics = new("2.0.0", "Windows de prueba", ".NET de prueba", "X64", "es", "Nvda");

    private readonly ScriptedPrompts _prompts = new();
    private readonly RecordingLauncher _launcher = new();
    private readonly FakeClipboard _clipboard = new();
    private readonly MemoryPersonalInfoStore _personalInfo = new();

    public Phase7FormsTests()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("es");
        Omnimud.Core.Localization.SetCulture(CultureInfo.GetCultureInfo("es"));
    }

    private static void Culture(string name)
    {
        Strings.Culture = CultureInfo.GetCultureInfo(name);
        Omnimud.Core.Localization.SetCulture(CultureInfo.GetCultureInfo(name));
    }

    private static T Get<T>(Form form, string name) where T : Control => (T)form.Controls.Find(name, true).Single();

    /// <summary>What a click does. Button.PerformClick does nothing on a window that is not on screen, and these never are.</summary>
    private static void Click(Button button) =>
        typeof(Button).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(button, [EventArgs.Empty]);

    private static IEnumerable<Control> Descendants(Control root) =>
        root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));

    private static void NoDescriptions_NoRichTextBox(Form form)
    {
        foreach (var control in Descendants(form))
        {
            control.Should().NotBeAssignableTo<RichTextBox>("dialogs use TextBox (see docs/05_RONDA_DIALOGOS.md)");
            control.AccessibleDescription.Should().BeNullOrEmpty($"{control.Name}: NVDA reads a description on every focus");
        }
    }

    private static UpdateAvailable Update(Uri? page, string notes = "Notas de la versión") =>
        new(new SemanticVersion(2, 1, 0), "Omnimud 2.1", notes, page);

    private ReportPresenter Presenter(ReportKind kind = ReportKind.Error, Exception? exception = null) =>
        new(kind, exception is null ? null : ExceptionInfo.From(exception), new ReportBuilder(new ReportSanitizer("maria", @"C:\Users\maria")),
            () => Diagnostics, [new GitHubIssueReportSender(_launcher), new EmailReportSender(_launcher)], _clipboard, _personalInfo, _prompts);

    // ═══════════════════════════ FrmUpdateAvailable ═══════════════════════════

    [Theory]
    [InlineData("es", true)]
    [InlineData("en", true)]
    [InlineData("es", false)]
    [InlineData("en", false)]
    public void UpdateNotice_PassesTheAudit(string culture, bool withPage) => Sta.Run(() =>
    {
        Culture(culture);
        using var form = new FrmUpdateAvailable(Update(withPage ? Page : null), "2.0.0");

        AccessibilityAudit.Check(form, isDialog: true).Should().BeEmpty();
        NoDescriptions_NoRichTextBox(form);
    });

    [Fact]
    public void UpdateNotice_SaysTheVersionInTheTitle_AndPutsEverythingInTheNotesBox() => Sta.Run(() =>
    {
        using var form = new FrmUpdateAvailable(Update(Page, "Línea 1\nLínea 2"), "2.0.0");

        form.Text.Should().Be("Omnimud 2.1.0 disponible");
        var notes = Get<ProtectedTextBox>(form, "_txtNotes");
        notes.AccessibleName.Should().Be("Notas de la versión");
        notes.Text.Should().Contain("2.1.0").And.Contain("2.0.0").And.Contain("Línea 1" + Environment.NewLine + "Línea 2");
        notes.Text.Should().Contain("No se descarga ni se instala nada");
    });

    /// <summary>NVDA reads the whole value of a read-only edit box on focus: the notes box is a normal one that refuses edits,
    /// and the focus starts on a button.</summary>
    [Fact]
    public void UpdateNotice_NotesAreNotDeclaredReadOnly_AndTheFocusStartsOnTheButton() => Sta.Run(() =>
    {
        using var form = new FrmUpdateAvailable(Update(Page), "2.0.0");

        Get<ProtectedTextBox>(form, "_txtNotes").ReadOnly.Should().BeFalse();
        form.ActiveControl.Should().BeSameAs(Get<Button>(form, "_btnOpen"));
        form.AcceptButton.Should().BeSameAs(Get<Button>(form, "_btnOpen"));
        form.CancelButton.Should().BeSameAs(Get<Button>(form, "_btnClose"));
        Get<Button>(form, "_btnOpen").DialogResult.Should().Be(DialogResult.OK);
        Get<Button>(form, "_btnOpen").Text.Should().Be("&Abrir la página de descarga");
        Get<Button>(form, "_btnClose").Text.Should().Be("&Cerrar");
    });

    [Fact]
    public void UpdateNotice_WithoutATrustedPage_DoesNotOfferToOpenAnything() => Sta.Run(() =>
    {
        using var form = new FrmUpdateAvailable(Update(null), "2.0.0");

        form.OffersDownloadPage.Should().BeFalse();
        Get<Button>(form, "_btnOpen").Enabled.Should().BeFalse();
        form.AcceptButton.Should().BeSameAs(Get<Button>(form, "_btnClose"));
        form.ActiveControl.Should().BeSameAs(Get<Button>(form, "_btnClose"));
    });

    [Fact]
    public void UpdateNotice_WithoutNotes_SaysSo() => Sta.Run(() =>
    {
        using var form = new FrmUpdateAvailable(Update(Page, ""), "2.0.0");
        Get<ProtectedTextBox>(form, "_txtNotes").Text.Should().Contain("Esta versión no tiene notas.");
    });

    // ═══════════════════════════ ProtectedTextBox ═══════════════════════════

    [Fact]
    public void ProtectedTextBox_RefusesEveryEdit_ButKeepsSelectionAndCopy() => Sta.Run(() =>
    {
        const int wmChar = 0x0102, wmKeyDown = 0x0100, wmPaste = 0x0302, wmCut = 0x0300, wmClear = 0x0303, wmUndo = 0x0304, emReplaceSel = 0x00C2;
        using var form = new Form();
        var box = new ProtectedTextBox();
        form.Controls.Add(box);
        box.SetProtectedText("uno\ndos");
        _ = box.Handle;
        box.SelectAll();

        box.SimulateMessage(wmChar, 'x');
        box.SimulateMessage(wmChar, '\r');
        box.SimulateMessage(wmKeyDown, (nint)Keys.Delete);
        box.SimulateMessage(wmKeyDown, (nint)Keys.Back);
        box.SimulateMessage(wmPaste);
        box.SimulateMessage(wmCut);
        box.SimulateMessage(wmClear);
        box.SimulateMessage(wmUndo);
        box.SimulateMessage(emReplaceSel);
        box.SelectedText = "pegado";

        box.Text.Should().Be("uno" + Environment.NewLine + "dos");
        box.ReadOnly.Should().BeFalse();
        box.Multiline.Should().BeTrue();
        box.SelectionLength.Should().Be(box.TextLength, "selecting still works");
    });

    [Fact]
    public void ProtectedTextBox_Append_KeepsTheCaretWhereItWas() => Sta.Run(() =>
    {
        using var form = new Form();
        var box = new ProtectedTextBox();
        form.Controls.Add(box);
        box.SetProtectedText("primera línea");
        _ = box.Handle;
        box.Select(3, 2);

        box.AppendProtectedText(Environment.NewLine + "segunda");

        box.Text.Should().EndWith("segunda");
        (box.SelectionStart, box.SelectionLength).Should().Be((3, 2));
    });

    // ═══════════════════════════ FrmReport ═══════════════════════════

    [Theory]
    [InlineData("es", ReportKind.Error, false)]
    [InlineData("en", ReportKind.Error, false)]
    [InlineData("es", ReportKind.Suggestion, false)]
    [InlineData("en", ReportKind.Suggestion, false)]
    [InlineData("es", ReportKind.Error, true)]
    [InlineData("en", ReportKind.Error, true)]
    public void Report_PassesTheAudit(string culture, ReportKind kind, bool withException) => Sta.Run(() =>
    {
        Culture(culture);
        using var form = new FrmReport(Presenter(kind, withException ? new InvalidOperationException("x") : null), _ => { });

        AccessibilityAudit.Check(form, isDialog: true).Should().BeEmpty();
        NoDescriptions_NoRichTextBox(form);
    });

    [Fact]
    public void Report_HasEveryControlOfTheBrief_WithLabelsAndNames() => Sta.Run(() =>
    {
        using var form = new FrmReport(Presenter(), _ => { });

        form.Text.Should().Be("Informar de un error");
        Get<ComboBox>(form, "_cboKind").Items.Cast<string>().Should().Equal("Error", "Sugerencia");
        Get<ComboBox>(form, "_cboKind").AccessibleName.Should().Be("Tipo");
        Get<TextBox>(form, "_txtDescription").AccessibleName.Should().Be("Descripción");
        Get<TextBox>(form, "_txtDescription").Multiline.Should().BeTrue();
        Get<CheckBox>(form, "_chkDiagnostics").Text.Should().Be("&Incluir datos de diagnóstico");
        Get<CheckBox>(form, "_chkDiagnostics").Checked.Should().BeTrue();
        Get<TextBox>(form, "_txtTitle").AccessibleName.Should().Be("Título");
        var preview = Get<TextBox>(form, "_txtPreview");
        preview.AccessibleName.Should().Be("Vista previa de lo que se enviará (puedes editarla)");
        preview.ReadOnly.Should().BeFalse("the preview is editable");
        new[] { "_btnGitHub", "_btnEmail", "_btnCopy", "_btnPersonalInfo", "_btnCancel" }
            .Select(n => Get<Button>(form, n).Text).Should().Equal("Abrir en &GitHub", "Enviar por &correo", "Copiar al &portapapeles", "I&nformación personal...", "Cancelar");
        form.ActiveControl.Should().BeSameAs(Get<TextBox>(form, "_txtDescription"));
        Get<Label>(form, "_lblPrivacy").Text.Should().Contain("Nada se envía automáticamente").And.Contain("contraseñas");
    });

    [Fact]
    public void Report_TypingTheDescription_UpdatesTitleAndPreview_AndTheKindChangesTheWindowTitle() => Sta.Run(() =>
    {
        using var form = new FrmReport(Presenter(), _ => { });

        Get<TextBox>(form, "_txtDescription").Text = "Se cuelga con F5";

        Get<TextBox>(form, "_txtTitle").Text.Should().Be("[Error] Se cuelga con F5");
        Get<TextBox>(form, "_txtPreview").Text.Should().Contain("Se cuelga con F5").And.Contain("Windows de prueba");

        Get<ComboBox>(form, "_cboKind").SelectedIndex = 1;
        form.Text.Should().Be("Enviar una sugerencia");
        Get<TextBox>(form, "_txtTitle").Text.Should().StartWith("[Sugerencia]");

        Get<CheckBox>(form, "_chkDiagnostics").Checked = false;
        Get<TextBox>(form, "_txtPreview").Text.Should().NotContain("Windows de prueba");
    });

    [Fact]
    public void Report_WhatIsEditedInThePreview_IsWhatTheButtonSends() => Sta.Run(() =>
    {
        using var form = new FrmReport(Presenter(), _ => { });
        Get<TextBox>(form, "_txtDescription").Text = "Original";
        Get<TextBox>(form, "_txtPreview").Text = "Solo esto.";

        Click(Get<Button>(form, "_btnGitHub"));

        var sent = Uri.UnescapeDataString(_launcher.Opened.Single().AbsoluteUri);
        sent.Should().Contain("body=Solo esto.").And.NotContain("Windows de prueba");
        form.DialogResult.Should().Be(DialogResult.OK);
    });

    [Fact]
    public void Report_WithoutDescription_Warns_Stays_AndFocusesTheDescription() => Sta.Run(() =>
    {
        using var form = new FrmReport(Presenter(), _ => { });
        form.ActiveControl = Get<Button>(form, "_btnGitHub");

        Click(Get<Button>(form, "_btnGitHub"));

        _prompts.Warnings.Should().ContainSingle();
        _launcher.Opened.Should().BeEmpty();
        form.DialogResult.Should().Be(DialogResult.None);
        form.ActiveControl.Should().BeSameAs(Get<TextBox>(form, "_txtDescription"));
    });

    [Fact]
    public void Report_Copy_UsesTheClipboardService_AndStaysOpen() => Sta.Run(() =>
    {
        using var form = new FrmReport(Presenter(), _ => { });
        Get<TextBox>(form, "_txtDescription").Text = "Copiar esto";

        Click(Get<Button>(form, "_btnCopy"));

        _clipboard.Text.Should().Contain("Copiar esto");
        form.DialogResult.Should().Be(DialogResult.None);
    });

    [Fact]
    public void Report_ForAnException_IsAnErrorReport_WithTheKindLocked() => Sta.Run(() =>
    {
        using var form = new FrmReport(Presenter(ReportKind.Suggestion, new InvalidOperationException("algo falló")), _ => { });

        Get<ComboBox>(form, "_cboKind").Enabled.Should().BeFalse();
        Get<ComboBox>(form, "_cboKind").SelectedIndex.Should().Be(0);
        Get<TextBox>(form, "_txtPreview").Text.Should().Contain("InvalidOperationException").And.Contain("algo falló");
    });

    [Fact]
    public void Report_PersonalInformationButton_OpensThatDialog_AndIsHiddenWithoutOne() => Sta.Run(() =>
    {
        var opened = 0;
        using var form = new FrmReport(Presenter(), _ => opened++);
        Click(Get<Button>(form, "_btnPersonalInfo"));
        opened.Should().Be(1);

        using var without = new FrmReport(Presenter());
        Get<Button>(without, "_btnPersonalInfo").Enabled.Should().BeFalse();
    });

    // ═══════════════════════════ FrmPersonalInfo ═══════════════════════════

    private FrmPersonalInfo PersonalInfoForm()
    {
        var form = new FrmPersonalInfo(new PersonalInfoModel(_personalInfo), _prompts);
        UiPump.Wait(form.InitializeAsync());
        return form;
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void PersonalInfo_PassesTheAudit(string culture) => Sta.Run(() =>
    {
        Culture(culture);
        using var form = PersonalInfoForm();

        AccessibilityAudit.Check(form, isDialog: true).Should().BeEmpty();
        NoDescriptions_NoRichTextBox(form);
    });

    [Fact]
    public void PersonalInfo_ShowsWhatIsStored_AndExplainsWhatItIsFor() => Sta.Run(() =>
    {
        _personalInfo.SaveAsync(new PersonalInfo("María", "maria@example.org")).GetAwaiter().GetResult();
        using var form = PersonalInfoForm();

        Get<TextBox>(form, "_txtName").Text.Should().Be("María");
        Get<TextBox>(form, "_txtName").AccessibleName.Should().Be("Nombre");
        Get<TextBox>(form, "_txtEmail").Text.Should().Be("maria@example.org");
        Get<TextBox>(form, "_txtEmail").AccessibleName.Should().Be("Correo electrónico");
        Get<Label>(form, "_lblHint").Text.Should().Contain("opcionales").And.Contain("Nunca se envían");
    });

    [Fact]
    public void PersonalInfo_Accept_Saves_AndCloses() => Sta.Run(() =>
    {
        using var form = PersonalInfoForm();
        Get<TextBox>(form, "_txtName").Text = " María ";
        Get<TextBox>(form, "_txtEmail").Text = "maria@example.org";

        UiPump.Wait(form.AcceptAsync());

        form.DialogResult.Should().Be(DialogResult.OK);
        _personalInfo.LoadAsync().GetAwaiter().GetResult().Should().Be(new PersonalInfo("María", "maria@example.org"));
    });

    [Fact]
    public void PersonalInfo_BadEmail_Warns_FocusesTheBox_AndStoresNothing() => Sta.Run(() =>
    {
        using var form = PersonalInfoForm();
        Get<TextBox>(form, "_txtName").Text = "María";
        Get<TextBox>(form, "_txtEmail").Text = "no es un correo";

        UiPump.Wait(form.AcceptAsync());

        _prompts.Warnings.Should().ContainSingle().Which.Should().Contain("correo");
        form.DialogResult.Should().Be(DialogResult.None);
        form.ActiveControl.Should().BeSameAs(Get<TextBox>(form, "_txtEmail"));
        _personalInfo.LoadAsync().GetAwaiter().GetResult().Should().Be(PersonalInfo.Empty);
    });

    [Fact]
    public void PersonalInfo_EmptyFields_AreFine() => Sta.Run(() =>
    {
        using var form = PersonalInfoForm();

        UiPump.Wait(form.AcceptAsync());

        form.DialogResult.Should().Be(DialogResult.OK);
        _prompts.Warnings.Should().BeEmpty();
    });

    // ═══════════════════════════ FrmUnexpectedError ═══════════════════════════

    [Theory]
    [InlineData("es", true)]
    [InlineData("en", true)]
    [InlineData("es", false)]
    [InlineData("en", false)]
    public void UnexpectedError_PassesTheAudit(string culture, bool canReport) => Sta.Run(() =>
    {
        Culture(culture);
        using var form = new FrmUnexpectedError(canReport, _clipboard, Substitute.For<IAnnouncer>());
        form.AddError("System.InvalidOperationException: x");

        AccessibilityAudit.Check(form, isDialog: true).Should().BeEmpty();
        NoDescriptions_NoRichTextBox(form);
    });

    [Fact]
    public void UnexpectedError_HasTheThreeButtons_FocusOnContinue_AndEscapeContinues() => Sta.Run(() =>
    {
        using var form = new FrmUnexpectedError(true, _clipboard, Substitute.For<IAnnouncer>());

        form.Text.Should().Be("Error inesperado");
        Get<Button>(form, "_btnReport").Text.Should().Be("&Informar de este error...");
        Get<Button>(form, "_btnReport").DialogResult.Should().Be(DialogResult.Yes);
        Get<Button>(form, "_btnCopy").Text.Should().Be("C&opiar detalles");
        Get<Button>(form, "_btnContinue").Text.Should().Be("&Continuar");
        form.CancelButton.Should().BeSameAs(Get<Button>(form, "_btnContinue"));
        form.AcceptButton.Should().BeSameAs(Get<Button>(form, "_btnContinue"));
        form.ActiveControl.Should().BeSameAs(Get<Button>(form, "_btnContinue"));
        Get<ProtectedTextBox>(form, "_txtDetails").ReadOnly.Should().BeFalse();
        Get<Label>(form, "_lblMessage").Text.Should().Contain("sigue funcionando");
    });

    [Fact]
    public void UnexpectedError_FurtherErrors_AreAppended_AndCounted() => Sta.Run(() =>
    {
        using var form = new FrmUnexpectedError(true, _clipboard, Substitute.For<IAnnouncer>());

        form.AddError("primero");
        form.AddError("segundo");
        form.AddError("tercero");

        form.Details.Should().StartWith("primero").And.Contain("Error número 2:").And.Contain("segundo").And.Contain("Error número 3:").And.EndWith("tercero");
        Get<Label>(form, "_lblMessage").Text.Should().Contain("3 errores");
    });

    [Fact]
    public void UnexpectedError_CopyDetails_CopiesEverything_AndAnnouncesIt() => Sta.Run(() =>
    {
        var announcer = Substitute.For<IAnnouncer>();
        using var form = new FrmUnexpectedError(true, _clipboard, announcer);
        form.AddError("primero");
        form.AddError("segundo");

        Click(Get<Button>(form, "_btnCopy"));

        _clipboard.Text.Should().Be(form.Details);
        announcer.Received(1).Announce("Detalles copiados al portapapeles.", AnnouncePriority.Interrupt);
        form.DialogResult.Should().Be(DialogResult.None, "copying does not close the dialog");
    });

    [Fact]
    public void UnexpectedError_WhenTheClipboardFails_SaysSo() => Sta.Run(() =>
    {
        var announcer = Substitute.For<IAnnouncer>();
        using var form = new FrmUnexpectedError(true, new FakeClipboard(works: false), announcer);
        form.AddError("x");

        form.CopyDetails();

        announcer.Received(1).Announce("No se pudieron copiar los detalles al portapapeles.", AnnouncePriority.Interrupt);
    });

    [Fact]
    public void UnexpectedError_WithoutAReportDialog_HidesThatButton() => Sta.Run(() =>
    {
        using var form = new FrmUnexpectedError(canReport: false, _clipboard, Substitute.For<IAnnouncer>());
        Get<Button>(form, "_btnReport").Enabled.Should().BeFalse();
    });
}
