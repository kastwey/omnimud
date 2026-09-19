using FluentAssertions;
using Omnimud.Data.Entities;
using Omnimud.Data.Repositories;

namespace Omnimud.Data.Tests.Repositories;

public sealed class SqliteAliasRepositoryTests : IAsyncLifetime
{
    private readonly InMemoryDatabaseFixture _db = new();
    private SqliteAliasRepository _sut = null!;
    private int _characterId;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        _sut = new SqliteAliasRepository(_db);

        var mudRepo = new SqliteMudRepository(_db);
        var mudId = await mudRepo.AddAsync(new MudEntity { Name = "TestMud", Host = "host", Port = 4000 });

        var charRepo = new SqliteCharacterRepository(_db);
        _characterId = await charRepo.AddAsync(new CharacterEntity { MudId = mudId, Name = "TestChar" });
    }

    public Task DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task AddAsync_ReturnsId()
    {
        var id = await _sut.AddAsync(CreateAlias("atk", "attack"));
        id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetByCharacterAsync_ReturnsAliases()
    {
        await _sut.AddAsync(CreateAlias("h", "heal"));
        await _sut.AddAsync(CreateAlias("k", "kill"));

        var result = await _sut.GetByCharacterAsync(_characterId);
        result.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task UpdateAsync_ModifiesAction()
    {
        var id = await _sut.AddAsync(CreateAlias("cmd", "original"));

        var aliases = await _sut.GetByCharacterAsync(_characterId);
        var alias = aliases.First(a => a.Id == id);
        alias.Action = "modified";
        await _sut.UpdateAsync(alias);

        var updated = (await _sut.GetByCharacterAsync(_characterId)).First(a => a.Id == id);
        updated.Action.Should().Be("modified");
    }

    [Fact]
    public async Task DeleteAsync_RemovesAlias()
    {
        var id = await _sut.AddAsync(CreateAlias("del", "delete"));
        await _sut.DeleteAsync(id);

        var result = await _sut.GetByCharacterAsync(_characterId);
        result.Should().NotContain(a => a.Id == id);
    }

    [Fact]
    public async Task DeleteByCommandAsync_RemovesByName()
    {
        await _sut.AddAsync(CreateAlias("findme", "action"));
        await _sut.DeleteByCommandAsync(_characterId, "FINDME"); // case insensitive

        var result = await _sut.GetByCharacterAsync(_characterId);
        result.Should().NotContain(a => a.Command == "findme");
    }

    private AliasEntity CreateAlias(string command, string action) => new()
    {
        CharacterId = _characterId,
        Command = command,
        Action = action,
        Enabled = true
    };
}
