using FluentAssertions;
using Omnimud.Data.Entities;
using Omnimud.Data.Repositories;

namespace Omnimud.Data.Tests.Repositories;

public sealed class SqliteCharacterRepositoryTests : IAsyncLifetime
{
    private readonly InMemoryDatabaseFixture _db = new();
    private SqliteCharacterRepository _sut = null!;
    private int _mudId;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        _sut = new SqliteCharacterRepository(_db);

        // Create a MUD to link characters to
        var mudRepo = new SqliteMudRepository(_db);
        _mudId = await mudRepo.AddAsync(new MudEntity { Name = "TestMud", Host = "host", Port = 4000 });
    }

    public Task DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task AddAsync_ReturnsNewId()
    {
        var id = await _sut.AddAsync(CreateCharacter("Warrior"));
        id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsCharacter()
    {
        var id = await _sut.AddAsync(CreateCharacter("Mage"));

        var result = await _sut.GetByIdAsync(id);
        result.Should().NotBeNull();
        result!.Name.Should().Be("Mage");
        result.MudId.Should().Be(_mudId);
    }

    [Fact]
    public async Task GetByMudAsync_ReturnsOnlyMudCharacters()
    {
        await _sut.AddAsync(CreateCharacter("Char1"));
        await _sut.AddAsync(CreateCharacter("Char2"));

        var result = await _sut.GetByMudAsync(_mudId);
        result.Should().HaveCountGreaterThanOrEqualTo(2);
        result.Should().AllSatisfy(c => c.MudId.Should().Be(_mudId));
    }

    [Fact]
    public async Task SetDefaultAsync_ClearsPreviousDefault()
    {
        var id1 = await _sut.AddAsync(CreateCharacter("First"));
        var id2 = await _sut.AddAsync(CreateCharacter("Second"));

        await _sut.SetDefaultAsync(_mudId, id1);
        await _sut.SetDefaultAsync(_mudId, id2);

        var char1 = await _sut.GetByIdAsync(id1);
        var char2 = await _sut.GetByIdAsync(id2);
        char1!.IsDefault.Should().BeFalse();
        char2!.IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_RemovesCharacter()
    {
        var id = await _sut.AddAsync(CreateCharacter("ToDelete"));
        await _sut.DeleteAsync(id);

        var result = await _sut.GetByIdAsync(id);
        result.Should().BeNull();
    }

    [Fact]
    public async Task CascadeDelete_WhenMudDeleted_CharactersRemoved()
    {
        await _sut.AddAsync(CreateCharacter("Orphan"));

        var mudRepo = new SqliteMudRepository(_db);
        await mudRepo.DeleteAsync(_mudId);

        var result = await _sut.GetByMudAsync(_mudId);
        result.Should().BeEmpty();
    }

    private CharacterEntity CreateCharacter(string name) => new()
    {
        MudId = _mudId,
        Name = name
    };
}
