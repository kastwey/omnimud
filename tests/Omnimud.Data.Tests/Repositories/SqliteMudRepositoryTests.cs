using FluentAssertions;
using Omnimud.Data.Entities;
using Omnimud.Data.Repositories;

namespace Omnimud.Data.Tests.Repositories;

public sealed class SqliteMudRepositoryTests : IAsyncLifetime
{
    private readonly InMemoryDatabaseFixture _db = new();
    private SqliteMudRepository _sut = null!;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        _sut = new SqliteMudRepository(_db);
    }

    public Task DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task AddAsync_ReturnsNewId()
    {
        var mud = CreateMud("TestMud");
        var id = await _sut.AddAsync(mud);
        id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsInsertedMud()
    {
        var mud = CreateMud("FindMe");
        var id = await _sut.AddAsync(mud);

        var result = await _sut.GetByIdAsync(id);

        result.Should().NotBeNull();
        result!.Name.Should().Be("FindMe");
        result.Host.Should().Be("mud.example.com");
        result.Port.Should().Be(4000);
    }

    [Fact]
    public async Task GetByNameAsync_IsCaseInsensitive()
    {
        var mud = CreateMud("CaseMud");
        await _sut.AddAsync(mud);

        var result = await _sut.GetByNameAsync("casemud");
        result.Should().NotBeNull();
        result!.Name.Should().Be("CaseMud");
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllMuds()
    {
        await _sut.AddAsync(CreateMud("Alpha"));
        await _sut.AddAsync(CreateMud("Beta"));

        var all = await _sut.GetAllAsync();
        all.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task UpdateAsync_ModifiesFields()
    {
        var mud = CreateMud("Original");
        var id = await _sut.AddAsync(mud);

        var loaded = await _sut.GetByIdAsync(id);
        loaded!.Host = "new.host.com";
        loaded.Port = 5000;
        await _sut.UpdateAsync(loaded);

        var updated = await _sut.GetByIdAsync(id);
        updated!.Host.Should().Be("new.host.com");
        updated.Port.Should().Be(5000);
    }

    [Fact]
    public async Task DeleteAsync_RemovesMud()
    {
        var id = await _sut.AddAsync(CreateMud("ToDelete"));
        await _sut.DeleteAsync(id);

        var result = await _sut.GetByIdAsync(id);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _sut.GetByIdAsync(999);
        result.Should().BeNull();
    }

    private static MudEntity CreateMud(string name) => new()
    {
        Name = name,
        Host = "mud.example.com",
        Port = 4000
    };
}
