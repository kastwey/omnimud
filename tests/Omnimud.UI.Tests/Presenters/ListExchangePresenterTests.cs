using Omnimud.Data.Entities;
using Omnimud.Data.Exchange;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Tests.Presenters;

/// <summary>Export, import and copy from another character, end to end over a real SQLite file.</summary>
public sealed class ListExchangePresenterTests : IDisposable
{
    private readonly ListsDatabase _db = new();
    private readonly RecordingPrompts _prompts = new();

    public void Dispose() => _db.Dispose();

    private ListExchangePresenter Sut(ExchangeParts part, int characterId, IImportConflictResolver? conflicts = null) =>
        new(_db.Exchange, _prompts, conflicts ?? new FixedConflictResolver(ImportDecision.Skip), part, characterId, "Personaje");

    private Task AddAlias(int characterId, string command, string action) =>
        _db.Aliases.AddAsync(new AliasEntity { CharacterId = characterId, Command = command, Action = action });

    [Fact]
    public void Constructor_RejectsAnythingThatIsNotOneList()
    {
        var act = () => Sut(ExchangeParts.All, _db.CharacterId);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Export_Cancelled_WritesNothing()
    {
        _prompts.SaveFile = null;
        (await Sut(ExchangeParts.Aliases, _db.CharacterId).ExportAsync()).Should().BeFalse();
        _prompts.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Aliases_ExportThenImportIntoAnotherCharacter_WithoutConflicts()
    {
        await AddAlias(_db.CharacterId, "k", "matar");
        await AddAlias(_db.CharacterId, "b", "beber");
        _prompts.SaveFile = _prompts.OpenFile = _db.FilePath("alias.omnimud");

        (await Sut(ExchangeParts.Aliases, _db.CharacterId).ExportAsync()).Should().BeTrue();
        File.Exists(_prompts.SaveFile).Should().BeTrue();

        var resolver = new FixedConflictResolver(ImportDecision.Overwrite);
        (await Sut(ExchangeParts.Aliases, _db.SecondCharacterId, resolver).ImportFromFileAsync()).Should().BeTrue();

        resolver.Calls.Should().Be(0);
        (await _db.Aliases.GetByCharacterAsync(_db.SecondCharacterId)).Select(a => a.Command).Should().BeEquivalentTo("k", "b");
        _prompts.Infos.Should().HaveCount(2); // exported + summary
    }

    [Fact]
    public async Task Aliases_ImportWithConflicts_Overwrite()
    {
        await AddAlias(_db.CharacterId, "k", "matar orco");
        await AddAlias(_db.CharacterId, "n", "nuevo");
        await AddAlias(_db.SecondCharacterId, "k", "matar troll");
        _prompts.SaveFile = _prompts.OpenFile = _db.FilePath("alias.omnimud");
        await Sut(ExchangeParts.Aliases, _db.CharacterId).ExportAsync();

        var resolver = new FixedConflictResolver(ImportDecision.Overwrite);
        (await Sut(ExchangeParts.Aliases, _db.SecondCharacterId, resolver).ImportFromFileAsync()).Should().BeTrue();

        resolver.LastConflicts.Select(c => c.Name).Should().Equal("k");
        var stored = await _db.Aliases.GetByCharacterAsync(_db.SecondCharacterId);
        stored.Single(a => a.Command == "k").Action.Should().Be("matar orco");
        stored.Should().HaveCount(2);
    }

    [Fact]
    public async Task Aliases_ImportWithConflicts_Skip_KeepsWhatWasThere_ButAddsTheNew()
    {
        await AddAlias(_db.CharacterId, "k", "matar orco");
        await AddAlias(_db.CharacterId, "n", "nuevo");
        await AddAlias(_db.SecondCharacterId, "k", "matar troll");
        _prompts.SaveFile = _prompts.OpenFile = _db.FilePath("alias.omnimud");
        await Sut(ExchangeParts.Aliases, _db.CharacterId).ExportAsync();

        (await Sut(ExchangeParts.Aliases, _db.SecondCharacterId, new FixedConflictResolver(ImportDecision.Skip)).ImportFromFileAsync()).Should().BeTrue();

        var stored = await _db.Aliases.GetByCharacterAsync(_db.SecondCharacterId);
        stored.Single(a => a.Command == "k").Action.Should().Be("matar troll");
        stored.Select(a => a.Command).Should().Contain("n");
    }

    [Fact]
    public async Task Import_ConflictsDialogCancelled_WritesNothing()
    {
        await AddAlias(_db.CharacterId, "k", "matar orco");
        await AddAlias(_db.CharacterId, "n", "nuevo");
        await AddAlias(_db.SecondCharacterId, "k", "matar troll");
        _prompts.SaveFile = _prompts.OpenFile = _db.FilePath("alias.omnimud");
        await Sut(ExchangeParts.Aliases, _db.CharacterId).ExportAsync();

        (await Sut(ExchangeParts.Aliases, _db.SecondCharacterId, new FixedConflictResolver(null)).ImportFromFileAsync()).Should().BeFalse();

        (await _db.Aliases.GetByCharacterAsync(_db.SecondCharacterId)).Should().ContainSingle().Which.Action.Should().Be("matar troll");
    }

    [Fact]
    public async Task Import_Cancelled_AtTheFilePicker()
    {
        _prompts.OpenFile = null;
        (await Sut(ExchangeParts.Aliases, _db.CharacterId).ImportFromFileAsync()).Should().BeFalse();
        _prompts.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Import_FileOfAnotherKind_IsRefusedWithAMessage()
    {
        await _db.Triggers.AddAsync(_db.NewTrigger(_db.CharacterId, "hambre"));
        _prompts.SaveFile = _prompts.OpenFile = _db.FilePath("triggers.omnimud");
        await Sut(ExchangeParts.Triggers, _db.CharacterId).ExportAsync();

        (await Sut(ExchangeParts.Aliases, _db.SecondCharacterId).ImportFromFileAsync()).Should().BeFalse();

        _prompts.Warnings.Should().Equal(string.Format(Strings.Exch_WrongKind, Strings.Exch_ListAliases));
        (await _db.Triggers.GetByCharacterAsync(_db.SecondCharacterId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Import_CorruptFile_IsAnErrorMessage_NotAnException()
    {
        _prompts.OpenFile = _db.FilePath("roto.omnimud");
        await File.WriteAllTextAsync(_prompts.OpenFile, "{ this is not json");

        var act = () => Sut(ExchangeParts.Paths, _db.CharacterId).ImportFromFileAsync();

        (await act.Should().NotThrowAsync()).Which.Should().BeFalse();
        _prompts.Errors.Should().ContainSingle();
    }

    [Fact]
    public async Task Import_MissingFile_IsAnErrorMessage()
    {
        (await Sut(ExchangeParts.Paths, _db.CharacterId).ImportFileAsync(_db.FilePath("no-existe.omnimud"))).Should().BeFalse();
        _prompts.Errors.Should().ContainSingle();
    }

    [Fact]
    public async Task Triggers_RoundTrip_KeepsTheNewFields()
    {
        var trigger = _db.NewTrigger(_db.CharacterId, "bloque", "^Salidas", "om.send('x')");
        trigger.ActionType = 2;
        trigger.PatternType = 1;
        trigger.Multiline = true;
        trigger.Priority = 70;
        await _db.Triggers.AddAsync(trigger);
        _prompts.SaveFile = _prompts.OpenFile = _db.FilePath("t.omnimud");

        await Sut(ExchangeParts.Triggers, _db.CharacterId).ExportAsync();
        (await Sut(ExchangeParts.Triggers, _db.ThirdCharacterId).ImportFromFileAsync()).Should().BeTrue();

        var copy = (await _db.Triggers.GetByCharacterAsync(_db.ThirdCharacterId)).Single();
        copy.Should().BeEquivalentTo(trigger, o => o.Including(t => t.Name).Including(t => t.Pattern).Including(t => t.PatternType)
            .Including(t => t.Action).Including(t => t.ActionType).Including(t => t.Multiline).Including(t => t.Priority));
        copy.Id.Should().NotBe(trigger.Id);
    }

    [Fact]
    public async Task Paths_CopyFromAnotherCharacter_WithAndWithoutConflict()
    {
        await _db.Paths.AddAsync(new PathEntity { CharacterId = _db.ThirdCharacterId, Name = "plaza", Path = "3n" });
        await _db.Paths.AddAsync(new PathEntity { CharacterId = _db.ThirdCharacterId, Name = "puerto", Path = "4s" });
        await _db.Paths.AddAsync(new PathEntity { CharacterId = _db.CharacterId, Name = "plaza", Path = "e" });
        await AddAlias(_db.ThirdCharacterId, "k", "no debe copiarse");
        var source = new CharacterChoice(_db.ThirdCharacterId, "Aragorn", "Otro");

        var resolver = new FixedConflictResolver(ImportDecision.Overwrite);
        (await Sut(ExchangeParts.Paths, _db.CharacterId, resolver).ImportFromCharacterAsync(source)).Should().BeTrue();

        resolver.LastConflicts.Select(c => c.Name).Should().Equal("plaza");
        var stored = await _db.Paths.GetByCharacterAsync(_db.CharacterId);
        stored.Should().HaveCount(2);
        stored.Single(p => p.Name == "plaza").Path.Should().Be("3n");
        (await _db.Aliases.GetByCharacterAsync(_db.CharacterId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Copy_FromACharacterWithNothing_SaysSo()
    {
        var source = new CharacterChoice(_db.ThirdCharacterId, "Aragorn", "Otro");
        (await Sut(ExchangeParts.Triggers, _db.CharacterId).ImportFromCharacterAsync(source)).Should().BeFalse();
        _prompts.Infos.Should().ContainSingle();
    }

    [Fact]
    public async Task Copy_FromItself_DoesNothing()
    {
        var self = new CharacterChoice(_db.CharacterId, "Gandalf", "Reinos");
        (await Sut(ExchangeParts.Triggers, _db.CharacterId).ImportFromCharacterAsync(self)).Should().BeFalse();
    }

    [Fact]
    public async Task LoadOtherCharacters_ListsEveryoneElse_WithTheirMud_SortedByMud()
    {
        var choices = await ListExchangePresenter.LoadOtherCharactersAsync(_db.Characters, _db.Muds, _db.CharacterId);

        choices.Select(c => (c.Name, c.MudName)).Should().Equal(("Aragorn", "Otro"), ("Frodo", "Reinos"));
        choices[0].Display.Should().Be(string.Format(Strings.PickChar_ItemFormat, "Aragorn", "Otro"));
        choices[0].ToString().Should().Be(choices[0].Display);
    }

    [Fact]
    public async Task ImportFromCharacter_OneDecisionPerConflict_EachIsApplied()
    {
        await AddAlias(_db.CharacterId, "a", "uno");
        await AddAlias(_db.CharacterId, "b", "dos");
        await AddAlias(_db.SecondCharacterId, "a", "viejo a");
        await AddAlias(_db.SecondCharacterId, "b", "viejo b");
        var resolver = new PerItemResolver(key => key.EndsWith(":a", StringComparison.Ordinal) ?ImportDecision.Overwrite : ImportDecision.Skip);
        var source = new CharacterChoice(_db.CharacterId, "Gandalf", "Reinos");

        await Sut(ExchangeParts.Aliases, _db.SecondCharacterId, resolver).ImportFromCharacterAsync(source);

        resolver.Asked.Should().Be(2);
        var stored = await _db.Aliases.GetByCharacterAsync(_db.SecondCharacterId);
        stored.Select(a => a.Action).Should().BeEquivalentTo("uno", "viejo b");
    }

    private sealed class PerItemResolver(Func<string, ImportDecision> decide) : IImportConflictResolver
    {
        public int Asked { get; private set; }

        public IReadOnlyDictionary<string, ImportDecision>? Resolve(IReadOnlyList<ImportItem> items)
        {
            var rows = new ImportConflictsPresenter(items).Rows;
            Asked += rows.Count;
            return rows.ToDictionary(r => r.Key, r => decide(r.Key));
        }
    }
}
