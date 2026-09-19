using FluentAssertions;
using Omnimud.Data.Repositories;

namespace Omnimud.Data.Tests.Repositories;

public sealed class SqliteOptionRepositoryTests : IAsyncLifetime
{
    private readonly InMemoryDatabaseFixture _db = new();
    private SqliteOptionRepository _sut = null!;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        _sut = new SqliteOptionRepository(_db);
    }

    public Task DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task SetValueAsync_StoresValue()
    {
        await _sut.SetValueAsync(0, null, "theme", "dark");

        var result = await _sut.GetValueAsync(0, null, "theme");
        result.Should().Be("dark");
    }

    [Fact]
    public async Task SetValueAsync_UpsertsValue()
    {
        await _sut.SetValueAsync(0, null, "font", "Courier");
        await _sut.SetValueAsync(0, null, "font", "Consolas");

        var result = await _sut.GetValueAsync(0, null, "font");
        result.Should().Be("Consolas");
    }

    [Fact]
    public async Task GetValueAsync_ReturnsNull_WhenKeyMissing()
    {
        var result = await _sut.GetValueAsync(0, null, "nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByScope_ReturnsAllInScope()
    {
        await _sut.SetValueAsync(1, 5, "opt1", "val1");
        await _sut.SetValueAsync(1, 5, "opt2", "val2");

        var result = await _sut.GetByScope(1, 5);
        result.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task DeleteAsync_RemovesSingleKey()
    {
        await _sut.SetValueAsync(0, null, "toDelete", "value");
        await _sut.DeleteAsync(0, null, "toDelete");

        var result = await _sut.GetValueAsync(0, null, "toDelete");
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAllByScopeAsync_RemovesAll()
    {
        await _sut.SetValueAsync(2, 10, "a", "1");
        await _sut.SetValueAsync(2, 10, "b", "2");
        await _sut.DeleteAllByScopeAsync(2, 10);

        var result = await _sut.GetByScope(2, 10);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ScopesAreIsolated()
    {
        await _sut.SetValueAsync(0, null, "key", "global");
        await _sut.SetValueAsync(1, 1, "key", "mud-specific");

        var global = await _sut.GetValueAsync(0, null, "key");
        var mudSpecific = await _sut.GetValueAsync(1, 1, "key");

        global.Should().Be("global");
        mudSpecific.Should().Be("mud-specific");
    }
}
