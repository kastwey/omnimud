using Omnimud.Core.Session;
using Omnimud.Data.Entities;
using Omnimud.Data.Exchange;
using Omnimud.Data.Repositories;
using Omnimud.Data.Session;

namespace Omnimud.Data.Tests.Repositories;

/// <summary>Movement key codes 0-17 (numpad, arrows, Page Up/Down, Home, End) through repository, session store and exchange.</summary>
public sealed class MovementKeyRangeTests : IAsyncLifetime
{
    private readonly InMemoryDatabaseFixture _db = new();
    private SqliteMovementRepository _movements = null!;
    private SessionStore _store = null!;
    private int _mudId;
    private int _characterId;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        var muds = new SqliteMudRepository(_db);
        var characters = new SqliteCharacterRepository(_db);
        _movements = new SqliteMovementRepository(_db);
        _store = new SessionStore(muds, characters, new SqliteAliasRepository(_db), new SqliteTriggerRepository(_db),
            new SqlitePathRepository(_db), new SqliteDirectionRepository(_db), _movements, new SqliteMessageRuleRepository(_db));
        _mudId = await muds.AddAsync(new MudEntity { Name = "Mud", Host = "h", Port = 1 });
        _characterId = await characters.AddAsync(new CharacterEntity { MudId = _mudId, Name = "Pj" });
    }

    public Task DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task EveryCode_From0To17_RoundTrips_WithReplaceAll()
    {
        var all = Enumerable.Range(MovementKeys.MinCode, MovementKeys.Count).ToDictionary(k => k, k => $"cmd{k}");

        await _movements.ReplaceAllAsync(null, _characterId, all);

        var rows = await _movements.GetByCharacterAsync(_characterId);
        rows.Select(m => m.KeyCode).Should().Equal(Enumerable.Range(0, 18));
        rows.ToDictionary(m => m.KeyCode, m => m.Command).Should().BeEquivalentTo(all);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(17)]
    public async Task NavigationCodes_RoundTrip_WithAddSetAndUpdate(int key)
    {
        var id = await _movements.AddAsync(new MovementEntity { MudId = _mudId, KeyCode = key, Command = "norte" });
        await _movements.SetAsync(_mudId, null, key, "north");
        (await _movements.GetByMudAsync(_mudId)).Should().ContainSingle().Which.Should().BeEquivalentTo(new { Id = id, KeyCode = key, Command = "north" });

        await _movements.UpdateAsync(new MovementEntity { Id = id, MudId = _mudId, KeyCode = key == 17 ? 16 : key + 1, Command = "otra" });
        (await _movements.GetByMudAsync(_mudId)).Should().ContainSingle().Which.KeyCode.Should().Be(key == 17 ? 16 : key + 1);
    }

    [Theory]
    [InlineData(18)]
    [InlineData(-1)]
    [InlineData(38)] // Keys.Up: a virtual key is not a movement code
    public async Task CodesOutside0To17_AreRejected_ByEveryWriteMethod(int key)
    {
        var add = () => _movements.AddAsync(new MovementEntity { MudId = _mudId, KeyCode = key, Command = "x" });
        var update = () => _movements.UpdateAsync(new MovementEntity { Id = 1, MudId = _mudId, KeyCode = key, Command = "x" });
        var set = () => _movements.SetAsync(_mudId, null, key, "x");
        var replace = () => _movements.ReplaceAllAsync(_mudId, null, new Dictionary<int, string> { [8] = "norte", [key] = "x" });

        await add.Should().ThrowAsync<ArgumentException>();
        await update.Should().ThrowAsync<ArgumentException>();
        await set.Should().ThrowAsync<ArgumentException>();
        await replace.Should().ThrowAsync<ArgumentException>();
        (await _movements.GetByMudAsync(_mudId)).Should().BeEmpty("a rejected ReplaceAll writes nothing");
    }

    [Fact]
    public async Task EmptyCommand_IsStored_ItMeansTheKeyIsSwitchedOff()
    {
        await _movements.ReplaceAllAsync(null, _characterId, new Dictionary<int, string> { [10] = "", [11] = "sur" });

        var result = await _store.GetMovementsAsync(_mudId, _characterId);

        result.Should().BeEquivalentTo(new Dictionary<int, string> { [10] = "", [11] = "sur" });
    }

    [Fact]
    public async Task SessionStore_ReturnsNavigationKeys_AndKeepsCharacterOverMudInheritance()
    {
        await _movements.ReplaceAllAsync(_mudId, null, new Dictionary<int, string> { [10] = "n", [14] = "subir" });

        (await _store.GetMovementsAsync(_mudId, _characterId)).Should().BeEquivalentTo(new Dictionary<int, string> { [10] = "n", [14] = "subir" },
            "a character without keys of its own uses the MUD's");

        await _movements.SetAsync(null, _characterId, 12, "o");

        (await _store.GetMovementsAsync(_mudId, _characterId)).Should().BeEquivalentTo(new Dictionary<int, string> { [12] = "o" },
            "own keys replace the MUD's as a whole, never mixed");
    }

    [Fact]
    public void Exchange_Accepts0To17AndEmptyCommands_AndRejects18()
    {
        ExchangeJson.Validate([new MovementDto { Key = 10, Command = "norte" }, new MovementDto { Key = 17, Command = "" }, new MovementDto { Key = 0, Command = "mirar" }])
            .Should().BeNull();
        ExchangeJson.Validate([new MovementDto { Key = 18, Command = "x" }]).Should().NotBeNull();
        ExchangeJson.Validate([new MovementDto { Key = -1, Command = "x" }]).Should().NotBeNull();
        ExchangeJson.Validate([new MovementDto { Key = 10, Command = "a" }, new MovementDto { Key = 10, Command = "b" }]).Should().NotBeNull();
    }
}
