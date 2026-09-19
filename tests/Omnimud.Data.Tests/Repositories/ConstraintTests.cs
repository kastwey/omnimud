using Dapper;
using Omnimud.Data.Entities;
using Omnimud.Data.Repositories;

namespace Omnimud.Data.Tests.Repositories;

/// <summary>Duplicates surface as DuplicateEntityException, deletes cascade, and the default character stays coherent.</summary>
public sealed class ConstraintTests : IAsyncLifetime
{
    private readonly InMemoryDatabaseFixture _db = new();
    private SqliteMudRepository _muds = null!;
    private SqliteCharacterRepository _characters = null!;
    private int _mudId;
    private int _characterId;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        _muds = new SqliteMudRepository(_db);
        _characters = new SqliteCharacterRepository(_db);
        _mudId = await _muds.AddAsync(new MudEntity { Name = "Base", Host = "h", Port = 1 });
        _characterId = await _characters.AddAsync(new CharacterEntity { MudId = _mudId, Name = "Pj" });
    }

    public Task DisposeAsync() => _db.DisposeAsync();

    // ── Duplicates ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Mud_DuplicateName_OnAddAndOnUpdate()
    {
        var add = () => _muds.AddAsync(new MudEntity { Name = "Base", Host = "x", Port = 2 });
        (await add.Should().ThrowAsync<DuplicateEntityException>()).Which.Should().Match<DuplicateEntityException>(
            e => e.EntityName == "Mud" && e.Key == "Base" && e.InnerException != null);

        var otherId = await _muds.AddAsync(new MudEntity { Name = "Otro", Host = "x", Port = 2 });
        var other = (await _muds.GetByIdAsync(otherId))!;
        other.Name = "Base";
        var update = () => _muds.UpdateAsync(other);
        await update.Should().ThrowAsync<DuplicateEntityException>();
    }

    [Fact]
    public async Task Character_DuplicateNameInSameMud_ButAllowedInAnother()
    {
        var add = () => _characters.AddAsync(new CharacterEntity { MudId = _mudId, Name = "Pj" });
        await add.Should().ThrowAsync<DuplicateEntityException>();

        var otherMud = await _muds.AddAsync(new MudEntity { Name = "Otro", Host = "x", Port = 2 });
        var allowed = () => _characters.AddAsync(new CharacterEntity { MudId = otherMud, Name = "Pj" });
        await allowed.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Character_DuplicateName_OnUpdate_LeavesNothingHalfDone()
    {
        var secondId = await _characters.AddAsync(new CharacterEntity { MudId = _mudId, Name = "Segundo" });
        var second = (await _characters.GetByIdAsync(secondId))!;
        second.Name = "Pj";
        second.IsDefault = true;

        var update = () => _characters.UpdateAsync(second);
        await update.Should().ThrowAsync<DuplicateEntityException>();

        (await _characters.GetByIdAsync(secondId))!.IsDefault.Should().BeFalse("the transaction was rolled back");
    }

    [Fact]
    public async Task Alias_Path_Trigger_Duplicates()
    {
        var aliases = new SqliteAliasRepository(_db);
        await aliases.AddAsync(new AliasEntity { CharacterId = _characterId, Command = "k", Action = "a" });
        var alias = () => aliases.AddAsync(new AliasEntity { CharacterId = _characterId, Command = "k", Action = "b" });
        await alias.Should().ThrowAsync<DuplicateEntityException>().Where(e => e.EntityName == "Alias" && e.Key == "k");

        // Aliases are case-sensitive: "K" is a different alias.
        var upper = () => aliases.AddAsync(new AliasEntity { CharacterId = _characterId, Command = "K", Action = "b" });
        await upper.Should().NotThrowAsync();

        var paths = new SqlitePathRepository(_db);
        await paths.AddAsync(new PathEntity { CharacterId = _characterId, Name = "p", Path = "n" });
        var path = () => paths.AddAsync(new PathEntity { CharacterId = _characterId, Name = "p", Path = "s" });
        await path.Should().ThrowAsync<DuplicateEntityException>().Where(e => e.EntityName == "Path");

        var triggers = new SqliteTriggerRepository(_db);
        await triggers.AddAsync(new TriggerEntity { Id = "same", CharacterId = _characterId, Name = "a", Pattern = "a", Action = "a" });
        var trigger = () => triggers.AddAsync(new TriggerEntity { Id = "same", CharacterId = _characterId, Name = "b", Pattern = "b", Action = "b" });
        await trigger.Should().ThrowAsync<DuplicateEntityException>().Where(e => e.EntityName == "Trigger");
    }

    [Fact]
    public async Task Direction_Duplicates_AndValidation()
    {
        var repo = new SqliteDirectionRepository(_db);
        await repo.AddAsync(new DirectionEntity { MudId = _mudId, Direction = "norte", Abbreviation = "n", OppositeDirection = "sur" });

        var sameName = () => repo.AddAsync(new DirectionEntity { MudId = _mudId, Direction = "norte", Abbreviation = "x" });
        var sameAbbreviation = () => repo.AddAsync(new DirectionEntity { MudId = _mudId, Direction = "noreste", Abbreviation = "n" });
        var twoCharacters = () => repo.AddAsync(new DirectionEntity { MudId = _mudId, Direction = "noreste", Abbreviation = "ne" });
        var empty = () => repo.AddAsync(new DirectionEntity { MudId = _mudId, Direction = "noreste", Abbreviation = "" });

        await sameName.Should().ThrowAsync<DuplicateEntityException>();
        await sameAbbreviation.Should().ThrowAsync<DuplicateEntityException>();
        await twoCharacters.Should().ThrowAsync<ArgumentException>();
        await empty.Should().ThrowAsync<ArgumentException>();

        var otherMud = await _muds.AddAsync(new MudEntity { Name = "Otro", Host = "x", Port = 2 });
        var inAnotherMud = () => repo.AddAsync(new DirectionEntity { MudId = otherMud, Direction = "norte", Abbreviation = "n" });
        await inAnotherMud.Should().NotThrowAsync("dictionaries are per MUD");
    }

    [Fact]
    public async Task Direction_ReplaceAll_IsAtomic()
    {
        var repo = new SqliteDirectionRepository(_db);
        await repo.ReplaceAllAsync(_mudId, [
            new DirectionEntity { Direction = "norte", Abbreviation = "n", OppositeDirection = "sur" },
            new DirectionEntity { Direction = "sur", Abbreviation = "s", OppositeDirection = "norte" }]);
        (await repo.GetByMudAsync(_mudId)).Select(d => d.Direction).Should().Equal("norte", "sur");

        var broken = () => repo.ReplaceAllAsync(_mudId, [
            new DirectionEntity { Direction = "este", Abbreviation = "e" },
            new DirectionEntity { Direction = "este", Abbreviation = "x" }]);
        await broken.Should().ThrowAsync<DuplicateEntityException>();

        (await repo.GetByMudAsync(_mudId)).Select(d => d.Direction).Should().Equal(["norte", "sur"], "a failed replacement keeps the previous dictionary");
    }

    [Fact]
    public async Task Movement_Duplicates_Validation_SetAndReplace()
    {
        var repo = new SqliteMovementRepository(_db);
        await repo.AddAsync(new MovementEntity { CharacterId = _characterId, KeyCode = 8, Command = "norte" });

        var duplicate = () => repo.AddAsync(new MovementEntity { CharacterId = _characterId, KeyCode = 8, Command = "otra" });
        var noOwner = () => repo.AddAsync(new MovementEntity { KeyCode = 1, Command = "x" });
        var twoOwners = () => repo.AddAsync(new MovementEntity { MudId = _mudId, CharacterId = _characterId, KeyCode = 1, Command = "x" });
        var badKey = () => repo.AddAsync(new MovementEntity { MudId = _mudId, KeyCode = 18, Command = "x" });
        await duplicate.Should().ThrowAsync<DuplicateEntityException>();
        await noOwner.Should().ThrowAsync<ArgumentException>();
        await twoOwners.Should().ThrowAsync<ArgumentException>();
        await badKey.Should().ThrowAsync<ArgumentException>();

        // Same key for the MUD is a different owner.
        await repo.SetAsync(_mudId, null, 8, "north");
        await repo.SetAsync(_mudId, null, 8, "norte del mud");
        (await repo.GetByMudAsync(_mudId)).Should().ContainSingle().Which.Command.Should().Be("norte del mud");
        (await repo.GetByCharacterAsync(_characterId)).Should().ContainSingle().Which.Command.Should().Be("norte");

        await repo.ReplaceAllAsync(null, _characterId, new Dictionary<int, string> { [2] = "sur", [4] = "oeste" });
        (await repo.GetByCharacterAsync(_characterId)).Select(m => $"{m.KeyCode}={m.Command}").Should().Equal("2=sur", "4=oeste");
        (await repo.GetByMudAsync(_mudId)).Should().ContainSingle("replacing the character's movements does not touch the MUD's");

        await repo.ReplaceAllAsync(null, _characterId, new Dictionary<int, string>());
        (await repo.GetByCharacterAsync(_characterId)).Should().BeEmpty();
    }

    [Fact]
    public async Task MessageRuleSet_DuplicateName_IsCaseInsensitive()
    {
        var repo = new SqliteMessageRuleRepository(_db);
        var add = () => repo.AddRuleSetAsync(new MessageRuleSetEntity { Name = "SIMAURIA" });
        await add.Should().ThrowAsync<DuplicateEntityException>().Where(e => e.EntityName == "MessageRuleSet");

        var simauriaId = (await repo.GetRuleSetByNameAsync("Simauria"))!.Id;
        var duplicate = () => repo.DuplicateRuleSetAsync(simauriaId, "cyberlife");
        await duplicate.Should().ThrowAsync<DuplicateEntityException>();
        (await repo.GetRuleSetsAsync()).Should().HaveCount(4, "the failed copy was rolled back");
    }

    // ── Message rules CRUD ─────────────────────────────────────────────────

    [Fact]
    public async Task MessageRules_Crud_Order_Duplicate_AndCascade()
    {
        var repo = new SqliteMessageRuleRepository(_db);
        var setId = await repo.AddRuleSetAsync(new MessageRuleSetEntity { Name = "Propio" });

        var first = new MessageRuleEntity { RuleSetId = setId, Pattern = "a" };
        var second = new MessageRuleEntity { RuleSetId = setId, Pattern = "b" };
        await repo.AddRuleAsync(first);
        second.Id = await repo.AddRuleAsync(second);
        first.SortOrder.Should().Be(1);
        second.SortOrder.Should().Be(2);

        await repo.ReplaceRulesAsync(setId, [
            new MessageRuleEntity { Pattern = "z", Template = "$1", Channel = "c" },
            new MessageRuleEntity { Pattern = "y", Enabled = false },
            new MessageRuleEntity { Pattern = "x" }]);
        var rules = await repo.GetRulesAsync(setId);
        rules.Select(r => r.Pattern).Should().Equal("z", "y", "x");
        rules.Select(r => r.SortOrder).Should().Equal(1, 2, 3);

        await repo.DeleteRuleAsync(rules[1].Id);
        (await repo.GetRulesAsync(setId)).Select(r => r.Pattern).Should().Equal("z", "x");

        var copyId = await repo.DuplicateRuleSetAsync(setId, "Copia");
        (await repo.GetRuleSetByIdAsync(copyId))!.IsBuiltIn.Should().BeFalse();
        (await repo.GetRulesAsync(copyId)).Should().BeEquivalentTo(await repo.GetRulesAsync(setId),
            o => o.Excluding(r => r.Id).Excluding(r => r.RuleSetId).WithStrictOrdering());

        await _muds.SetMessageRuleSetAsync(_mudId, setId);
        await repo.DeleteRuleSetAsync(setId);
        (await repo.GetRulesAsync(setId)).Should().BeEmpty();
        (await _muds.GetByIdAsync(_mudId))!.MessageRuleSetId.Should().BeNull();
        (await repo.GetRuleSetByNameAsync("propio")).Should().BeNull();
        (await repo.GetRuleSetByNameAsync("COPIA")).Should().NotBeNull();
    }

    // ── Cascades ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeletingAMud_RemovesEverythingThatHangsFromIt()
    {
        await FillCharacterAsync(_characterId);
        await new SqliteDirectionRepository(_db).AddAsync(new DirectionEntity { MudId = _mudId, Direction = "norte", Abbreviation = "n" });
        await new SqliteMovementRepository(_db).AddAsync(new MovementEntity { MudId = _mudId, KeyCode = 8, Command = "norte" });
        await new SqliteOptionRepository(_db).SetValueAsync(1, _mudId, "Volume", "10");
        await new SqliteOptionRepository(_db).SetValueAsync(0, null, "Volume", "90");

        await _muds.DeleteAsync(_mudId);

        using var conn = _db.Create();
        foreach (var table in new[] { "Characters", "Aliases", "Triggers", "Paths", "Directions", "Movements" })
            (await conn.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM {table}")).Should().Be(0, $"{table} must be emptied by the cascade");
        (await conn.QueryAsync<int>("SELECT Scope FROM Options")).Should().Equal([0], "only the global option survives");
    }

    [Fact]
    public async Task DeletingACharacter_RemovesItsData_AndClearsTheDefault()
    {
        await FillCharacterAsync(_characterId);
        await _characters.SetDefaultAsync(_mudId, _characterId);

        await _characters.DeleteAsync(_characterId);

        using var conn = _db.Create();
        foreach (var table in new[] { "Aliases", "Triggers", "Paths", "Movements", "Options" })
            (await conn.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM {table}")).Should().Be(0, table);
        (await _muds.GetByIdAsync(_mudId))!.DefaultCharacterId.Should().BeNull();
    }

    private async Task FillCharacterAsync(int characterId)
    {
        await new SqliteAliasRepository(_db).AddAsync(new AliasEntity { CharacterId = characterId, Command = "k", Action = "a" });
        await new SqliteTriggerRepository(_db).AddAsync(new TriggerEntity { Id = Guid.NewGuid().ToString(), CharacterId = characterId, Name = "t", Pattern = "p", Action = "a" });
        await new SqlitePathRepository(_db).AddAsync(new PathEntity { CharacterId = characterId, Name = "p", Path = "n" });
        await new SqliteMovementRepository(_db).AddAsync(new MovementEntity { CharacterId = characterId, KeyCode = 1, Command = "c" });
        await new SqliteOptionRepository(_db).SetValueAsync(2, characterId, "Volume", "20");
    }

    // ── Default character coherence ────────────────────────────────────────

    [Fact]
    public async Task SetDefault_KeepsIsDefaultAndDefaultCharacterIdInStep()
    {
        var secondId = await _characters.AddAsync(new CharacterEntity { MudId = _mudId, Name = "Segundo" });

        await _characters.SetDefaultAsync(_mudId, _characterId);
        await AssertDefaultAsync(_characterId);

        await _characters.SetDefaultAsync(_mudId, secondId);
        await AssertDefaultAsync(secondId);

        await _characters.ClearDefaultAsync(_mudId);
        await AssertDefaultAsync(null);
    }

    [Fact]
    public async Task SetDefault_WithACharacterOfAnotherMud_LeavesNoDefault()
    {
        var otherMud = await _muds.AddAsync(new MudEntity { Name = "Otro", Host = "x", Port = 2 });
        var foreign = await _characters.AddAsync(new CharacterEntity { MudId = otherMud, Name = "Ajeno" });
        await _characters.SetDefaultAsync(_mudId, _characterId);

        await _characters.SetDefaultAsync(_mudId, foreign);

        await AssertDefaultAsync(null);
        (await _characters.GetByIdAsync(foreign))!.IsDefault.Should().BeFalse();
    }

    [Fact]
    public async Task AddAndUpdateCharacter_WithIsDefault_UpdateTheMud()
    {
        var secondId = await _characters.AddAsync(new CharacterEntity { MudId = _mudId, Name = "Segundo", IsDefault = true });
        await AssertDefaultAsync(secondId);

        var first = (await _characters.GetByIdAsync(_characterId))!;
        first.IsDefault = true;
        await _characters.UpdateAsync(first);
        await AssertDefaultAsync(_characterId);

        first.IsDefault = false;
        await _characters.UpdateAsync(first);
        await AssertDefaultAsync(null);
    }

    [Fact]
    public async Task UpdateMud_DefaultCharacterId_IsValidatedAndMirrored()
    {
        var otherMud = await _muds.AddAsync(new MudEntity { Name = "Otro", Host = "x", Port = 2 });
        var foreign = await _characters.AddAsync(new CharacterEntity { MudId = otherMud, Name = "Ajeno" });

        var mud = (await _muds.GetByIdAsync(_mudId))!;
        mud.DefaultCharacterId = _characterId;
        await _muds.UpdateAsync(mud);
        await AssertDefaultAsync(_characterId);

        mud.DefaultCharacterId = foreign;
        await _muds.UpdateAsync(mud);
        await AssertDefaultAsync(null);
    }

    private async Task AssertDefaultAsync(int? expectedCharacterId)
    {
        (await _muds.GetByIdAsync(_mudId))!.DefaultCharacterId.Should().Be(expectedCharacterId);
        var flagged = (await _characters.GetByMudAsync(_mudId)).Where(c => c.IsDefault).Select(c => (int?)c.Id).ToList();
        flagged.Should().Equal(expectedCharacterId is null ? [] : new[] { expectedCharacterId });
    }

    // ── Options ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Options_GlobalUpsert_NeverDuplicates_AndNullEqualsZero()
    {
        var repo = new SqliteOptionRepository(_db);
        await repo.SetValueAsync(0, null, "Language", "en");
        await repo.SetValueAsync(0, null, "Language", "es");
        await repo.SetValueAsync(0, 0, "Language", "fr");

        using var conn = _db.Create();
        (await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Options WHERE Key = 'Language'")).Should().Be(1);
        (await repo.GetValueAsync(0, null, "Language")).Should().Be("fr");
        (await repo.HasAnyAsync(0, null)).Should().BeTrue();
        (await repo.HasAnyAsync(1, _mudId)).Should().BeFalse();
    }

    [Fact]
    public async Task Options_ReplaceAll_ReplacesOnlyThatScope()
    {
        var repo = new SqliteOptionRepository(_db);
        await repo.SetValueAsync(1, _mudId, "Old", "1");
        await repo.SetValueAsync(0, null, "Global", "1");

        await repo.ReplaceAllAsync(1, _mudId, new Dictionary<string, string> { ["A"] = "1", ["B"] = "2" });

        (await repo.GetByScope(1, _mudId)).Select(o => o.Key).Should().BeEquivalentTo("A", "B");
        (await repo.GetValueAsync(0, null, "Global")).Should().Be("1");
    }

    [Fact]
    public async Task Alias_RemoveByCommand_IsExact_AndTellsWhetherItExisted()
    {
        var repo = new SqliteAliasRepository(_db);
        await repo.AddAsync(new AliasEntity { CharacterId = _characterId, Command = "k", Action = "a" });

        (await repo.RemoveByCommandAsync(_characterId, "K")).Should().BeFalse();
        (await repo.RemoveByCommandAsync(_characterId, "k")).Should().BeTrue();
        (await repo.RemoveByCommandAsync(_characterId, "k")).Should().BeFalse();
    }
}
