using Omnimud.Data.Entities;
using Omnimud.Data.Repositories;

namespace Omnimud.Data.Tests.Repositories;

/// <summary>
/// Every column of every entity survives Add → Get and Update → Get. Each entity is filled with
/// non-default values so a column missing from the SQL shows up as a difference.
/// </summary>
public sealed class RoundTripTests : IAsyncLifetime
{
    private readonly InMemoryDatabaseFixture _db = new();
    private SqliteMudRepository _muds = null!;
    private SqliteCharacterRepository _characters = null!;
    private int _mudId;
    private int _characterId;
    private int _ruleSetId;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        _muds = new SqliteMudRepository(_db);
        _characters = new SqliteCharacterRepository(_db);
        _mudId = await _muds.AddAsync(new MudEntity { Name = "Base", Host = "h", Port = 1 });
        _characterId = await _characters.AddAsync(new CharacterEntity { MudId = _mudId, Name = "Pj" });
        _ruleSetId = (await new SqliteMessageRuleRepository(_db).GetRuleSetByNameAsync("Simauria"))!.Id;
    }

    public Task DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task Mud_AllColumns()
    {
        var mud = new MudEntity
        {
            Name = "Reinos de Leyenda",
            Host = "rlmud.org",
            Port = 5001,
            UseTls = true,
            ValidateCertificate = false,
            SaveCommand = "guardar",
            QuitCommand = "salir",
            LoginScript = "%n\n%p",
            ProcessRule = "Simauria",
            MessageRuleSetId = _ruleSetId,
            SoundDirectory = @"D:\sonidos",
            Encoding = "iso-8859-1",
            MovementMode = true
        };
        mud.Id = await _muds.AddAsync(mud);

        var stored = await _muds.GetByIdAsync(mud.Id);
        stored.Should().BeEquivalentTo(mud, o => o.Excluding(m => m.CreatedAt).Excluding(m => m.UpdatedAt));
        stored!.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));
        stored.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));

        var characterId = await _characters.AddAsync(new CharacterEntity { MudId = mud.Id, Name = "Predeterminado" });
        stored.Name = "Otro nombre";
        stored.Host = "otro.org";
        stored.Port = 23;
        stored.UseTls = false;
        stored.ValidateCertificate = true;
        stored.SaveCommand = null;
        stored.QuitCommand = "quit";
        stored.LoginScript = null;
        stored.ProcessRule = null;
        stored.MessageRuleSetId = null;
        stored.SoundDirectory = null;
        stored.Encoding = "utf-8";
        stored.MovementMode = false;
        stored.DefaultCharacterId = characterId;
        await _muds.UpdateAsync(stored);

        var updated = await _muds.GetByIdAsync(mud.Id);
        updated.Should().BeEquivalentTo(stored, o => o.Excluding(m => m.CreatedAt).Excluding(m => m.UpdatedAt));
    }

    [Fact]
    public async Task Character_AllColumns()
    {
        var character = new CharacterEntity
        {
            MudId = _mudId,
            Name = "Gandalf",
            EncryptedPassword = [9, 8, 7, 6],
            IsDefault = true,
            MovementMode = true
        };
        character.Id = await _characters.AddAsync(character);

        var stored = await _characters.GetByIdAsync(character.Id);
        stored.Should().BeEquivalentTo(character, o => o.Excluding(c => c.CreatedAt).Excluding(c => c.UpdatedAt));
        stored!.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));

        stored.Name = "Mithrandir";
        stored.EncryptedPassword = null;
        stored.IsDefault = false;
        stored.MovementMode = false;
        await _characters.UpdateAsync(stored);

        var updated = await _characters.GetByIdAsync(character.Id);
        updated.Should().BeEquivalentTo(stored, o => o.Excluding(c => c.CreatedAt).Excluding(c => c.UpdatedAt));
    }

    [Fact]
    public async Task Alias_AllColumns()
    {
        var repo = new SqliteAliasRepository(_db);
        var alias = new AliasEntity { CharacterId = _characterId, Command = "k", Action = "matar orco", Enabled = false };
        alias.Id = await repo.AddAsync(alias);

        (await repo.GetByCharacterAsync(_characterId)).Should().ContainSingle().Which.Should().BeEquivalentTo(alias);

        alias.Command = "kk";
        alias.Action = "matar todo";
        alias.Enabled = true;
        await repo.UpdateAsync(alias);

        (await repo.GetByCharacterAsync(_characterId)).Should().ContainSingle().Which.Should().BeEquivalentTo(alias);
    }

    [Fact]
    public async Task Trigger_AllColumns()
    {
        var repo = new SqliteTriggerRepository(_db);
        var trigger = new TriggerEntity
        {
            Id = "trigger-1",
            CharacterId = _characterId,
            Name = "Vida baja",
            Pattern = @"^Vida: (\d+)$",
            PatternType = 1,
            Action = "beber pocion",
            ActionType = 3,
            Sound = "alarma.wav",
            Enabled = false,
            CaseSensitive = true,
            Priority = 77,
            Multiline = true,
            GagLine = true,
            SortOrder = 5
        };
        await repo.AddAsync(trigger);

        var stored = await repo.GetByIdAsync("trigger-1");
        stored.Should().BeEquivalentTo(trigger, o => o.Excluding(t => t.CreatedAt).Excluding(t => t.UpdatedAt));
        stored!.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));

        stored.Name = "Otro";
        stored.Pattern = "otro";
        stored.PatternType = 2;
        stored.Action = string.Empty;
        stored.ActionType = 1;
        stored.Sound = null;
        stored.Enabled = true;
        stored.CaseSensitive = false;
        stored.Priority = 1;
        stored.Multiline = false;
        stored.GagLine = false;
        stored.SortOrder = 9;
        await repo.UpdateAsync(stored);

        (await repo.GetByIdAsync("trigger-1")).Should().BeEquivalentTo(stored, o => o.Excluding(t => t.CreatedAt).Excluding(t => t.UpdatedAt));
    }

    [Fact]
    public async Task Path_AllColumns()
    {
        var repo = new SqlitePathRepository(_db);
        var path = new PathEntity { CharacterId = _characterId, Name = "plaza", Path = "3n2e" };
        path.Id = await repo.AddAsync(path);

        (await repo.GetByCharacterAsync(_characterId)).Should().ContainSingle().Which.Should().BeEquivalentTo(path);

        path.Name = "mercado";
        path.Path = "5s";
        await repo.UpdateAsync(path);

        (await repo.GetByCharacterAsync(_characterId)).Should().ContainSingle().Which.Should().BeEquivalentTo(path);
    }

    [Fact]
    public async Task Direction_AllColumns()
    {
        var repo = new SqliteDirectionRepository(_db);
        var direction = new DirectionEntity { MudId = _mudId, Direction = "norte", Abbreviation = "n", OppositeDirection = "sur" };
        direction.Id = await repo.AddAsync(direction);

        (await repo.GetByMudAsync(_mudId)).Should().ContainSingle().Which.Should().BeEquivalentTo(direction);

        direction.Direction = "arriba";
        direction.Abbreviation = "ñ";
        direction.OppositeDirection = null;
        await repo.UpdateAsync(direction);

        (await repo.GetByMudAsync(_mudId)).Should().ContainSingle().Which.Should().BeEquivalentTo(direction);
    }

    [Fact]
    public async Task Movement_AllColumns_ForBothOwners()
    {
        var repo = new SqliteMovementRepository(_db);
        var ofMud = new MovementEntity { MudId = _mudId, KeyCode = 8, Command = "norte" };
        var ofCharacter = new MovementEntity { CharacterId = _characterId, KeyCode = 0, Command = "mirar" };
        ofMud.Id = await repo.AddAsync(ofMud);
        ofCharacter.Id = await repo.AddAsync(ofCharacter);

        (await repo.GetByMudAsync(_mudId)).Should().ContainSingle().Which.Should().BeEquivalentTo(ofMud);
        (await repo.GetByCharacterAsync(_characterId)).Should().ContainSingle().Which.Should().BeEquivalentTo(ofCharacter);

        ofCharacter.KeyCode = 9;
        ofCharacter.Command = "inventario";
        await repo.UpdateAsync(ofCharacter);

        (await repo.GetByCharacterAsync(_characterId)).Should().ContainSingle().Which.Should().BeEquivalentTo(ofCharacter);
    }

    [Fact]
    public async Task MessageRuleSetAndRule_AllColumns()
    {
        var repo = new SqliteMessageRuleRepository(_db);
        var set = new MessageRuleSetEntity { Name = "Mi MUD", IsBuiltIn = true };
        set.Id = await repo.AddRuleSetAsync(set);
        (await repo.GetRuleSetByIdAsync(set.Id)).Should().BeEquivalentTo(set);

        set.Name = "Mi MUD 2";
        set.IsBuiltIn = false;
        await repo.UpdateRuleSetAsync(set);
        (await repo.GetRuleSetByIdAsync(set.Id)).Should().BeEquivalentTo(set);

        var rule = new MessageRuleEntity
        {
            RuleSetId = set.Id, SortOrder = 4, Pattern = @"^(\w+) dice: (.*)$", Template = "$1: $2",
            CaseSensitive = true, Channel = "decir", Enabled = false
        };
        rule.Id = await repo.AddRuleAsync(rule);
        (await repo.GetRulesAsync(set.Id)).Should().ContainSingle().Which.Should().BeEquivalentTo(rule);

        rule.SortOrder = 2;
        rule.Pattern = "otra";
        rule.Template = "$0";
        rule.CaseSensitive = false;
        rule.Channel = null;
        rule.Enabled = true;
        await repo.UpdateRuleAsync(rule);
        (await repo.GetRulesAsync(set.Id)).Should().ContainSingle().Which.Should().BeEquivalentTo(rule);
    }

    [Fact]
    public async Task Option_AllColumns()
    {
        var repo = new SqliteOptionRepository(_db);
        await repo.SetValueAsync(2, _characterId, "FontFamily", "Consolas");

        var row = (await repo.GetByScope(2, _characterId)).Should().ContainSingle().Subject;
        row.Should().BeEquivalentTo(new OptionEntity { Scope = 2, ScopeId = _characterId, Key = "FontFamily", Value = "Consolas" },
            o => o.Excluding(e => e.Id));
        row.Id.Should().BeGreaterThan(0);
    }
}
