using Omnimud.Core.Aliases;
using Omnimud.Core.Paths;
using Omnimud.Core.Session;
using Omnimud.Core.Triggers;
using Omnimud.Data.Entities;
using Omnimud.Data.Repositories;
using Omnimud.Data.Session;

namespace Omnimud.Data.Tests.Session;

public sealed class SessionStoreTests : IAsyncLifetime
{
    private readonly InMemoryDatabaseFixture _db = new();
    private SqliteMudRepository _muds = null!;
    private SqliteCharacterRepository _characters = null!;
    private SqliteAliasRepository _aliases = null!;
    private SqliteTriggerRepository _triggers = null!;
    private SqlitePathRepository _paths = null!;
    private SqliteDirectionRepository _directions = null!;
    private SqliteMovementRepository _movements = null!;
    private SqliteMessageRuleRepository _rules = null!;
    private ISessionStore _sut = null!;
    private int _mudId;
    private int _characterId;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        _muds = new SqliteMudRepository(_db);
        _characters = new SqliteCharacterRepository(_db);
        _aliases = new SqliteAliasRepository(_db);
        _triggers = new SqliteTriggerRepository(_db);
        _paths = new SqlitePathRepository(_db);
        _directions = new SqliteDirectionRepository(_db);
        _movements = new SqliteMovementRepository(_db);
        _rules = new SqliteMessageRuleRepository(_db);
        _sut = new SessionStore(_muds, _characters, _aliases, _triggers, _paths, _directions, _movements, _rules);

