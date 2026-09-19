using System.Globalization;
using NSubstitute;
using Omnimud.Core.Options;
using Omnimud.Core.Security;
using Omnimud.Data;
using Omnimud.Data.Exchange;
using Omnimud.Data.Migrations;
using Omnimud.Data.Options;
using Omnimud.Data.Repositories;
using Omnimud.UI.Forms;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;
using Omnimud.UI.Tests.Accessibility;
using Omnimud.UI.Tests.Presenters;

namespace Omnimud.UI.Tests.Forms;

public sealed class FrmOptionsTests
{
    private const int MudId = 3;
    private const int CharacterId = 7;

    private readonly FakeOptionsService _service = new();
    private readonly IUserPrompts _prompts = Substitute.For<IUserPrompts>();
    private readonly IFontPrompt _fontPrompt = Substitute.For<IFontPrompt>();
    private readonly IOptionsFileStore _files = Substitute.For<IOptionsFileStore>();
    private readonly IDirectoryAccess _directories = Substitute.For<IDirectoryAccess>();
    private readonly ProxyCredentialStore _credentials =
        new(new AesPasswordProtector(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));

    public FrmOptionsTests()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("es");
        _directories.Exists(Arg.Any<string>()).Returns(true);
    }

    private FrmOptions Create(OptionScope scope = OptionScope.Global, int? id = null, string name = "", int? parentMudId = null)
    {
        var model = new OptionsEditorModel(_service, scope, id, name, parentMudId, _files, _directories, _credentials);
        var form = new FrmOptions(model, _prompts, _fontPrompt);
        Wait(form.InitializeAsync());
        return form;
    }

    /// <summary>STA thread, and an exception inside a window procedure fails the test instead of opening
    /// the modal WinForms error dialog, which nobody would answer.</summary>
    private static void Ui(Action action) => Sta.Run(() =>
    {
        Exception? failure = null;
        Application.ThreadException += (_, e) => failure ??= e.Exception;
        action();
        if (failure is not null)
            throw new InvalidOperationException("Unhandled exception in the user interface: " + failure, failure);
    });

    private FrmOptions CreateMud() => Create(OptionScope.Mud, MudId, "Reinos");
    private FrmOptions CreateCharacter() => Create(OptionScope.Character, CharacterId, "Aldara", MudId);

    /// <summary>Waits for a task started on this STA thread, pumping messages so its continuations can run.</summary>
    private static void Wait(Task task)
    {
        var limit = DateTime.UtcNow.AddSeconds(20);
        while (!task.IsCompleted)
        {
            if (DateTime.UtcNow > limit) throw new TimeoutException("The form did not finish its asynchronous work.");
            Application.DoEvents();
            Thread.Sleep(5);
        }
        task.GetAwaiter().GetResult();
    }

    private static T Get<T>(Form form, string name) where T : Control =>
        (T)form.Controls.Find(name, searchAllChildren: true).Single();

    private static IEnumerable<Control> Descendants(Control root) =>
        root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));

    private static IEnumerable<Control> Inputs(Form form) =>
        Descendants(Get<TabControl>(form, "_tabs")).Where(c => c is ComboBox or NumericUpDown or CheckBox or Button || c is TextBox && c.Parent is TableLayoutPanel);

    // ───────────────────────────── Accessibility ─────────────────────────────

    [Theory]
    [InlineData("es", OptionScope.Global)]
    [InlineData("es", OptionScope.Mud)]
    [InlineData("es", OptionScope.Character)]
    [InlineData("en", OptionScope.Global)]
    [InlineData("en", OptionScope.Mud)]
    [InlineData("en", OptionScope.Character)]
    public void Dialog_PassesTheAccessibilityAudit_InEveryLanguageAndScope(string culture, OptionScope scope) => Ui(() =>
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);
        using var form = scope == OptionScope.Global ? Create() : scope == OptionScope.Mud ? CreateMud() : CreateCharacter();

        AccessibilityAudit.Check(form, isDialog: true).Should().BeEmpty();
    });

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void EveryLabelledBox_HasAMnemonic_AndAnAccessibleNameWithoutAmpersandOrColon(string culture) => Ui(() =>
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);
        using var form = CreateMud();

        var boxes = Descendants(form).Where(c => c is ComboBox or NumericUpDown || c is TextBox && c.Parent is TableLayoutPanel).ToList();
        boxes.Should().HaveCount(21);
        foreach (var box in boxes)
        {
            var label = box.Parent!.Controls.OfType<Label>().Single(l => l.TabIndex == box.TabIndex - 1);
            AccessibilityAudit.Mnemonic(label.Text).Should().NotBeNull($"{label.Text} needs a mnemonic");
            box.AccessibleName.Should().Be(FrmOptions.PlainText(label.Text));
            box.AccessibleName.Should().NotContain("&").And.NotEndWith(":");
        }
    });

    [Fact]
    public void NoControl_HasAnAccessibleDescription_AndThereIsNoRichTextBox() => Ui(() =>
    {
        using var form = CreateMud();

        Descendants(form).Should().OnlyContain(c => string.IsNullOrEmpty(c.AccessibleDescription), "NVDA reads the description on every focus");
        Descendants(form).OfType<RichTextBox>().Should().BeEmpty();
        Get<TextBox>(form, "_txtPreview").Multiline.Should().BeTrue();
    });

    [Fact]
    public void ThereIsOneSetOfButtons_OutsideTheTabs() => Ui(() =>
    {
        using var form = Create();

        var tabs = Get<TabControl>(form, "_tabs");
        foreach (var name in new[] { "_btnOk", "_btnCancel", "_btnExport", "_btnImport" })
        {
            var buttons = form.Controls.Find(name, true);
            buttons.Should().ContainSingle();
            Descendants(tabs).Should().NotContain(buttons[0]);
        }
        tabs.TabPages.Cast<TabPage>().Select(p => p.Text)
            .Should().Equal("General", "Logs", "Sonidos", "Conexión", "Apariencia", "Caracteres especiales");
        form.AcceptButton.Should().BeSameAs(Get<Button>(form, "_btnOk"));
        form.CancelButton.Should().BeSameAs(Get<Button>(form, "_btnCancel"));
        int Tab(string n) => Get<Button>(form, n).TabIndex;
        new[] { Tab("_btnOk"), Tab("_btnCancel"), Tab("_btnExport"), Tab("_btnImport") }.Should().BeInAscendingOrder();
    });

    [Fact]
    public void Title_SaysTheScope() => Ui(() =>
    {
        using var global = Create();
        using var mud = CreateMud();
        using var character = CreateCharacter();

        global.Text.Should().Be("Opciones globales");
        mud.Text.Should().Be("Opciones del MUD Reinos");
        character.Text.Should().Be("Opciones del personaje Aldara");
    });

    [Fact]
    public void Combos_OfferEveryValueOfTheirEnum() => Ui(() =>
    {
        FrmOptions.ScreenReaderItems.Should().BeEquivalentTo(Enum.GetValues<ScreenReaderMode>());
        FrmOptions.CursorItems.Should().BeEquivalentTo(Enum.GetValues<CursorBehavior>());
        FrmOptions.LogItems.Should().BeEquivalentTo(Enum.GetValues<LogMode>());
        FrmOptions.ProxyItems.Should().BeEquivalentTo(Enum.GetValues<ProxyMode>());
        FrmOptions.ProtocolItems.Should().BeEquivalentTo(Enum.GetValues<ProxyProtocol>());

        using var form = Create();
        Get<ComboBox>(form, "_cboScreenReader").Items.Cast<string>().Should().Equal("Automático (UI Automation)", "JAWS", "NVDA", "Ninguno");
        Get<ComboBox>(form, "_cboLanguage").Items.Cast<string>().Should().Equal("Automático", "Español", "English");
        Get<ComboBox>(form, "_cboCursorReceived").Items.Count.Should().Be(3);
        Get<ComboBox>(form, "_cboCursorMessages").Items.Count.Should().Be(3);
        Get<ComboBox>(form, "_cboLogType").Items.Count.Should().Be(3);
        Get<ComboBox>(form, "_cboProxy").Items.Count.Should().Be(3);
        Get<ComboBox>(form, "_cboProxyProtocol").Items.Cast<string>().Should().Equal("SOCKS5", "HTTP CONNECT");
    });

    // ───────────────────────────── Every option has its control ─────────────────────────────

    [Fact]
    public void EveryOption_GoesFromTheStoreToTheControls_AndBackOnAccept() => Ui(() =>
    {
        var sample = OptionsSamples.AllNonDefault();
        _service.With(OptionScope.Global, null, sample);
        using var form = Create();
        form.Show();

        Wait(form.AcceptAsync());

        _service.Saved.Should().ContainSingle();
        _service.Saved[0].Options.Should().Be(sample, "an option without a control in the dialog is lost when the block is saved");
    });

    public static TheoryData<string> OptionNames()
    {
        var data = new TheoryData<string>();
        foreach (var property in OptionsSamples.Properties) data.Add(property.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(OptionNames))]
    public void EachOption_HasAControlThatShowsAndReadsIt(string name) => Ui(() =>
    {
        var options = OptionsSamples.WithOnly(typeof(OmnimudOptions).GetProperty(name)!);
        using var form = Create();

        form.ShowFields(OptionsFields.From(options));

        form.ReadFields().ToOptions().Should().Be(options, $"the dialog needs a control for {name}");
    });

    [Fact]
    public void UpdateCheckBox_IsOffByDefault_AndOnlyEnabledInTheGlobalOptions() => Ui(() =>
    {
        using var global = Create();
        using var mud = CreateMud();
        using var character = CreateCharacter();

        var box = Get<CheckBox>(global, "_chkCheckUpdates");
        box.Checked.Should().BeFalse("nothing goes to the network on its own unless the user asks for it");
        box.Text.Should().Be("Comprobar si hay actualizaciones al iniciar Omnimud");
        box.UseMnemonic.Should().BeFalse("the dialog has no letters left: check boxes go without mnemonic");
        box.Parent!.Parent.Should().BeSameAs(Get<TabPage>(global, "_tabGeneral"));
        box.Enabled.Should().BeTrue();
        Get<CheckBox>(mud, "_chkCheckUpdates").Enabled.Should().BeFalse();
        Get<CheckBox>(character, "_chkCheckUpdates").Enabled.Should().BeFalse();
    });

    [Fact]
    public void Controls_ShowTheStoredValues() => Ui(() =>
    {
        _service.With(OptionScope.Global, null, OmnimudOptions.Default with
        {
            ScreenReader = ScreenReaderMode.Nvda, CursorOnMessages = CursorBehavior.Keep, HistorySize = 123, Language = "en",
            LogType = LogMode.PerSession, LogDirectory = @"D:\logs", Volume = 35, ProxyType = ProxyMode.Manual, ProxyHost = "proxy.local",
            ProxyPort = 1080, ProxyProtocol = ProxyProtocol.HttpConnect, FontFamily = "Arial", FontSize = 9.75f,
            UseConcatChar = true, ConcatChar = '|', ConfirmBeforeExit = false
        });
        using var form = Create();

        Get<ComboBox>(form, "_cboScreenReader").Text.Should().Be("NVDA");
        Get<ComboBox>(form, "_cboCursorMessages").SelectedIndex.Should().Be(1);
        Get<NumericUpDown>(form, "_nudHistorySize").Value.Should().Be(123);
        Get<ComboBox>(form, "_cboLanguage").Text.Should().Be("English");
        Get<ComboBox>(form, "_cboLogType").SelectedIndex.Should().Be(2);
        Get<TextBox>(form, "_txtLogDirectory").Text.Should().Be(@"D:\logs");
        Get<NumericUpDown>(form, "_nudVolume").Value.Should().Be(35);
        Get<ComboBox>(form, "_cboProxy").SelectedIndex.Should().Be(2);
        Get<TextBox>(form, "_txtProxyHost").Text.Should().Be("proxy.local");
        Get<NumericUpDown>(form, "_nudProxyPort").Value.Should().Be(1080);
        Get<ComboBox>(form, "_cboProxyProtocol").Text.Should().Be("HTTP CONNECT");
        Get<ComboBox>(form, "_cboFont").Text.Should().Be("Arial");
        Get<NumericUpDown>(form, "_nudFontSize").Value.Should().Be(9.75m);
        Get<CheckBox>(form, "_chkUseConcat").Checked.Should().BeTrue();
        Get<TextBox>(form, "_txtConcatChar").Text.Should().Be("|");
        Get<CheckBox>(form, "_chkConfirmExit").Checked.Should().BeFalse();
    });

    [Fact]
    public void AFontThatIsNotInstalled_IsKept() => Ui(() =>
    {
        _service.With(OptionScope.Global, null, OmnimudOptions.Default with { FontFamily = "Fuente Que No Existe" });
        using var form = Create();

        Get<ComboBox>(form, "_cboFont").Text.Should().Be("Fuente Que No Existe");
        form.ReadFields().FontFamily.Should().Be("Fuente Que No Existe");
    });

    // ───────────────────────────── Accept / cancel ─────────────────────────────

    [Fact]
    public void Accept_Saves_AndClosesTheDialog() => Ui(() =>
    {
        using var form = Create();
        var closed = false;
        form.FormClosed += (_, _) => closed = true;
        form.Show();
        Get<NumericUpDown>(form, "_nudVolume").Value = 42;

        Get<Button>(form, "_btnOk").PerformClick();
        Application.DoEvents();

        _service.Saved.Should().ContainSingle().Which.Options.Volume.Should().Be(42);
        _service.Saved[0].Scope.Should().Be(OptionScope.Global);
        closed.Should().BeTrue("OK saves AND closes");
        form.DialogResult.Should().Be(DialogResult.OK);
    });

    [Fact]
    public void Cancel_ClosesWithoutSaving() => Ui(() =>
    {
        using var form = Create();
        var closed = false;
        form.FormClosed += (_, _) => closed = true;
        form.Show();
        Get<NumericUpDown>(form, "_nudVolume").Value = 42;

        Get<Button>(form, "_btnCancel").PerformClick();
        Application.DoEvents();

        closed.Should().BeTrue();
        _service.Saved.Should().BeEmpty();
        _service.Resets.Should().BeEmpty();
    });

    [Fact]
    public void ValidationError_OnAnotherTab_SwitchesToItsTab_FocusesTheField_AndKeepsTheDialogOpen() => Ui(() =>
    {
        using var form = Create();
        var closed = false;
        form.FormClosed += (_, _) => closed = true;
        form.Show();
        var tabs = Get<TabControl>(form, "_tabs");
        tabs.SelectedTab!.Name.Should().Be("_tabGeneral");
        Get<ComboBox>(form, "_cboProxy").SelectedIndex = 2; // manual, without host

        Wait(form.AcceptAsync());

        closed.Should().BeFalse();
        _service.Saved.Should().BeEmpty();
        _prompts.Received(1).Warn("Debes introducir el servidor proxy.", "Opciones globales");
        tabs.SelectedTab!.Name.Should().Be("_tabConnection");
        form.ActiveControl.Should().BeSameAs(Get<TextBox>(form, "_txtProxyHost"));
    });

    [Fact]
    public void ValidationError_InTheLastTab_AlsoGetsTheFocus_WithItsTextSelected() => Ui(() =>
    {
        using var form = Create();
        form.Show();
        Get<CheckBox>(form, "_chkUseConcat").Checked = true;
        Get<CheckBox>(form, "_chkUseRepeat").Checked = true;
        Get<TextBox>(form, "_txtConcatChar").Text = "#";
        Get<TextBox>(form, "_txtRepeatChar").Text = "#";

        Wait(form.AcceptAsync());

        _prompts.Received(1).Warn("El carácter que separa los comandos y el de repetición deben ser distintos.", Arg.Any<string?>());
        Get<TabControl>(form, "_tabs").SelectedTab!.Name.Should().Be("_tabSpecialChars");
        var box = Get<TextBox>(form, "_txtRepeatChar");
        form.ActiveControl.Should().BeSameAs(box);
        box.SelectedText.Should().Be("#");
        _service.Saved.Should().BeEmpty();
    });

    [Fact]
    public void EveryFieldOfTheModel_CanBeFocused() => Ui(() =>
    {
        using var form = Create();
        form.Show();
        // Everything enabled, so every control can take the focus.
        form.ShowFields(OptionsFields.From(OptionsSamples.AllNonDefault()) with
        {
            EnableSounds = true, EnableMusic = true, DownloadSounds = true, UseConcatChar = true, UseRepeatChar = true
        });

        foreach (var field in Enum.GetValues<OptionsField>())
        {
            form.FocusField(field);

            form.ActiveControl.Should().NotBeNull();
            var page = Get<TabControl>(form, "_tabs").SelectedTab!;
            Descendants(page).Should().Contain(form.ActiveControl!, $"{field} must be focused on the visible tab");
        }
    });

    [Fact]
    public void ChangingTheLanguage_TellsItNeedsARestart() => Ui(() =>
    {
        using var form = Create();
        form.Show();
        Get<ComboBox>(form, "_cboLanguage").SelectedIndex = 2;

        Wait(form.AcceptAsync());

        _service.Saved.Single().Options.Language.Should().Be("en");
        _prompts.Received(1).Info("El idioma cambiará la próxima vez que inicies Omnimud.", Arg.Any<string?>());
    });

    // ───────────────────────────── Inherit box ─────────────────────────────

    [Fact]
    public void Global_HasNoInheritBox() => Ui(() =>
    {
        using var form = Create();

        form.Controls.Find("_chkInherit", true).Should().BeEmpty();
        Inputs(form).Where(c => c.Name != "_txtPreview").Should().Contain(c => c.Enabled);
    });

    [Fact]
    public void Mud_ThatInherits_StartsChecked_WithEverythingDisabled_ShowingTheGlobalValues() => Ui(() =>
    {
        _service.With(OptionScope.Global, null, OmnimudOptions.Default with { Volume = 40, HistorySize = 99 });
        using var form = CreateMud();

        var inherit = Get<CheckBox>(form, "_chkInherit");
        inherit.Text.Should().Be("&Usar las opciones globales");
        inherit.Checked.Should().BeTrue();
        Inputs(form).Should().OnlyContain(c => !c.Enabled);
        Get<NumericUpDown>(form, "_nudVolume").Value.Should().Be(40);
        Get<NumericUpDown>(form, "_nudHistorySize").Value.Should().Be(99);
        Get<Button>(form, "_btnOk").Enabled.Should().BeTrue();
        Get<Button>(form, "_btnImport").Enabled.Should().BeTrue();
    });

    [Fact]
    public void Character_WithOwnOptions_StartsUnchecked_AndEnabled() => Ui(() =>
    {
        _service.With(OptionScope.Character, CharacterId, OmnimudOptions.Default with { Volume = 15 });
        using var form = CreateCharacter();

        var inherit = Get<CheckBox>(form, "_chkInherit");
        inherit.Text.Should().Be("&Usar las opciones del MUD");
        inherit.Checked.Should().BeFalse();
        Get<NumericUpDown>(form, "_nudVolume").Enabled.Should().BeTrue();
        Get<NumericUpDown>(form, "_nudVolume").Value.Should().Be(15);
    });

    [Fact]
    public void CheckingInherit_DisablesAndShowsInherited_UncheckingRestoresTheEdits() => Ui(() =>
    {
        _service.With(OptionScope.Global, null, OmnimudOptions.Default with { Volume = 40 })
                .With(OptionScope.Mud, MudId, OmnimudOptions.Default with { Volume = 90 });
        using var form = CreateMud();
        var inherit = Get<CheckBox>(form, "_chkInherit");
        var volume = Get<NumericUpDown>(form, "_nudVolume");
        volume.Value = 55;

        inherit.Checked = true;
        Inputs(form).Should().OnlyContain(c => !c.Enabled);
        volume.Value.Should().Be(40);

        inherit.Checked = false;
        volume.Enabled.Should().BeTrue();
        volume.Value.Should().Be(55);
    });

    [Fact]
    public void Accept_WithInheritChecked_ResetsTheScope() => Ui(() =>
    {
        _service.With(OptionScope.Mud, MudId, OmnimudOptions.Default with { Volume = 90 });
        using var form = CreateMud();
        form.Show();
        Get<CheckBox>(form, "_chkInherit").Checked = true;

        Wait(form.AcceptAsync());

        _service.Resets.Should().Equal((OptionScope.Mud, (int?)MudId));
        _service.Saved.Should().BeEmpty();
        form.DialogResult.Should().Be(DialogResult.OK);
    });

    [Fact]
    public void Accept_WithInheritUnchecked_SavesTheWholeBlockForTheScope() => Ui(() =>
    {
        _service.With(OptionScope.Global, null, OmnimudOptions.Default with { Volume = 40 });
        using var form = CreateCharacter();
        form.Show();
        Get<CheckBox>(form, "_chkInherit").Checked = false;
        Get<NumericUpDown>(form, "_nudHistorySize").Value = 5;

        Wait(form.AcceptAsync());

        var saved = _service.Saved.Should().ContainSingle().Subject;
        saved.Scope.Should().Be(OptionScope.Character);
        saved.ScopeId.Should().Be(CharacterId);
        saved.Options.Should().Be(OmnimudOptions.Default with { Volume = 40, HistorySize = 5 }, "it starts from a copy of what it inherited");
    });

    // ───────────────────────────── Dependent controls ─────────────────────────────

    [Fact]
    public void ProxyDetails_AreOnlyEnabledWithAManualProxy() => Ui(() =>
    {
        using var form = Create();
        var proxy = Get<ComboBox>(form, "_cboProxy");
        bool Details() => Get<TextBox>(form, "_txtProxyHost").Enabled && Get<NumericUpDown>(form, "_nudProxyPort").Enabled
                          && Get<ComboBox>(form, "_cboProxyProtocol").Enabled;

        proxy.SelectedIndex.Should().Be(0);
        Details().Should().BeFalse();
        Get<CheckBox>(form, "_chkProxyForMud").Enabled.Should().BeFalse();

        proxy.SelectedIndex = 1;
        Details().Should().BeFalse();
        Get<CheckBox>(form, "_chkProxyForMud").Enabled.Should().BeTrue();

        proxy.SelectedIndex = 2;
        Details().Should().BeTrue();
    });

    [Fact]
    public void LogFolder_IsDisabledWhenLogsAreNotSaved() => Ui(() =>
    {
        using var form = Create();
        var type = Get<ComboBox>(form, "_cboLogType");

        type.SelectedIndex = 0;
        Get<TextBox>(form, "_txtLogDirectory").Enabled.Should().BeFalse();
        Get<Button>(form, "_btnBrowse").Enabled.Should().BeFalse();

        type.SelectedIndex = 1;
        Get<TextBox>(form, "_txtLogDirectory").Enabled.Should().BeTrue();
        Get<Button>(form, "_btnBrowse").Enabled.Should().BeTrue();
    });

    [Fact]
    public void CharacterBoxes_FollowTheirCheckBoxes_AndTakeOneCharacter() => Ui(() =>
    {
        using var form = Create();

        Get<TextBox>(form, "_txtConcatChar").Enabled.Should().BeFalse();
        Get<TextBox>(form, "_txtRepeatChar").Enabled.Should().BeFalse();
        Get<TextBox>(form, "_txtConcatChar").MaxLength.Should().Be(1);
        Get<TextBox>(form, "_txtRepeatChar").MaxLength.Should().Be(1);

        Get<CheckBox>(form, "_chkUseConcat").Checked = true;
        Get<TextBox>(form, "_txtConcatChar").Enabled.Should().BeTrue();
        Get<TextBox>(form, "_txtRepeatChar").Enabled.Should().BeFalse();

        Get<CheckBox>(form, "_chkUseRepeat").Checked = true;
        Get<TextBox>(form, "_txtRepeatChar").Enabled.Should().BeTrue();
        Get<CheckBox>(form, "_chkUseRepeat").Text.Should().NotBe(Get<CheckBox>(form, "_chkUseConcat").Text, "the original showed the same text in both");
    });

    [Fact]
    public void SoundOptions_FollowTheirMasters() => Ui(() =>
    {
        using var form = Create();

        Get<CheckBox>(form, "_chkEnableSounds").Checked = false;
        Get<CheckBox>(form, "_chkSoundsBackground").Enabled.Should().BeFalse();
        Get<CheckBox>(form, "_chkMusicBackground").Enabled.Should().BeTrue();

        Get<CheckBox>(form, "_chkEnableMusic").Checked = false;
        Get<CheckBox>(form, "_chkMusicBackground").Enabled.Should().BeFalse();

        Get<CheckBox>(form, "_chkDownloadSounds").Checked = false;
        Get<CheckBox>(form, "_chkAllowHttp").Enabled.Should().BeFalse();
        Get<CheckBox>(form, "_chkDownloadSounds").Checked = true;
        Get<CheckBox>(form, "_chkAllowHttp").Enabled.Should().BeTrue();
    });

    [Fact]
    public void Language_IsOnlyEditableInTheGlobalDialog() => Ui(() =>
    {
        _service.With(OptionScope.Mud, MudId, OmnimudOptions.Default);
        using var global = Create();
        using var mud = CreateMud();

        Get<ComboBox>(global, "_cboLanguage").Enabled.Should().BeTrue();
        Get<ComboBox>(mud, "_cboLanguage").Enabled.Should().BeFalse();
        Get<ComboBox>(mud, "_cboScreenReader").Enabled.Should().BeTrue();
    });

    // ───────────────────────────── Proxy user and password ─────────────────────────────

    private OmnimudOptions WithStoredProxyPassword(string password = "la-guardada") =>
        _credentials.WithPassword(OmnimudOptions.Default with
        {
            ProxyType = ProxyMode.Manual, ProxyHost = "proxy.corp", ProxyPort = 3128,
            ProxyProtocol = ProxyProtocol.HttpConnect, ProxyUsername = "juan"
        }, password);

    [Theory]
    [InlineData("es", "Usuario del proxy", "Nueva contraseña del proxy, vacía para no cambiarla", "Borrar la contraseña del proxy guardada")]
    [InlineData("en", "Proxy user name", "New proxy password, empty to keep the current one", "Delete the stored proxy password")]
    public void ProxyCredentials_HaveCleanAccessibleNames_AndTheirOwnMnemonics(string culture, string user, string password, string clear) => Ui(() =>
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);
        using var form = Create();

        var userBox = Get<TextBox>(form, "_txtProxyUser");
        var passwordBox = Get<TextBox>(form, "_txtProxyPassword");
        userBox.AccessibleName.Should().Be(user, "the mnemonic letter in brackets is not part of the name");
        passwordBox.AccessibleName.Should().Be(password);
        Get<CheckBox>(form, "_chkClearProxyPassword").Text.Should().Be(clear);

        var labels = new[] { Get<Label>(form, "_lblProxyUser"), Get<Label>(form, "_lblProxyPassword") };
        labels.Select(l => AccessibilityAudit.Mnemonic(l.Text)).Should().OnlyHaveUniqueItems().And.NotContainNulls();
        labels[0].TabIndex.Should().Be(userBox.TabIndex - 1);
        labels[1].TabIndex.Should().Be(passwordBox.TabIndex - 1);
    });

    [Fact]
    public void PlainText_DropsABracketedMnemonic_AndLeavesOtherBracketsAlone()
    {
        FrmOptions.PlainText("Usuario del proxy (&K):").Should().Be("Usuario del proxy");
        FrmOptions.PlainText("&Volumen (0 a 100):").Should().Be("Volumen (0 a 100)");
        FrmOptions.PlainText("Tom && Jerry (&J):").Should().Be("Tom & Jerry");
    }

    [Fact]
    public void PasswordBox_IsMasked_StartsEmpty_AndTheStoredPasswordIsNowhereInTheWindow() => Ui(() =>
    {
        var stored = WithStoredProxyPassword();
        _service.With(OptionScope.Global, null, stored);
        using var form = Create();
        form.Show();

        var box = Get<TextBox>(form, "_txtProxyPassword");
        box.UseSystemPasswordChar.Should().BeTrue();
        box.Text.Should().BeEmpty("the stored password is never shown, not even as dots");
        Get<TextBox>(form, "_txtProxyUser").Text.Should().Be("juan");
        Get<CheckBox>(form, "_chkClearProxyPassword").Checked.Should().BeFalse();
        foreach (var control in Descendants(form))
        {
            (control.Text ?? string.Empty).Should().NotContain("la-guardada").And.NotContain(stored.ProxyPasswordProtected!);
            (control.Tag?.ToString() ?? string.Empty).Should().NotContain(stored.ProxyPasswordProtected!);
        }
    });

    [Fact]
    public void ProxyCredentials_AreEnabledInManualAndAutomatic_AndDeleteOnlyWithAStoredPassword() => Ui(() =>
    {
        _service.With(OptionScope.Global, null, WithStoredProxyPassword() with { ProxyType = ProxyMode.Disabled });
        using var form = Create();
        var proxy = Get<ComboBox>(form, "_cboProxy");
        bool Credentials() => Get<TextBox>(form, "_txtProxyUser").Enabled && Get<TextBox>(form, "_txtProxyPassword").Enabled;
        bool Clear() => Get<CheckBox>(form, "_chkClearProxyPassword").Enabled;

        (Credentials(), Clear()).Should().Be((false, false));
        proxy.SelectedIndex = 1;
        (Credentials(), Clear()).Should().Be((true, true));
        Get<TextBox>(form, "_txtProxyHost").Enabled.Should().BeFalse("host, port and protocol stay manual-only");
        proxy.SelectedIndex = 2;
        (Credentials(), Clear()).Should().Be((true, true));

        using var empty = new FrmOptions(new OptionsEditorModel(new FakeOptionsService(), OptionScope.Global, null, null, null, _files, _directories, _credentials), _prompts, _fontPrompt);
        Wait(empty.InitializeAsync());
        Get<ComboBox>(empty, "_cboProxy").SelectedIndex = 2;
        Get<TextBox>(empty, "_txtProxyPassword").Enabled.Should().BeTrue();
        Get<CheckBox>(empty, "_chkClearProxyPassword").Enabled.Should().BeFalse("there is nothing to delete");
    });

    [Fact]
    public void Accept_WithTheBoxEmpty_KeepsTheStoredPassword() => Ui(() =>
    {
        var stored = WithStoredProxyPassword();
        _service.With(OptionScope.Global, null, stored);
        using var form = Create();
        form.Show();
        Get<NumericUpDown>(form, "_nudVolume").Value = 9;

        Wait(form.AcceptAsync());

        _service.Saved.Single().Options.Should().Be(stored with { Volume = 9 });
    });

    [Fact]
    public void Accept_WithANewPassword_StoresItProtected() => Ui(() =>
    {
        _service.With(OptionScope.Global, null, WithStoredProxyPassword());
        using var form = Create();
        form.Show();
        Get<TextBox>(form, "_txtProxyPassword").Text = "nueva-s3cret";

        Wait(form.AcceptAsync());

        var saved = _service.Saved.Single().Options;
        saved.ProxyPasswordProtected.Should().NotContain("nueva-s3cret");
        _credentials.GetPassword(saved).Should().Be("nueva-s3cret");
    });

    [Fact]
    public void Accept_WithTheDeleteBoxChecked_RemovesThePassword() => Ui(() =>
    {
        _service.With(OptionScope.Global, null, WithStoredProxyPassword());
        using var form = Create();
        form.Show();
        Get<CheckBox>(form, "_chkClearProxyPassword").Checked = true;

        Wait(form.AcceptAsync());

        _service.Saved.Single().Options.ProxyPasswordProtected.Should().BeNull();
        _service.Saved.Single().Options.ProxyUsername.Should().Be("juan");
    });

    [Fact]
    public void PasswordWithoutUser_WarnsAndFocusesTheUserBox_OnTheConnectionTab() => Ui(() =>
    {
        using var form = Create();
        form.Show();
        Get<ComboBox>(form, "_cboProxy").SelectedIndex = 1;
        Get<TextBox>(form, "_txtProxyPassword").Text = "s3cret";

        Wait(form.AcceptAsync());

        _prompts.Received(1).Warn("Introduce el usuario del proxy o deja la contraseña vacía.", "Opciones globales");
        Get<TabControl>(form, "_tabs").SelectedTab!.Name.Should().Be("_tabConnection");
        form.ActiveControl.Should().BeSameAs(Get<TextBox>(form, "_txtProxyUser"));
        _service.Saved.Should().BeEmpty();
    });

    [Fact]
    public void AMudThatInherits_ShowsTheInheritedUser_AndStartsItsOwnBlockWithTheInheritedPassword() => Ui(() =>
    {
        var stored = WithStoredProxyPassword();
        _service.With(OptionScope.Global, null, stored);
        using var form = CreateMud();
        form.Show();
        Get<TextBox>(form, "_txtProxyUser").Text.Should().Be("juan");
        Get<TextBox>(form, "_txtProxyUser").Enabled.Should().BeFalse();

        Get<CheckBox>(form, "_chkInherit").Checked = false;
        Wait(form.AcceptAsync());

        _service.Saved.Single().Options.Should().Be(stored, "a copy of what it inherited, password included");
    });

    // ───────────────────────────── System dialogs ─────────────────────────────

    [Fact]
    public void Browse_PutsTheChosenFolderInTheBox() => Ui(() =>
    {
        _prompts.PickFolder(Arg.Any<string>(), Arg.Any<string?>()).Returns(@"D:\mis logs");
        using var form = Create();
        form.Show();
        Get<TabControl>(form, "_tabs").SelectedIndex = 1;

        Get<Button>(form, "_btnBrowse").PerformClick();

        Get<TextBox>(form, "_txtLogDirectory").Text.Should().Be(@"D:\mis logs");
        _prompts.Received(1).PickFolder("Carpeta donde se guardan los logs", Arg.Any<string?>());
    });

    [Fact]
    public void Browse_Cancelled_LeavesTheBoxAlone() => Ui(() =>
    {
        _prompts.PickFolder(Arg.Any<string>(), Arg.Any<string?>()).Returns((string?)null);
        _service.With(OptionScope.Global, null, OmnimudOptions.Default with { LogDirectory = @"D:\antes" });
        using var form = Create();
        form.Show();
        Get<TabControl>(form, "_tabs").SelectedIndex = 1;

        Get<Button>(form, "_btnBrowse").PerformClick();

        Get<TextBox>(form, "_txtLogDirectory").Text.Should().Be(@"D:\antes");
    });

    [Fact]
    public void ChangeFont_UsesTheFontDialog_AndUpdatesFamilySizeAndPreview() => Ui(() =>
    {
        _fontPrompt.PickFont(Arg.Any<FontChoice>()).Returns(new FontChoice("Arial", 14.25f));
        using var form = Create();
        form.Show();
        Get<TabControl>(form, "_tabs").SelectedIndex = 4;

        Get<Button>(form, "_btnChangeFont").PerformClick();

        _fontPrompt.Received(1).PickFont(new FontChoice("Consolas", 10f));
        Get<ComboBox>(form, "_cboFont").Text.Should().Be("Arial");
        Get<NumericUpDown>(form, "_nudFontSize").Value.Should().Be(14.25m);
        var preview = Get<TextBox>(form, "_txtPreview");
        preview.Font.FontFamily.Name.Should().Be("Arial");
        preview.Font.SizeInPoints.Should().BeApproximately(14.25f, 0.01f);
        preview.ReadOnly.Should().BeTrue();
        preview.Text.Should().NotBeEmpty();
    });

    [Fact]
    public void Preview_FollowsTheFontControls() => Ui(() =>
    {
        using var form = Create();

        Get<NumericUpDown>(form, "_nudFontSize").Value = 20;

        Get<TextBox>(form, "_txtPreview").Font.SizeInPoints.Should().BeApproximately(20f, 0.01f);
        Get<TextBox>(form, "_txtPreview").Font.FontFamily.Name.Should().Be("Consolas");
    });

    [Fact]
    public void Import_FillsTheControls_UnchecksInherit_AndSavesOnlyOnAccept() => Ui(() =>
    {
        _prompts.PickOpenFile(Arg.Any<string>(), Arg.Any<string>()).Returns("in.omnimud");
        _files.LoadAsync("in.omnimud", Arg.Any<CancellationToken>()).Returns(OmnimudOptions.Default with { Volume = 12, ScreenReader = ScreenReaderMode.Jaws });
        using var form = CreateMud();
        form.Show();
        Get<CheckBox>(form, "_chkInherit").Checked.Should().BeTrue();

        Get<Button>(form, "_btnImport").PerformClick();
        Application.DoEvents();

        Get<CheckBox>(form, "_chkInherit").Checked.Should().BeFalse();
        Get<NumericUpDown>(form, "_nudVolume").Value.Should().Be(12);
        Get<NumericUpDown>(form, "_nudVolume").Enabled.Should().BeTrue();
        Get<ComboBox>(form, "_cboScreenReader").Items.Count.Should().Be(4, "the original duplicated the items of the combos on import");
        Get<ComboBox>(form, "_cboScreenReader").Text.Should().Be("JAWS");
        _service.Saved.Should().BeEmpty();

        Wait(form.AcceptAsync());
        _service.Saved.Single().Options.Volume.Should().Be(12);
    });

    [Fact]
    public void Export_WritesTheValuesOnScreen() => Ui(() =>
    {
        _prompts.PickSaveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>()).Returns("out.omnimud");
        using var form = Create();
        form.Show();
        Get<NumericUpDown>(form, "_nudVolume").Value = 61;

        Get<Button>(form, "_btnExport").PerformClick();
        Application.DoEvents();

        _files.Received(1).SaveAsync("out.omnimud", Arg.Is<OmnimudOptions>(o => o.Volume == 61), Arg.Any<CancellationToken>());
        _service.Saved.Should().BeEmpty();
    });

    // ───────────────────────────── Compatibility and the real thing ─────────────────────────────

    [Fact]
    public void RepositoryConstructor_OpensTheGlobalOptions() => Ui(() =>
    {
        var repository = Substitute.For<IOptionRepository>();
        repository.GetByScope(0, null, Arg.Any<CancellationToken>()).Returns(new List<Omnimud.Data.Entities.OptionEntity>());

        using var form = new FrmOptions(repository);
        Wait(form.InitializeAsync());

        form.Text.Should().Be("Opciones globales");
        form.Controls.Find("_chkInherit", true).Should().BeEmpty();
        AccessibilityAudit.Check(form, isDialog: true).Should().BeEmpty();
    });

    [Fact]
    public void WithTheRealServiceOverSqlite_WhatTheDialogSaves_IsWhatResolveReturns() => Ui(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "omnimud-frmoptions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var factory = new SqliteConnectionFactory(Path.Combine(dir, "omnimud.db"));
            using (var connection = factory.Create())
                MigrationRunner.RunAsync(connection).GetAwaiter().GetResult();
            var service = new OptionsService(new SqliteOptionRepository(factory));
            var exchange = new ExchangeService(factory, service);
            Wait(service.SaveAsync(OptionScope.Global, null, OmnimudOptions.Default with { Volume = 40 }));

            // A MUD that inherits: uncheck, edit, accept.
            using (var form = new FrmOptions(service, OptionScope.Mud, MudId, "Reinos", _prompts, exchange: exchange, fontPrompt: _fontPrompt))
            {
                form.Show();
                Wait(form.InitializeAsync());
                Get<CheckBox>(form, "_chkInherit").Checked.Should().BeTrue();
                Get<NumericUpDown>(form, "_nudVolume").Value.Should().Be(40);

                Get<CheckBox>(form, "_chkInherit").Checked = false;
                Get<NumericUpDown>(form, "_nudHistorySize").Value = 321;
                Get<CheckBox>(form, "_chkUseRepeat").Checked = true;
                Get<TextBox>(form, "_txtRepeatChar").Text = "*";
                Wait(form.AcceptAsync());
                form.DialogResult.Should().Be(DialogResult.OK);
            }

            var expected = OmnimudOptions.Default with { Volume = 40, HistorySize = 321, UseRepeatChar = true, RepeatChar = '*' };
            Resolve(service, MudId, null).Should().Be(expected);
            Resolve(service, MudId, CharacterId).Should().Be(expected, "the character inherits from its MUD");
            Resolve(service, null, null).Should().Be(OmnimudOptions.Default with { Volume = 40 }, "the global block is untouched");
            HasOwn(service, OptionScope.Mud, MudId).Should().BeTrue();

            // The character gets its own block...
            using (var form = new FrmOptions(service, OptionScope.Character, CharacterId, "Aldara", _prompts, MudId, exchange, _fontPrompt))
            {
                form.Show();
                Wait(form.InitializeAsync());
                Get<NumericUpDown>(form, "_nudHistorySize").Value.Should().Be(321, "it shows what it inherits from the MUD");
                Get<CheckBox>(form, "_chkInherit").Checked = false;
                Get<NumericUpDown>(form, "_nudVolume").Value = 5;
                Wait(form.AcceptAsync());
            }
            Resolve(service, MudId, CharacterId).Should().Be(expected with { Volume = 5 });

            // ...and goes back to inherit, which the original client could not do.
            using (var form = new FrmOptions(service, OptionScope.Character, CharacterId, "Aldara", _prompts, MudId, exchange, _fontPrompt))
            {
                form.Show();
                Wait(form.InitializeAsync());
                Get<CheckBox>(form, "_chkInherit").Checked.Should().BeFalse();
                Get<CheckBox>(form, "_chkInherit").Checked = true;
                Wait(form.AcceptAsync());
            }
            HasOwn(service, OptionScope.Character, CharacterId).Should().BeFalse();
            Resolve(service, MudId, CharacterId).Should().Be(expected);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    });

    private static OmnimudOptions Resolve(IOptionsService service, int? mudId, int? characterId)
    {
        var task = service.ResolveAsync(mudId, characterId);
        Wait(task);
        return task.Result;
    }

    private static bool HasOwn(IOptionsService service, OptionScope scope, int? id)
    {
        var task = service.HasOwnOptionsAsync(scope, id);
        Wait(task);
        return task.Result;
    }
}
