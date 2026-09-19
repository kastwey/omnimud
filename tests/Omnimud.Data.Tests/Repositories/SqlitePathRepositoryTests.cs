using FluentAssertions;
using Omnimud.Data.Entities;
using Omnimud.Data.Repositories;

namespace Omnimud.Data.Tests.Repositories;

public sealed class SqlitePathRepositoryTests : IAsyncLifetime
{
    private readonly InMemoryDatabaseFixture _db = new();
    private SqlitePathRepository _sut = null!;
    private int _characterId;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        _sut = new SqlitePathRepository(_db);

        var mudRepo = new SqliteMudRepository(_db);
        var mudId = await mudRepo.AddAsync(new MudEntity { Name = "TestMud", Host = "host", Port = 4000 });

        var charRepo = new SqliteCharacterRepository(_db);
        _characterId = await charRepo.AddAsync(new CharacterEntity { MudId = mudId, Name = "TestChar" });
    }

    public Task DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task AddAsync_ReturnsId()
    {
        var id = await _sut.AddAsync(CreatePath("Tavern", "n;n;e"));
        id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetByCharacterAsync_ReturnsPaths()
    {
        await _sut.AddAsync(CreatePath("Market", "s;s;w"));
        await _sut.AddAsync(CreatePath("Guild", "n;e;e"));

        var result = await _sut.GetByCharacterAsync(_characterId);
        result.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task UpdateAsync_ModifiesPath()
    {
        var id = await _sut.AddAsync(CreatePath("OldName", "n;s"));

        var paths = await _sut.GetByCharacterAsync(_characterId);
        var path = paths.First(p => p.Id == id);
        path.Name = "NewName";
        path.Path = "e;w";
        await _sut.UpdateAsync(path);

        var updated = (await _sut.GetByCharacterAsync(_characterId)).First(p => p.Id == id);
        updated.Name.Should().Be("NewName");
        updated.Path.Should().Be("e;w");
    }

    [Fact]
    public async Task DeleteAsync_RemovesPath()
    {
        var id = await _sut.AddAsync(CreatePath("ToDelete", "n"));
        await _sut.DeleteAsync(id);

        var result = await _sut.GetByCharacterAsync(_characterId);
        result.Should().NotContain(p => p.Id == id);
    }

    private PathEntity CreatePath(string name, string path) => new()
    {
        CharacterId = _characterId,
        Name = name,
        Path = path
    };
}
