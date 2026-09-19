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

public sealed class FrmAddEditCharacterTests
{
    private const string Secret = "clave-secreta";

    private readonly ICharacterRepository _characters = Substitute.For<ICharacterRepository>();
    private readonly IMudRepository _muds = Substitute.For<IMudRepository>();
    private readonly FakeProtector _protector = new();
    private readonly ScriptedPrompts _prompts = new();

    public FrmAddEditCharacterTests()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("es");
        _muds.GetAllAsync().Returns([
            new MudEntity { Id = 1, Name = "Balzhur", Host = "h", Port = 1 },
            new MudEntity { Id = 2, Name = "Simauria", Host = "h", Port = 1 }]);
    }

    private CharacterEntity Stored() => new() { Id = 9, MudId = 2, Name = "Aldara", EncryptedPassword = _protector.Protect(Secret) };

    private FrmAddEditCharacter Create(CharacterEntity? existing = null, int? mudId = 1)
    {
        var form = new FrmAddEditCharacter(new CharacterEditorModel(_characters, _muds, _protector, existing, mudId), _prompts);
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
        using var form = Create(editing ? Stored() : null);

        AccessibilityAudit.Check(form, isDialog: true).Should().BeEmpty();
    });

    [Fact]
    public void ThePassword_IsNeverShown_NotEvenWhenEditing() => Sta.Run(() =>
    {
        using var form = Create(Stored());
        var box = Get<TextBox>(form, "_txtPassword");

        box.UseSystemPasswordChar.Should().BeTrue();
        box.Text.Should().BeEmpty();
        form.Controls.Cast<Control>().Should().OnlyContain(c => !c.Text.Contains(Secret) && !(c.AccessibleDescription ?? "").Contains(Secret));
        Get<Label>(form, "_lblPasswordHint").Text.Should().Be(Strings.CharEdit_HintStored);
        Get<CheckBox>(form, "_chkRemember").Checked.Should().BeTrue();
    });

    [Fact]
    public void UncheckingRemember_DisablesAndEmptiesThePasswordBox_AndTheHintSaysItWillBeDeleted() => Sta.Run(() =>
    {
        using var form = Create(Stored());
        var box = Get<TextBox>(form, "_txtPassword");
        box.Text = "a medias";

        Get<CheckBox>(form, "_chkRemember").Checked = false;

        box.Enabled.Should().BeFalse();
        box.Text.Should().BeEmpty();
        Get<Label>(form, "_lblPassword").Enabled.Should().BeFalse();
        Get<Label>(form, "_lblPasswordHint").Text.Should().Be(Strings.CharEdit_HintNotRemembered);
    });

    [Fact]
    public void Accept_UncheckedRemember_DeletesTheStoredPassword() => Sta.Run(() =>
    {
        var existing = Stored();
        using var form = Create(existing);
        Get<CheckBox>(form, "_chkRemember").Checked = false;

        UiPump.Wait(form.AcceptAsync()).Should().BeTrue();

        existing.EncryptedPassword.Should().BeNull();
        _characters.Received(1).UpdateAsync(existing);
        form.DialogResult.Should().Be(DialogResult.OK);
    });

    [Fact]
    public void Accept_LeavingThePasswordEmpty_KeepsTheStoredOne() => Sta.Run(() =>
    {
        var existing = Stored();
        using var form = Create(existing);
        Get<TextBox>(form, "_txtName").Text = "Aldara II";

        UiPump.Wait(form.AcceptAsync()).Should().BeTrue();

        existing.Name.Should().Be("Aldara II");
        _protector.Unprotect(existing.EncryptedPassword!).Should().Be(Secret);
    });

    [Fact]
    public void MudCombo_IsPreselectedFromTheContext_AndLockedWhenEditing() => Sta.Run(() =>
    {
        using (var create = Create(mudId: 2))
        {
            var combo = Get<ComboBox>(create, "_cboMud");
            combo.Enabled.Should().BeTrue();
            combo.DropDownStyle.Should().Be(ComboBoxStyle.DropDownList);
            combo.Items.Cast<object>().Select(i => i.ToString()).Should().Equal("Balzhur", "Simauria");
            combo.SelectedItem!.ToString().Should().Be("Simauria");
        }

        using var edit = Create(Stored());
        Get<ComboBox>(edit, "_cboMud").Enabled.Should().BeFalse();
        Get<ComboBox>(edit, "_cboMud").SelectedItem!.ToString().Should().Be("Simauria");
    });

    [Fact]
    public void CreatedWithoutMudContext_TheMudMustBeChosen_AndIsSaved() => Sta.Run(() =>
    {
        CharacterEntity? saved = null;
        _characters.AddAsync(Arg.Do<CharacterEntity>(c => saved = c)).Returns(3);
        using var form = Create(mudId: null);
        Get<TextBox>(form, "_txtName").Text = "Eva";
        Get<CheckBox>(form, "_chkRemember").Checked = false;

        UiPump.Wait(form.AcceptAsync()).Should().BeFalse();
        _prompts.Warnings.Should().Equal(Strings.CharEdit_MudRequired);
        form.ActiveControl.Should().BeSameAs(Get<ComboBox>(form, "_cboMud"));

        Get<ComboBox>(form, "_cboMud").SelectedIndex = 1;
        UiPump.Wait(form.AcceptAsync()).Should().BeTrue();
        saved!.MudId.Should().Be(2);
    });

    [Theory]
    [InlineData("_txtName")]
    [InlineData("_txtPassword")]
    public void TextBoxes_SelectAllTheirText_WhenTheyGetTheFocus(string name) => Sta.Run(() =>
    {
        using var form = Create();
        var box = (SelectAllTextBox)Get<TextBox>(form, name);
        box.Text = "contenido";
        box.Select(2, 0);

        box.SimulateEnter();

        (box.SelectionStart, box.SelectionLength).Should().Be((0, "contenido".Length));
    });

    [Fact]
    public void Accept_WithoutName_FocusesTheName() => Sta.Run(() =>
    {
        using var form = Create();

        UiPump.Wait(form.AcceptAsync()).Should().BeFalse();

        _prompts.Warnings.Should().Equal(Strings.CharEdit_NameRequired);
        form.ActiveControl.Should().BeSameAs(Get<TextBox>(form, "_txtName"));
        form.DialogResult.Should().Be(DialogResult.None);
    });

    [Fact]
    public void Accept_RememberWithoutPassword_FocusesThePassword() => Sta.Run(() =>
    {
        using var form = Create();
        Get<TextBox>(form, "_txtName").Text = "Eva";

        UiPump.Wait(form.AcceptAsync()).Should().BeFalse();

        _prompts.Warnings.Should().Equal(Strings.CharEdit_PasswordRequired);
        form.ActiveControl.Should().BeSameAs(Get<TextBox>(form, "_txtPassword"));
    });

    [Fact]
    public void Accept_DuplicateName_IsAMessageAndFocusOnTheName_NotAnException() => Sta.Run(() =>
    {
        _characters.AddAsync(Arg.Any<CharacterEntity>()).ThrowsAsync(new DuplicateEntityException("Character", "Eva"));
        using var form = Create();
        Get<TextBox>(form, "_txtName").Text = "Eva";
        Get<TextBox>(form, "_txtPassword").Text = "x";

        UiPump.Wait(form.AcceptAsync()).Should().BeFalse();

        _prompts.Warnings.Should().Equal(string.Format(Strings.CharEdit_Duplicate, "Eva"));
        form.ActiveControl.Should().BeSameAs(Get<TextBox>(form, "_txtName"));
        form.DialogResult.Should().Be(DialogResult.None);
    });
}
