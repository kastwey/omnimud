using System.Globalization;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Omnimud.Data;
using Omnimud.Data.Entities;
using Omnimud.Data.Repositories;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Tests.Presenters;

public sealed class CharacterEditorModelTests
{
    private readonly ICharacterRepository _characters = Substitute.For<ICharacterRepository>();
    private readonly IMudRepository _muds = Substitute.For<IMudRepository>();
    private readonly FakeProtector _protector = new();

    public CharacterEditorModelTests()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("es");
        _muds.GetAllAsync().Returns([
            new MudEntity { Id = 2, Name = "Simauria", Host = "h", Port = 1 },
            new MudEntity { Id = 1, Name = "Balzhur", Host = "h", Port = 1 }]);
    }

    private CharacterEditorModel New(int? mudId = 1) => new(_characters, _muds, _protector, null, mudId);

    private CharacterEntity Stored(bool withPassword = true) => new()
    {
        Id = 9, MudId = 1, Name = "Aldara", IsDefault = true, MovementMode = true,
        EncryptedPassword = withPassword ? _protector.Protect("secreta") : null,
        CreatedAt = new DateTime(2020, 1, 1), UpdatedAt = new DateTime(2020, 1, 1),
    };

    private CharacterEditorModel Edit(CharacterEntity existing) => new(_characters, _muds, _protector, existing);

    // ── Validation ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Name_IsRequired(string name)
    {
        var model = New();
        model.Name = name;
        model.Password = "x";

        model.Validate().Should().BeEquivalentTo(new FieldError<CharacterField>(CharacterField.Name, Strings.CharEdit_NameRequired));
    }

    [Fact]
    public void Name_CannotBeLongerThanTheLimit()
    {
        var model = New();
        model.Name = new string('x', CharacterEditorModel.MaxNameLength + 1);
        model.Password = "x";

        model.Validate()!.Field.Should().Be(CharacterField.Name);
    }

    [Fact]
    public void NewCharacter_RemembersThePasswordByDefault_SoItMustBeTypedOrTheBoxUnchecked()
    {
        var model = New();
        model.Name = "Aldara";

        model.RememberPassword.Should().BeTrue();
        model.Validate().Should().BeEquivalentTo(new FieldError<CharacterField>(CharacterField.Password, Strings.CharEdit_PasswordRequired));

        model.RememberPassword = false;
        model.Validate().Should().BeNull();
    }

    [Fact]
    public void WithoutMudContext_TheMudMustBeChosen()
    {
        var model = New(mudId: null);
        model.Name = "Aldara";
        model.RememberPassword = false;

        model.CanChooseMud.Should().BeTrue();
        model.Validate().Should().BeEquivalentTo(new FieldError<CharacterField>(CharacterField.Mud, Strings.CharEdit_MudRequired));

        model.MudId = 2;
        model.Validate().Should().BeNull();
    }

    [Fact]
    public async Task MudChoices_AreSortedByName()
    {
        var choices = await New().GetMudChoicesAsync();

        choices.Select(c => c.Text).Should().Equal("Balzhur", "Simauria");
        choices.Select(c => c.Value).Should().Equal(1, 2);
    }

    [Fact]
    public void AnExistingCharacter_CannotChangeMud()
    {
        var model = Edit(Stored());

        model.CanChooseMud.Should().BeFalse();
        model.MudId.Should().Be(1);
    }

    // ── Password: never shown, keep, replace, forget ───────────────────────

    [Fact]
    public void Editing_NeverExposesTheStoredPassword()
    {
        var model = Edit(Stored());

        model.Password.Should().BeEmpty("the stored password is never decrypted into the dialog");
        model.HasStoredPassword.Should().BeTrue();
        model.RememberPassword.Should().BeTrue();
        model.PasswordHint.Should().Be(Strings.CharEdit_HintStored);
    }

    [Fact]
    public void Editing_WithoutStoredPassword_StartsUnchecked()
    {
        var model = Edit(Stored(withPassword: false));

        model.RememberPassword.Should().BeFalse();
        model.CanEditPassword.Should().BeFalse();
        model.PasswordHint.Should().Be(Strings.CharEdit_HintNotRemembered);
        model.Validate().Should().BeNull();
    }

    [Fact]
    public void Hint_FollowsTheCheckBox()
    {
        var model = New();
        model.PasswordHint.Should().Be(Strings.CharEdit_HintNew);

        model.RememberPassword = false;
        model.PasswordHint.Should().Be(Strings.CharEdit_HintNotRemembered);
    }

    [Fact]
    public async Task Save_New_ProtectsThePassword_NeverStoresPlaintext()
    {
        CharacterEntity? saved = null;
        _characters.AddAsync(Arg.Do<CharacterEntity>(c => saved = c)).Returns(33);
        var model = New(mudId: 2);
        model.Name = "  Aldara ";
        model.Password = "secreta";

        (await model.SaveAsync()).Should().BeNull();

        model.SavedId.Should().Be(33);
        saved!.Name.Should().Be("Aldara");
        saved.MudId.Should().Be(2);
        saved.EncryptedPassword.Should().Equal(_protector.Protect("secreta"));
        System.Text.Encoding.UTF8.GetString(saved.EncryptedPassword!).Should().NotBe("secreta");
    }

    [Fact]
    public async Task Save_New_Unchecked_StoresNoPassword_EvenIfSomethingWasTyped()
    {
        CharacterEntity? saved = null;
        _characters.AddAsync(Arg.Do<CharacterEntity>(c => saved = c)).Returns(1);
        var model = New();
        model.Name = "Aldara";
        model.Password = "secreta";
        model.RememberPassword = false;

        await model.SaveAsync();

        saved!.EncryptedPassword.Should().BeNull();
    }

    [Fact]
    public async Task Save_Edit_BlankPassword_KeepsTheStoredOne()
    {
        var existing = Stored();
        var before = existing.EncryptedPassword;
        var model = Edit(existing);
        model.Name = "Aldara la Gris";

        (await model.SaveAsync()).Should().BeNull();

        await _characters.Received(1).UpdateAsync(existing);
        existing.Name.Should().Be("Aldara la Gris");
        existing.EncryptedPassword.Should().Equal(before);
        existing.IsDefault.Should().BeTrue();
        existing.MovementMode.Should().BeTrue();
        model.SavedId.Should().Be(9);
    }

    [Fact]
    public async Task Save_Edit_NewPassword_ReplacesTheStoredOne()
    {
        var existing = Stored();
        var model = Edit(existing);
        model.Password = "nueva";

        await model.SaveAsync();

        existing.EncryptedPassword.Should().Equal(_protector.Protect("nueva"));
    }

    [Fact]
    public async Task Save_Edit_UncheckingRemember_ForgetsTheStoredPassword()
    {
        var existing = Stored();
        var model = Edit(existing);
        model.RememberPassword = false;

        (await model.SaveAsync()).Should().BeNull();

        existing.EncryptedPassword.Should().BeNull("unchecking Remember password deletes the saved one");
        await _characters.Received(1).UpdateAsync(existing);
    }

    // ── Duplicates ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Save_DuplicateName_IsAMessageOnTheNameField_NotAnException()
    {
        _characters.AddAsync(Arg.Any<CharacterEntity>()).ThrowsAsync(new DuplicateEntityException("Character", "Aldara"));
        var model = New();
        model.Name = "Aldara";
        model.RememberPassword = false;

        var error = await model.SaveAsync();

        error.Should().BeEquivalentTo(new FieldError<CharacterField>(CharacterField.Name, string.Format(Strings.CharEdit_Duplicate, "Aldara")));
        model.SavedId.Should().BeNull();
    }

    [Fact]
    public async Task Save_NameThatOnlyDiffersInCase_IsADuplicateToo_ButItsOwnNameIsNot()
    {
        var existing = Stored();
        _characters.GetByMudAsync(1).Returns([existing, new CharacterEntity { Id = 10, MudId = 1, Name = "Borin" }]);

        var other = New();
        other.Name = "ALDARA";
        other.RememberPassword = false;
        (await other.SaveAsync())!.Field.Should().Be(CharacterField.Name);
        await _characters.DidNotReceiveWithAnyArgs().AddAsync(default!);

        var same = Edit(existing);
        same.Name = "aldara";
        (await same.SaveAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Save_DuplicateName_WhenEditing_LeavesTheEntityAsItWas()
    {
        var existing = Stored();
        var password = existing.EncryptedPassword;
        _characters.UpdateAsync(Arg.Any<CharacterEntity>()).ThrowsAsync(new DuplicateEntityException("Character", "Otro"));
        var model = Edit(existing);
        model.Name = "Otro";
        model.RememberPassword = false;

        (await model.SaveAsync())!.Field.Should().Be(CharacterField.Name);

        existing.Name.Should().Be("Aldara");
        existing.EncryptedPassword.Should().Equal(password);
        existing.UpdatedAt.Should().Be(new DateTime(2020, 1, 1));
    }

    [Fact]
    public async Task Save_Invalid_WritesNothing()
    {
        var model = New();

        (await model.SaveAsync()).Should().NotBeNull();

        await _characters.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Theory]
    [InlineData("es", "Añadir personaje", "Editar Aldara")]
    [InlineData("en", "Add character", "Edit Aldara")]
    public void Titles_AreLocalized(string culture, string add, string edit)
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);

        New().Title.Should().Be(add);
        Edit(Stored()).Title.Should().Be(edit);
    }
}
