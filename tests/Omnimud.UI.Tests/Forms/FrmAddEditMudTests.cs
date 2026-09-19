using System.Globalization;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Omnimud.Data;
using Omnimud.Data.Entities;
using Omnimud.Data.Repositories;
using Omnimud.UI.Forms;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Tests.Accessibility;
using Omnimud.UI.Tests.Presenters;

namespace Omnimud.UI.Tests.Forms;

public sealed class FrmAddEditMudTests
{
    private readonly IMudRepository _muds = Substitute.For<IMudRepository>();
    private readonly IMessageRuleRepository _rules = Substitute.For<IMessageRuleRepository>();
    private readonly ScriptedPrompts _prompts = new();

    public FrmAddEditMudTests()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("es");
        _rules.GetRuleSetsAsync().Returns([
            new MessageRuleSetEntity { Id = 1, Name = "Balzhur", IsBuiltIn = true },
            new MessageRuleSetEntity { Id = 2, Name = "Simauria", IsBuiltIn = true }]);
    }

    private static MudEntity Existing() => new()
    {
        Id = 7, Name = "Reinos", Host = "rlmud.org", Port = 5001, UseTls = true, ValidateCertificate = false, MessageRuleSetId = 2,
        SoundDirectory = "", Encoding = "iso-8859-15", SaveCommand = "salvar", QuitCommand = "abandonar", LoginScript = "%character\n%password",
    };

    private FrmAddEditMud Create(MudEntity? existing = null)
    {
        var form = new FrmAddEditMud(new MudEditorModel(_muds, _rules, existing, _ => false), _prompts);
        UiPump.Wait(form.LoadAsync());
        return form;
    }

    private static T Get<T>(Form form, string name) where T : Control => (T)form.Controls.Find(name, true).Single();

    [Theory]
    [InlineData("es", false)]
    [InlineData("en", false)]
    [InlineData("es", true)]
    [InlineData("en", true)]
    public void Dialog_PassesTheAccessibilityAudit_InEveryLanguage(string culture, bool editing) => Sta.Run(() =>
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);
        using var form = Create(editing ? Existing() : null);

        AccessibilityAudit.Check(form, isDialog: true).Should().BeEmpty();
    });

    [Fact]
    public void NoFocusableControl_HasAnAccessibleDescription() => Sta.Run(() =>
    {
        using var form = Create();

        form.Controls.Cast<Control>().Where(c => c is not Label)
            .Should().OnlyContain(c => string.IsNullOrEmpty(c.AccessibleDescription), "NVDA reads the description on every focus");
    });

    [Fact]
    public void LoginScriptHelp_IsAVisibleLabel_ExplainingThePlaceholders() => Sta.Run(() =>
    {
        using var form = Create();

        var hint = Get<Label>(form, "_lblLoginHint");
        var script = Get<TextBox>(form, "_txtLoginScript");
        hint.Parent.Should().BeSameAs(form, "the help is a visible label of the dialog, not an accessible description");
        script.AccessibleDescription.Should().BeNullOrEmpty();
        hint.Text.Should().Contain("%character").And.Contain("%password");
        script.Multiline.Should().BeTrue();
        script.AcceptsReturn.Should().BeTrue();
        script.AccessibleName.Should().Be("Script de inicio de sesión");
    });

    [Fact]
    public void NewMud_Defaults() => Sta.Run(() =>
    {
        using var form = Create();

        form.Text.Should().Be("Añadir MUD");
        Get<NumericUpDown>(form, "_nudPort").Value.Should().Be(23);
        Get<ComboBox>(form, "_cboEncoding").Text.Should().Be("utf-8");
        Get<ComboBox>(form, "_cboEncoding").Items.Cast<string>().Should().Equal("utf-8", "iso-8859-1", "windows-1252", "ascii");
        Get<ComboBox>(form, "_cboEncoding").DropDownStyle.Should().Be(ComboBoxStyle.DropDown, "other encodings can be typed");
        Get<ComboBox>(form, "_cboRuleSet").Items.Cast<object>().Select(i => i.ToString()).Should().Equal("(Ninguno)", "Balzhur", "Simauria");
        Get<ComboBox>(form, "_cboRuleSet").SelectedIndex.Should().Be(0);
    });

    [Fact]
    public void ValidateCertificate_IsOnlyEnabledWithTls() => Sta.Run(() =>
    {
        using var form = Create();
        var tls = Get<CheckBox>(form, "_chkTls");
        var validate = Get<CheckBox>(form, "_chkValidate");

        validate.Enabled.Should().BeFalse();
        validate.Checked.Should().BeTrue("validating is the safe default");
        tls.Checked = true;
        validate.Enabled.Should().BeTrue();
        tls.Checked = false;
        validate.Enabled.Should().BeFalse();
    });

    [Fact]
    public void Editing_LoadsEveryField_WithTheTextSelected() => Sta.Run(() =>
    {
        using var form = Create(Existing());

        form.Text.Should().Be("Editar Reinos");
        Get<TextBox>(form, "_txtName").Text.Should().Be("Reinos");
        Get<TextBox>(form, "_txtName").SelectionLength.Should().Be(6);
        Get<TextBox>(form, "_txtHost").Text.Should().Be("rlmud.org");
        Get<NumericUpDown>(form, "_nudPort").Value.Should().Be(5001);
        Get<CheckBox>(form, "_chkTls").Checked.Should().BeTrue();
        Get<CheckBox>(form, "_chkValidate").Checked.Should().BeFalse();
        Get<CheckBox>(form, "_chkValidate").Enabled.Should().BeTrue();
        Get<ComboBox>(form, "_cboEncoding").Text.Should().Be("iso-8859-15");
        Get<ComboBox>(form, "_cboRuleSet").SelectedItem!.ToString().Should().Be("Simauria");
        Get<TextBox>(form, "_txtSaveCommand").Text.Should().Be("salvar");
        Get<TextBox>(form, "_txtQuitCommand").Text.Should().Be("abandonar");
        Get<TextBox>(form, "_txtLoginScript").Lines.Should().Equal("%character", "%password");
    });

    [Fact]
    public void Accept_SavesWhatIsOnScreen_AndCloses() => Sta.Run(() =>
    {
        MudEntity? saved = null;
        _muds.AddAsync(Arg.Do<MudEntity>(m => saved = m)).Returns(5);
        using var form = Create();
        Get<TextBox>(form, "_txtName").Text = "Reinos";
        Get<TextBox>(form, "_txtHost").Text = "rlmud.org";
        Get<NumericUpDown>(form, "_nudPort").Value = 5001;
        Get<CheckBox>(form, "_chkTls").Checked = true;
        Get<CheckBox>(form, "_chkValidate").Checked = false;
        Get<ComboBox>(form, "_cboEncoding").Text = "windows-1252";
        Get<ComboBox>(form, "_cboRuleSet").SelectedIndex = 2;
        Get<TextBox>(form, "_txtLoginScript").Text = "conectar %character %password";

        UiPump.Wait(form.AcceptAsync()).Should().BeTrue();

        form.DialogResult.Should().Be(DialogResult.OK);
        saved.Should().BeEquivalentTo(new
        {
            Name = "Reinos", Host = "rlmud.org", Port = 5001, UseTls = true, ValidateCertificate = false, Encoding = "windows-1252",
            MessageRuleSetId = (int?)2, SoundDirectory = (string?)null, LoginScript = "conectar %character %password",
        });
        _prompts.Warnings.Should().BeEmpty();
    });

    [Theory]
    [InlineData("_txtName", "", "_txtName")]
    [InlineData("_txtHost", "", "_txtHost")]
    [InlineData("_cboEncoding", "klingon", "_cboEncoding")]
    [InlineData("_txtSound", @"Z:\no\existe", "_txtSound")]
    public void Accept_WithAProblem_ShowsTheMessage_MovesTheFocusToTheField_AndStaysOpen(string control, string value, string focused) => Sta.Run(() =>
    {
        using var form = Create();
        Get<TextBox>(form, "_txtName").Text = "Reinos";
        Get<TextBox>(form, "_txtHost").Text = "rlmud.org";
        Get<Control>(form, control).Text = value;

        UiPump.Wait(form.AcceptAsync()).Should().BeFalse();

        _prompts.Warnings.Should().ContainSingle();
        form.ActiveControl.Should().BeSameAs(Get<Control>(form, focused));
        form.DialogResult.Should().Be(DialogResult.None);
        _muds.DidNotReceiveWithAnyArgs().AddAsync(default!);
    });

    [Fact]
    public void Accept_DuplicateName_IsAMessageAndFocusOnTheName_NotAnException() => Sta.Run(() =>
    {
        _muds.AddAsync(Arg.Any<MudEntity>()).ThrowsAsync(new DuplicateEntityException("Mud", "Reinos"));
        using var form = Create();
        Get<TextBox>(form, "_txtName").Text = "Reinos";
        Get<TextBox>(form, "_txtHost").Text = "rlmud.org";

        UiPump.Wait(form.AcceptAsync()).Should().BeFalse();

        _prompts.Warnings.Should().Equal(string.Format(Strings.MudEdit_Duplicate, "Reinos"));
        form.ActiveControl.Should().BeSameAs(Get<TextBox>(form, "_txtName"));
        form.DialogResult.Should().Be(DialogResult.None);
    });

    [Fact]
    public void Browse_PutsTheChosenFolderInTheBox_AndCancellingLeavesIt() => Sta.Run(() =>
    {
        using var form = Create();
        var box = Get<TextBox>(form, "_txtSound");

        _prompts.Folder = null;
        form.BrowseSoundFolder();
        box.Text.Should().BeEmpty();

        _prompts.Folder = @"C:\sonidos\reinos";
        form.BrowseSoundFolder();
        box.Text.Should().Be(@"C:\sonidos\reinos");
    });

    [Theory]
    [InlineData("_txtName")]
    [InlineData("_txtHost")]
    [InlineData("_txtSound")]
    [InlineData("_txtSaveCommand")]
    [InlineData("_txtQuitCommand")]
    public void SingleLineBoxes_SelectAllTheirText_WhenTheyGetTheFocus(string name) => Sta.Run(() =>
    {
        using var form = Create(Existing());
        var box = (SelectAllTextBox)Get<TextBox>(form, name);
        box.Text = "contenido";
        box.Select(2, 0);

        box.SimulateEnter();

        (box.SelectionStart, box.SelectionLength).Should().Be((0, "contenido".Length));
    });

    [Fact]
    public void TheMultilineLoginScript_KeepsItsCaret_WhenItGetsTheFocus() => Sta.Run(() =>
    {
        using var form = Create(Existing());
        var box = (SelectAllTextBox)Get<TextBox>(form, "_txtLoginScript");
        box.Select(3, 0);

        box.SimulateEnter();

        box.SelectionLength.Should().Be(0, "selecting a whole script would make one key press erase it");
    });

    [Fact]
    public void Escape_Cancels() => Sta.Run(() =>
    {
        using var form = Create();

        ((Button)form.CancelButton!).DialogResult.Should().Be(DialogResult.Cancel);
        form.AcceptButton.Should().BeSameAs(Get<Button>(form, "_btnOk"));
    });
}
