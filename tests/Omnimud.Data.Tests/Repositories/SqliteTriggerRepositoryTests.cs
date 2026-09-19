using FluentAssertions;
using Omnimud.Data.Entities;
using Omnimud.Data.Repositories;

namespace Omnimud.Data.Tests.Repositories;

public sealed class SqliteTriggerRepositoryTests : IAsyncLifetime
{
    private readonly InMemoryDatabaseFixture _db = new();
    private SqliteTriggerRepository _sut = null!;
    private int _characterId;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        _sut = new SqliteTriggerRepository(_db);

        var mudRepo = new SqliteMudRepository(_db);
        var mudId = await mudRepo.AddAsync(new MudEntity { Name = "TestMud", Host = "host", Port = 4000 });

        var charRepo = new SqliteCharacterRepository(_db);
        _characterId = await charRepo.AddAsync(new CharacterEntity { MudId = mudId, Name = "TestChar" });
    }

    public Task DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task AddAsync_StoresTrigger()
    {
        var trigger = CreateTrigger("t1", "HpAlert", "HP: %d");
        await _sut.AddAsync(trigger);

        var result = await _sut.GetByIdAsync("t1");
        result.Should().NotBeNull();
        result!.Name.Should().Be("HpAlert");
    }

    [Fact]
    public async Task GetByCharacterAsync_ReturnsCreationOrder_WhateverThePriority()
    {
        await _sut.AddAsync(CreateTrigger("low", "Low", "low*", priority: 10));
        await _sut.AddAsync(CreateTrigger("high", "High", "high*", priority: 90));
        await _sut.AddAsync(CreateTrigger("mid", "Mid", "mid*", priority: 50));

        var result = await _sut.GetByCharacterAsync(_characterId);

        result.Select(t => t.Id).Should().Equal("low", "high", "mid");
        result.Select(t => t.SortOrder).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task AddAsync_WritesAssignedSortOrderBackToEntity()
    {
        var first = CreateTrigger("a", "A", "a");
        var second = CreateTrigger("b", "B", "b");
        await _sut.AddAsync(first);
        await _sut.AddAsync(second);

        first.SortOrder.Should().Be(1);
        second.SortOrder.Should().Be(2);
    }

    [Fact]
    public async Task UpdateAsync_WithSortOrderZero_KeepsStoredPosition()
    {
        await _sut.AddAsync(CreateTrigger("a", "A", "a"));
        await _sut.AddAsync(CreateTrigger("b", "B", "b"));

        var detached = CreateTrigger("b", "B renamed", "b");
        await _sut.UpdateAsync(detached);

        var stored = await _sut.GetByIdAsync("b");
        stored!.Name.Should().Be("B renamed");
        stored.SortOrder.Should().Be(2);
    }

    [Fact]
    public async Task ReorderAsync_RenumbersAndKeepsUnlistedAtTheEnd()
    {
        await _sut.AddAsync(CreateTrigger("a", "A", "a"));
        await _sut.AddAsync(CreateTrigger("b", "B", "b"));
        await _sut.AddAsync(CreateTrigger("c", "C", "c"));

        await _sut.ReorderAsync(_characterId, ["c", "a", "does-not-exist"]);

        var result = await _sut.GetByCharacterAsync(_characterId);
        result.Select(t => t.Id).Should().Equal("c", "a", "b");
        result.Select(t => t.SortOrder).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task AddAsync_SoundOnlyTrigger_AllowsEmptyActionAndActionType3()
    {
        var trigger = CreateTrigger("snd", "Sound", "ding");
        trigger.Action = string.Empty;
        trigger.ActionType = 3;
        trigger.Sound = "ding.wav";
        await _sut.AddAsync(trigger);

        var stored = await _sut.GetByIdAsync("snd");
        stored!.Action.Should().BeEmpty();
        stored.ActionType.Should().Be(3);
    }

    [Fact]
    public async Task SetEnabledAsync_TogglesEnabled()
    {
        await _sut.AddAsync(CreateTrigger("toggler", "Toggle", "pattern"));
        await _sut.SetEnabledAsync("toggler", false);

        var result = await _sut.GetByIdAsync("toggler");
        result!.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_ModifiesFields()
    {
        await _sut.AddAsync(CreateTrigger("upd", "Original", "old pattern"));

        var trigger = await _sut.GetByIdAsync("upd");
        trigger!.Pattern = "new pattern";
        trigger.Name = "Updated";
        await _sut.UpdateAsync(trigger);

        var updated = await _sut.GetByIdAsync("upd");
        updated!.Pattern.Should().Be("new pattern");
        updated.Name.Should().Be("Updated");
    }

    [Fact]
    public async Task DeleteAsync_RemovesTrigger()
    {
        await _sut.AddAsync(CreateTrigger("del", "Delete", "p"));
        await _sut.DeleteAsync("del");

        var result = await _sut.GetByIdAsync("del");
        result.Should().BeNull();
    }

    private TriggerEntity CreateTrigger(string id, string name, string pattern, int priority = 50) => new()
    {
        Id = id,
        CharacterId = _characterId,
        Name = name,
        Pattern = pattern,
        PatternType = 0,
        Action = "send hello",
        ActionType = 0,
        Enabled = true,
        CaseSensitive = false,
        Priority = priority
    };
}
