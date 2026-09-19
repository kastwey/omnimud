using System.Globalization;
using Omnimud.Core.Options;
using Omnimud.Data.Options;
using Omnimud.Data.Repositories;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Tests.Presenters;

public sealed class PersonalInfoTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private readonly SqliteOptionRepository _repository;
    private readonly OptionPersonalInfoStore _store;

    public PersonalInfoTests()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("es");
        _repository = new SqliteOptionRepository(_db.Factory);
        _store = new OptionPersonalInfoStore(_repository);
    }

    public void Dispose() => _db.Dispose();

    // ── Validation ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", "")]
    [InlineData("María López", "")]
    [InlineData("", "maria@example.org")]
    [InlineData("  María  ", "  maria.lopez+mud@correo.example.org  ")]
    [InlineData("X", "a_b-c@sub.dominio.es")]
    public async Task BothFieldsAreOptional_AndWhatIsValidIsSavedTrimmed(string name, string email)
    {
        var model = new PersonalInfoModel(_store) { Name = name, Email = email };

        (await model.SaveAsync()).Should().BeNull();

        (await _store.LoadAsync()).Should().Be(new PersonalInfo(name.Trim(), email.Trim()));
    }

    [Theory]
    [InlineData("maria")]
    [InlineData("maria@")]
    [InlineData("@example.org")]
    [InlineData("maria@example")]
    [InlineData("maria@@example.org")]
    [InlineData("maria lopez@example.org")]
    [InlineData("maria@exam ple.org")]
    [InlineData("maria@example..org")]
    [InlineData("maria@-example.org")]
    [InlineData("maria@example.org, otra@example.org")]
    [InlineData("María <maria@example.org>")]
    [InlineData("maria@example.org?cc=x@example.org")]
    public async Task BadEmail_IsRefused_WithFocusOnTheEmail_AndNothingIsStored(string email)
    {
        var model = new PersonalInfoModel(_store) { Name = "María", Email = email };

        var issue = await model.SaveAsync();

        issue!.Field.Should().Be(nameof(PersonalInfoModel.Email));
        issue.Message.Should().Contain("correo");
        (await _store.LoadAsync()).Should().Be(PersonalInfo.Empty);
    }

    [Fact]
    public void TooLongOrStrangeName_IsRefused()
    {
        new PersonalInfoModel(_store) { Name = new string('a', 256) }.Validate()!.Field.Should().Be(nameof(PersonalInfoModel.Name));
        new PersonalInfoModel(_store) { Name = "María" }.Validate()!.Field.Should().Be(nameof(PersonalInfoModel.Name));
        new PersonalInfoModel(_store) { Name = new string('a', 255) }.Validate().Should().BeNull();
    }

    [Fact]
    public async Task Load_BringsWhatWasStored()
    {
        await _store.SaveAsync(new PersonalInfo("María", "maria@example.org"));
        var model = new PersonalInfoModel(_store);

        await model.LoadAsync();

        (model.Name, model.Email).Should().Be(("María", "maria@example.org"));
    }

    [Fact]
    public async Task EmptyingTheFields_ReallyDeletesTheData()
    {
        await _store.SaveAsync(new PersonalInfo("María", "maria@example.org"));

        await _store.SaveAsync(PersonalInfo.Empty);

        (await _repository.GetByScope(OptionPersonalInfoStore.PersonalInfoScope, null)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("María", "m@example.org", "María <m@example.org>")]
    [InlineData("María", "", "María")]
    [InlineData("", "m@example.org", "m@example.org")]
    [InlineData("", "", "")]
    public void Signature(string name, string email, string expected) => new PersonalInfo(name, email).Signature.Should().Be(expected);

    // ── It stays here ──────────────────────────────────────────────────────

    [Fact]
    public void ItLivesInItsOwnScope_AwayFromTheOptionsAndTheOtherInterfaceState()
    {
        OptionPersonalInfoStore.PersonalInfoScope.Should().Be(101);
        OptionPersonalInfoStore.PersonalInfoScope.Should().NotBe(OptionQuickConnectStore.UiStateScope).And.NotBe(OptionListSortStore.UiStateScope);
        Enum.GetValues<OptionScope>().Cast<int>().Should().NotContain(OptionPersonalInfoStore.PersonalInfoScope);
    }

    [Fact]
    public async Task ItIsNotPartOfTheGlobalOptions_NorMakesGlobalLookConfigured()
    {
        await _store.SaveAsync(new PersonalInfo("María López", "maria@example.org"));
        var service = new OptionsService(_repository);

        (await service.HasOwnOptionsAsync(OptionScope.Global, null)).Should().BeFalse();
        (await service.ResolveAsync(null, null)).Should().Be(new OmnimudOptions());
    }

    [Fact]
    public async Task SavingTheGlobalOptions_DoesNotEraseIt()
    {
        await _store.SaveAsync(new PersonalInfo("María López", "maria@example.org"));

        await new OptionsService(_repository).SaveAsync(OptionScope.Global, null, new OmnimudOptions { Volume = 10 });

        (await _store.LoadAsync()).Should().Be(new PersonalInfo("María López", "maria@example.org"));
    }

    [Fact]
    public async Task ItIsNeverExported_NotInAMud_NotInACharacter_NotInTheOptions()
    {
        await _store.SaveAsync(new PersonalInfo("María López", "maria@example.org"));
        var mud = await _db.AddMudAsync("Reinos");
        var character = await _db.AddCharacterAsync(mud.Id, "Aldara");
        await new OptionsService(_repository).SaveAsync(OptionScope.Global, null, new OmnimudOptions { Volume = 10 });

        var exported = new[]
        {
            await _db.Exchange.ExportMudAsync(mud.Id, includeCharacters: true),
            await _db.Exchange.ExportCharacterAsync(character.Id),
            await _db.Exchange.ExportOptionsAsync(OptionScope.Global, null),
            await _db.Exchange.ExportOptionsAsync(OptionScope.Mud, mud.Id),
        };

        foreach (var file in exported)
        {
            file.Should().NotContain("maria@example.org").And.NotContain("López").And.NotContain("PersonalInfo");
        }
    }
}