        _mudId = await _muds.AddAsync(new MudEntity { Name = "Mud", Host = "h", Port = 1 });
        _characterId = await _characters.AddAsync(new CharacterEntity { MudId = _mudId, Name = "Pj" });
    }

    public Task DisposeAsync() => _db.DisposeAsync();

    // ── Aliases ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAliases_ReturnsOnlyEnabledOnes()
    {
        await _aliases.AddAsync(new AliasEntity { CharacterId = _characterId, Command = "k", Action = "matar", Enabled = true });
        await _aliases.AddAsync(new AliasEntity { CharacterId = _characterId, Command = "h", Action = "curar", Enabled = false });

        var result = await _sut.GetAliasesAsync(_characterId);

        result.Should().Equal(new AliasDefinition("k", "matar", true));
    }

    [Fact]
    public async Task AddAlias_ReturnsFalseWhenItExists_AndIsCaseSensitive()
    {
        (await _sut.AddAliasAsync(_characterId, "k", "matar")).Should().BeTrue();
        (await _sut.AddAliasAsync(_characterId, "k", "otra")).Should().BeFalse();
        (await _sut.AddAliasAsync(_characterId, "K", "otra")).Should().BeTrue();
        (await _sut.AddAliasAsync(_characterId, " ", "vacía")).Should().BeFalse();

        (await _sut.GetAliasesAsync(_characterId)).Should().BeEquivalentTo([new AliasDefinition("k", "matar"), new AliasDefinition("K", "otra")]);
    }

    [Fact]
    public async Task RemoveAlias_ReturnsFalseWhenItDidNotExist()
    {
        await _sut.AddAliasAsync(_characterId, "k", "matar");

        (await _sut.RemoveAliasAsync(_characterId, "x")).Should().BeFalse();
        (await _sut.RemoveAliasAsync(_characterId, "k")).Should().BeTrue();
        (await _sut.GetAliasesAsync(_characterId)).Should().BeEmpty();
    }

    // ── Triggers ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTriggers_MapsEveryField_EnumsIncluded_InCreationOrder()
    {
        await _triggers.AddAsync(new TriggerEntity
        {
            Id = "t1", CharacterId = _characterId, Name = "Uno", Pattern = "@cmd", PatternType = 0,
            Action = "return 1", ActionType = 2, Enabled = false, Priority = 5
        });
        await _triggers.AddAsync(new TriggerEntity
        {
            Id = "t2", CharacterId = _characterId, Name = "Dos", Pattern = @"^HP: (\d+)", PatternType = 1,
            Action = "", ActionType = 3, Sound = "ding.wav", Enabled = true, CaseSensitive = true, Priority = 99,
            Multiline = true, GagLine = true
        });
        await _triggers.AddAsync(new TriggerEntity
        {
            Id = "t3", CharacterId = _characterId, Name = "Tres", Pattern = "%s dice %s", PatternType = 2,
            Action = "x", ActionType = 1, Sound = "s.wav"
        });

        var result = await _sut.GetTriggersAsync(_characterId);

        result.Select(t => t.Id).Should().Equal("t1", "t2", "t3");
        result[0].Should().BeEquivalentTo(new TriggerDefinition
        {
            Id = "t1", Name = "Uno", Pattern = "@cmd", PatternType = PatternType.Literal, Action = "return 1",
            ActionType = TriggerActionType.Script, Enabled = false, Priority = 5
        });
        result[0].IsCommandTrigger.Should().BeTrue();
        result[1].Should().BeEquivalentTo(new TriggerDefinition
        {
            Id = "t2", Name = "Dos", Pattern = @"^HP: (\d+)", PatternType = PatternType.Regex, Action = "",
            ActionType = TriggerActionType.SendCommandAndPlaySound, Sound = "ding.wav", Enabled = true,
            CaseSensitive = true, Priority = 99, Multiline = true, GagLine = true
        });
        result[2].PatternType.Should().Be(PatternType.Sscanf);
        result[2].ActionType.Should().Be(TriggerActionType.PlaySound);
    }

    [Fact]
    public async Task GetTriggers_UnknownEnumNumbers_FallBackToDefaults()
    {
        await _triggers.AddAsync(new TriggerEntity { Id = "t", CharacterId = _characterId, Name = "n", Pattern = "p", Action = "a", PatternType = 42, ActionType = -1 });

        var trigger = (await _sut.GetTriggersAsync(_characterId)).Single();

        trigger.PatternType.Should().Be(PatternType.Literal);
        trigger.ActionType.Should().Be(TriggerActionType.SendCommand);
    }

    [Fact]
    public async Task SetTriggerEnabled_Persists()
    {
        await _triggers.AddAsync(new TriggerEntity { Id = "t", CharacterId = _characterId, Name = "n", Pattern = "p", Action = "a" });

        await _sut.SetTriggerEnabledAsync("t", false);

        (await _sut.GetTriggersAsync(_characterId)).Single().Enabled.Should().BeFalse();
    }

    [Fact]
    public void EntityMapper_TriggerRoundTrip()
    {
        var definition = new TriggerDefinition
        {
            Id = "x", Name = "n", Pattern = "p", PatternType = PatternType.Sscanf, Action = "a",
            ActionType = TriggerActionType.SendCommandAndPlaySound, Sound = "s", Enabled = false, CaseSensitive = true,
            Priority = 3, Multiline = true, GagLine = true
        };

        var entity = EntityMapper.ToEntity(definition, characterId: 7, sortOrder: 4);

        entity.CharacterId.Should().Be(7);
        entity.SortOrder.Should().Be(4);
        EntityMapper.ToDefinition(entity).Should().BeEquivalentTo(definition);
    }

    // ── Paths and directions ───────────────────────────────────────────────

    [Fact]
    public async Task GetPaths_MapsNameAndPath()
    {
        await _paths.AddAsync(new PathEntity { CharacterId = _characterId, Name = "plaza", Path = "3n2e" });

        (await _sut.GetPathsAsync(_characterId)).Should().Equal(new PathDefinition("plaza", "3n2e"));
    }

    [Fact]
    public async Task GetDirections_ArePerMud_WithCharAbbreviation()
    {
        var otherMud = await _muds.AddAsync(new MudEntity { Name = "Otro", Host = "h", Port = 1 });
        await _directions.AddAsync(new DirectionEntity { MudId = _mudId, Direction = "norte", Abbreviation = "n", OppositeDirection = "sur" });
        await _directions.AddAsync(new DirectionEntity { MudId = _mudId, Direction = "entrar", Abbreviation = "x", OppositeDirection = "" });
        await _directions.AddAsync(new DirectionEntity { MudId = otherMud, Direction = "north", Abbreviation = "n", OppositeDirection = "south" });

        var result = await _sut.GetDirectionsAsync(_mudId);

        result.Should().Equal(new DirectionEntry("norte", 'n', "sur"), new DirectionEntry("entrar", 'x', null));
        (await _sut.GetDirectionsAsync(otherMud)).Should().Equal(new DirectionEntry("north", 'n', "south"));
    }

    // ── Movements ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMovements_CharacterWithoutOwn_InheritsTheMuds()
    {
        await _movements.SetAsync(_mudId, null, 8, "norte");
        await _movements.SetAsync(_mudId, null, 2, "sur");

        var result = await _sut.GetMovementsAsync(_mudId, _characterId);

        result.Should().BeEquivalentTo(new Dictionary<int, string> { [8] = "norte", [2] = "sur" });
    }

    [Fact]
    public async Task GetMovements_CharacterWithOwn_UsesOnlyItsOwn_NotAMix()
    {
        await _movements.SetAsync(_mudId, null, 8, "norte");
        await _movements.SetAsync(_mudId, null, 2, "sur");
        await _movements.SetAsync(null, _characterId, 5, "mirar");

        var result = await _sut.GetMovementsAsync(_mudId, _characterId);

        result.Should().BeEquivalentTo(new Dictionary<int, string> { [5] = "mirar" });
    }

    [Fact]
    public async Task GetMovements_WithoutCharacter_UsesTheMuds_AndWithoutAnything_IsEmpty()
    {
        await _movements.SetAsync(_mudId, null, 8, "norte");

        (await _sut.GetMovementsAsync(_mudId, null)).Should().BeEquivalentTo(new Dictionary<int, string> { [8] = "norte" });
        (await _sut.GetMovementsAsync(null, null)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetMovements_OnlyCharacterGiven_StillFindsItsMud()
    {
        await _movements.SetAsync(_mudId, null, 8, "norte");

        (await _sut.GetMovementsAsync(null, _characterId)).Should().BeEquivalentTo(new Dictionary<int, string> { [8] = "norte" });
    }

    [Fact]
    public async Task MovementMode_IsPerCharacterWhenThereIsOne_ElsePerMud()
    {
        (await _sut.GetMovementModeAsync(_mudId, _characterId)).Should().BeFalse();

        await _sut.SetMovementModeAsync(_mudId, _characterId, true);
        (await _sut.GetMovementModeAsync(_mudId, _characterId)).Should().BeTrue();
        (await _sut.GetMovementModeAsync(_mudId, null)).Should().BeFalse("the character's flag does not leak to the MUD");
        (await _characters.GetByIdAsync(_characterId))!.MovementMode.Should().BeTrue();

        await _sut.SetMovementModeAsync(_mudId, null, true);
        await _sut.SetMovementModeAsync(_mudId, _characterId, false);
        (await _sut.GetMovementModeAsync(_mudId, null)).Should().BeTrue();
        (await _sut.GetMovementModeAsync(_mudId, _characterId)).Should().BeFalse();

        (await _sut.GetMovementModeAsync(null, null)).Should().BeFalse();
        await _sut.Invoking(s => s.SetMovementModeAsync(null, null, true)).Should().NotThrowAsync();
    }

    // ── Message rules ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetMessageRules_NoRuleSet_IsEmpty()
    {
        (await _sut.GetMessageRulesAsync(_mudId)).Should().BeEmpty();
        (await _sut.GetMessageRulesAsync(987654)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetMessageRules_ReturnsEnabledRulesOfTheMudsSet_InOrder()
    {
        var setId = await _rules.AddRuleSetAsync(new MessageRuleSetEntity { Name = "Propio" });
        await _rules.AddRuleAsync(new MessageRuleEntity { RuleSetId = setId, SortOrder = 3, Pattern = "tercera", Template = "$0" });
        await _rules.AddRuleAsync(new MessageRuleEntity { RuleSetId = setId, SortOrder = 1, Pattern = "primera", Template = "$1", CaseSensitive = true, Channel = "canal" });
        await _rules.AddRuleAsync(new MessageRuleEntity { RuleSetId = setId, SortOrder = 2, Pattern = "apagada", Enabled = false });
        await _muds.SetMessageRuleSetAsync(_mudId, setId);

        var result = await _sut.GetMessageRulesAsync(_mudId);

        result.Should().Equal(
            new MessageRule("primera", "$1", true, "canal"),
            new MessageRule("tercera", "$0", false, null));
    }

    [Fact]
    public async Task GetMessageRules_SeededSet()
    {
        await _muds.SetMessageRuleSetAsync(_mudId, (await _rules.GetRuleSetByNameAsync("Cyberlife"))!.Id);

        var result = await _sut.GetMessageRulesAsync(_mudId);

        result.Should().HaveCount(15);
        result.Should().OnlyContain(r => !r.CaseSensitive && r.Template == "$0");
    }
}
