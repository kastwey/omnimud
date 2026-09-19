using System.Globalization;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Omnimud.Data;
using Omnimud.Data.Entities;
using Omnimud.Data.Repositories;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Tests.Presenters;

public sealed class MudEditorModelTests
{
    private readonly IMudRepository _muds = Substitute.For<IMudRepository>();
    private readonly IMessageRuleRepository _rules = Substitute.For<IMessageRuleRepository>();

    public MudEditorModelTests()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("es");
        _rules.GetRuleSetsAsync().Returns([
            new MessageRuleSetEntity { Id = 2, Name = "Simauria", IsBuiltIn = true },
            new MessageRuleSetEntity { Id = 1, Name = "Balzhur", IsBuiltIn = true }]);
    }

    private MudEditorModel Valid(MudEntity? existing = null, Func<string, bool>? directoryExists = null) =>
        new(_muds, _rules, existing, directoryExists) { Name = "Reinos", Host = "rlmud.org", Port = 23 };

    private static MudEntity Existing() => new()
    {
        Id = 7, Name = "Reinos", Host = "rlmud.org", Port = 23, UseTls = true, ValidateCertificate = false,
        MessageRuleSetId = 2, SoundDirectory = @"C:\sonidos", Encoding = "iso-8859-1", SaveCommand = "salvar",
        QuitCommand = "abandonar", LoginScript = "%character\n%password", ProcessRule = "legacy", MovementMode = true,
        DefaultCharacterId = 3, CreatedAt = new DateTime(2020, 1, 1), UpdatedAt = new DateTime(2020, 1, 1),
    };

    // ── Validation, field by field ─────────────────────────────────────────

    [Fact]
    public void ValidModel_HasNoError() => Valid().Validate().Should().BeNull();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_IsRequired(string name)
    {
        var model = Valid();
        model.Name = name;

        model.Validate().Should().BeEquivalentTo(new FieldError<MudField>(MudField.Name, Strings.MudEdit_NameRequired));
    }

    [Fact]
    public void Name_CannotBeLongerThanTheLimit()
    {
        var model = Valid();
        model.Name = new string('x', MudEditorModel.MaxNameLength + 1);

        model.Validate()!.Field.Should().Be(MudField.Name);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData("mud example.org", false)]
    public void Host_IsRequired_AndHasNoSpaces(string host, bool missing)
    {
        var model = Valid();
        model.Host = host;

        var error = model.Validate();

        error!.Field.Should().Be(MudField.Host);
        error.Message.Should().Be(missing ? Strings.MudEdit_HostRequired : Strings.MudEdit_HostInvalid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Port_MustBeInRange(int port)
    {
        var model = Valid();
        model.Port = port;

        model.Validate()!.Field.Should().Be(MudField.Port);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(65535)]
    public void Port_Limits_AreValid(int port)
    {
        var model = Valid();
        model.Port = port;

        model.Validate().Should().BeNull();
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("iso-8859-1")]
    [InlineData("windows-1252")]
    [InlineData("ascii")]
    [InlineData("iso-8859-15")]
    [InlineData("  UTF-8  ")]
    public void Encoding_AcceptsTheOfferedOnes_AndAnyOtherThatExists(string encoding)
    {
        var model = Valid();
        model.Encoding = encoding;

        model.Validate().Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("klingon-8")]
    public void Encoding_ThatDoesNotExist_IsRejected_NamingIt(string encoding)
    {
        var model = Valid();
        model.Encoding = encoding;

        var error = model.Validate();

        error!.Field.Should().Be(MudField.Encoding);
        if (encoding.Length > 0) error.Message.Should().Contain(encoding.Trim());
    }

    [Fact]
    public void OfferedEncodings_AreTheFourOfTheSpec_AndAllExist()
    {
        MudEncodings.Common.Should().Equal("utf-8", "iso-8859-1", "windows-1252", "ascii");
        MudEncodings.Common.Should().OnlyContain(e => MudEncodings.IsValid(e));
    }

    [Fact]
    public void SoundDirectory_Empty_MeansDefault_AndIsValid()
    {
        var model = Valid(directoryExists: _ => false);
        model.SoundDirectory = "  ";

        model.Validate().Should().BeNull();
    }

    [Fact]
    public void SoundDirectory_ThatDoesNotExist_IsRejected()
    {
        var model = Valid(directoryExists: _ => false);
        model.SoundDirectory = @"Z:\no\existe";

        model.Validate().Should().BeEquivalentTo(new FieldError<MudField>(MudField.SoundDirectory, Strings.MudEdit_SoundDirectoryMissing));
    }

    [Fact]
    public void ValidateCertificate_IsOnlyEditableOverTls()
    {
        var model = Valid();

        model.CanValidateCertificate.Should().BeFalse();
        model.UseTls = true;
        model.CanValidateCertificate.Should().BeTrue();
    }

    // ── Rule sets ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RuleSetChoices_StartWithNone_ThenTheSetsByName()
    {
        var choices = await Valid().GetRuleSetChoicesAsync();

        choices.Select(c => c.Value).Should().Equal(null, 1, 2);
        choices.Select(c => c.Text).Should().Equal(Strings.MudEdit_RuleSetNone, "Balzhur", "Simauria");
    }

    // ── Saving ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Save_InvalidModel_WritesNothing()
    {
        var model = Valid();
        model.Host = "";

        (await model.SaveAsync())!.Field.Should().Be(MudField.Host);

        await _muds.DidNotReceiveWithAnyArgs().AddAsync(default!);
        model.SavedId.Should().BeNull();
    }

    [Fact]
    public async Task Save_NewMud_WritesEveryField_Trimmed()
    {
        MudEntity? saved = null;
        _muds.AddAsync(Arg.Do<MudEntity>(m => saved = m)).Returns(42);
        var model = new MudEditorModel(_muds, _rules, null, _ => true)
        {
            Name = "  Reinos  ", Host = " rlmud.org ", Port = 5001, UseTls = true, ValidateCertificate = false,
            MessageRuleSetId = 2, SoundDirectory = @" C:\sonidos ", Encoding = " iso-8859-1 ",
            SaveCommand = " salvar ", QuitCommand = " abandonar ", LoginScript = "conectar %character %password",
        };

        (await model.SaveAsync()).Should().BeNull();

        model.SavedId.Should().Be(42);
        saved.Should().BeEquivalentTo(new
        {
            Name = "Reinos", Host = "rlmud.org", Port = 5001, UseTls = true, ValidateCertificate = false,
            MessageRuleSetId = (int?)2, SoundDirectory = @"C:\sonidos", Encoding = "iso-8859-1",
            SaveCommand = "salvar", QuitCommand = "abandonar", LoginScript = "conectar %character %password",
        });
    }

    [Fact]
    public async Task Save_BlankOptionalFields_AreStoredAsNull()
    {
        MudEntity? saved = null;
        _muds.AddAsync(Arg.Do<MudEntity>(m => saved = m)).Returns(1);
        var model = Valid();
        model.SoundDirectory = " ";
        model.SaveCommand = "";
        model.QuitCommand = "  ";
        model.LoginScript = " \r\n ";

        await model.SaveAsync();

        saved!.SoundDirectory.Should().BeNull();
        saved.SaveCommand.Should().BeNull();
        saved.QuitCommand.Should().BeNull();
        saved.LoginScript.Should().BeNull();
        saved.MessageRuleSetId.Should().BeNull();
    }

    [Fact]
    public void Edit_LoadsEveryField()
    {
        var model = new MudEditorModel(_muds, _rules, Existing());

        model.IsNew.Should().BeFalse();
        model.Title.Should().Be("Editar Reinos");
        model.Should().BeEquivalentTo(new
        {
            Name = "Reinos", Host = "rlmud.org", Port = 23, UseTls = true, ValidateCertificate = false,
            MessageRuleSetId = (int?)2, SoundDirectory = @"C:\sonidos", Encoding = "iso-8859-1",
            SaveCommand = "salvar", QuitCommand = "abandonar", LoginScript = "%character\n%password",
        });
    }

    [Fact]
    public async Task Save_Edit_UpdatesTheSameEntity_KeepingWhatTheDialogDoesNotShow()
    {
        var existing = Existing();
        var model = new MudEditorModel(_muds, _rules, existing, _ => true) { };
        model.Name = "Reinos de Leyenda";
        model.MessageRuleSetId = null;

        (await model.SaveAsync()).Should().BeNull();

        await _muds.Received(1).UpdateAsync(existing);
        model.SavedId.Should().Be(7);
        existing.Name.Should().Be("Reinos de Leyenda");
        existing.MessageRuleSetId.Should().BeNull();
        existing.DefaultCharacterId.Should().Be(3);
        existing.MovementMode.Should().BeTrue();
        existing.ProcessRule.Should().Be("legacy");
        existing.CreatedAt.Should().Be(new DateTime(2020, 1, 1));
        existing.UpdatedAt.Should().BeAfter(new DateTime(2020, 1, 1));
    }

    [Fact]
    public async Task Save_DuplicateName_IsAMessageOnTheNameField_NotAnException()
    {
        _muds.AddAsync(Arg.Any<MudEntity>()).ThrowsAsync(new DuplicateEntityException("Mud", "Reinos"));

        var error = await Valid().SaveAsync();

        error.Should().BeEquivalentTo(new FieldError<MudField>(MudField.Name, string.Format(Strings.MudEdit_Duplicate, "Reinos")));
    }

    [Fact]
    public async Task Save_NameThatOnlyDiffersInCase_IsADuplicateToo_AndNothingIsWritten()
    {
        _muds.GetByNameAsync("REINOS").Returns(new MudEntity { Id = 3, Name = "Reinos", Host = "h", Port = 1 });
        var model = Valid();
        model.Name = "REINOS";

        (await model.SaveAsync()).Should().BeEquivalentTo(new FieldError<MudField>(MudField.Name, string.Format(Strings.MudEdit_Duplicate, "REINOS")));

        await _muds.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task Save_Edit_KeepingItsOwnName_IsNotADuplicate()
    {
        var existing = Existing();
        _muds.GetByNameAsync("Reinos").Returns(existing);
        var model = new MudEditorModel(_muds, _rules, existing, _ => true);

        (await model.SaveAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Save_DuplicateName_WhenEditing_LeavesTheEntityAsItWas()
    {
        var existing = Existing();
        _muds.UpdateAsync(Arg.Any<MudEntity>()).ThrowsAsync(new DuplicateEntityException("Mud", "Otro"));
        var model = new MudEditorModel(_muds, _rules, existing, _ => true) { };
        model.Name = "Otro";
        model.Port = 9999;

        (await model.SaveAsync())!.Field.Should().Be(MudField.Name);

        existing.Should().BeEquivalentTo(Existing());
        model.SavedId.Should().BeNull();
    }

    [Theory]
    [InlineData("es", "Añadir MUD")]
    [InlineData("en", "Add MUD")]
    public void Title_IsLocalized(string culture, string expected)
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);

        new MudEditorModel(_muds, _rules).Title.Should().Be(expected);
    }
}
