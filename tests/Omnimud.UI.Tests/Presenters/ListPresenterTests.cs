using Omnimud.Data.Entities;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Tests.Presenters;

/// <summary>AliasListPresenter, TriggerListPresenter and PathListPresenter, with in-memory repositories and with SQLite.</summary>
public class ListPresenterTests
{
    private readonly RecordingPrompts _prompts = new();
    private readonly RecordingAnnouncer _announcer = new();

    private AliasListPresenter Aliases(MemoryAliasRepository repository, IListSortStore? store = null) =>
        new(repository, 1, _prompts, store, _announcer);

    private TriggerListPresenter Triggers(MemoryTriggerRepository repository, IListSortStore? store = null) =>
        new(repository, 1, _prompts, store, _announcer);

    // ── Loading, sorting, selection ──

    [Fact]
    public async Task Load_SortsByCommand_AndSelectsTheFirst()
    {
        var sut = Aliases(new MemoryAliasRepository(("z", "zeta"), ("a", "alfa"), ("M", "eme")));
        var changes = 0;
        sut.Changed += (_, _) => changes++;

        await sut.LoadAsync();

        sut.Items.Select(a => a.Command).Should().Equal("a", "M", "z");
        sut.SelectedIndex.Should().Be(0);
        changes.Should().Be(1);
    }

    [Fact]
    public async Task Load_EmptyList_HasNoSelection()
    {
        var sut = Aliases(new MemoryAliasRepository());
        await sut.LoadAsync();
        sut.Selected.Should().BeNull();
        (await sut.RemoveSelectedAsync()).Should().BeFalse();
        (await sut.ToggleSelectedAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task SortBy_KeepsTheSelectedElement_TogglesDirection_AndAnnounces()
    {
        var sut = Aliases(new MemoryAliasRepository(("a", "tres"), ("b", "uno"), ("c", "dos")));
        await sut.LoadAsync();
        sut.Select(2); // "c"

        await sut.SortByAsync(AliasListPresenter.ColAction);
        sut.Items.Select(a => a.Command).Should().Equal("c", "a", "b");
        sut.Selected!.Command.Should().Be("c");
        _announcer.Spoken.Should().Contain(string.Format(Strings.Lst_SortedAscending, Strings.AliasList_ColAction));

        await sut.SortByAsync(AliasListPresenter.ColAction);
        sut.Items.Select(a => a.Command).Should().Equal("b", "a", "c");
        sut.Selected!.Command.Should().Be("c");
        sut.Sort.Should().Be(new ListSort(AliasListPresenter.ColAction, true));
    }

    [Fact]
    public async Task SortBy_UnknownColumn_IsIgnored()
    {
        var sut = Aliases(new MemoryAliasRepository(("a", "x")));
        await sut.LoadAsync();
        await sut.SortByAsync("nope");
        sut.Sort.Column.Should().Be(AliasListPresenter.ColCommand);
    }

    [Fact]
    public async Task Sort_IsRememberedPerCharacterAndPerList()
    {
        var store = new MemoryListSortStore();
        var first = Aliases(new MemoryAliasRepository(("a", "x")), store);
        await first.LoadAsync();
        await first.SortByAsync(AliasListPresenter.ColEnabled);

        var again = Aliases(new MemoryAliasRepository(("a", "x")), store);
        await again.LoadAsync();
        again.Sort.Should().Be(new ListSort(AliasListPresenter.ColEnabled));

        var otherCharacter = new AliasListPresenter(new MemoryAliasRepository(), 2, _prompts, store, _announcer);
        await otherCharacter.LoadAsync();
        otherCharacter.Sort.Column.Should().Be(AliasListPresenter.ColCommand);

        var otherList = Triggers(new MemoryTriggerRepository("t"), store);
        await otherList.LoadAsync();
        otherList.Sort.Column.Should().Be(TriggerListPresenter.ColOrder);
    }

    [Fact]
    public async Task Sort_StoredColumnThatNoLongerExists_FallsBackToTheDefault()
    {
        var store = new MemoryListSortStore();
        await store.SaveAsync(1, "aliases", new ListSort("gone"));
        var sut = Aliases(new MemoryAliasRepository(("a", "x")), store);
        await sut.LoadAsync();
        sut.Sort.Column.Should().Be(AliasListPresenter.ColCommand);
    }

    [Theory]
    [InlineData("name;asc", "name", false)]
    [InlineData("name;desc", "name", true)]
    public void ListSort_RoundTrips(string text, string column, bool descending)
    {
        ListSort.TryParse(text).Should().Be(new ListSort(column, descending));
        new ListSort(column, descending).Serialize().Should().Be(text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("name")]
    [InlineData("name;up")]
    [InlineData(";asc")]
    public void ListSort_Garbage_IsNull(string? text) => ListSort.TryParse(text).Should().BeNull();

    // ── Add / edit / remove ──

    [Fact]
    public async Task Add_SelectsTheNewElement()
    {
        var sut = Aliases(new MemoryAliasRepository(("a", "alfa"), ("z", "zeta")));
        await sut.LoadAsync();

        var added = await sut.AddAsync((current, isNew, all) =>
        {
            current.Should().BeNull();
            isNew.Should().BeTrue();
            all.Should().HaveCount(2);
            return new AliasEntity { Command = "m", Action = "eme" };
        });

        added.Should().BeTrue();
        sut.Items.Select(a => a.Command).Should().Equal("a", "m", "z");
        sut.Selected!.Command.Should().Be("m");
        sut.DataChanged.Should().BeTrue();
    }

    [Fact]
    public async Task Add_Cancelled_ChangesNothing()
    {
        var sut = Aliases(new MemoryAliasRepository(("a", "alfa")));
        await sut.LoadAsync();
        (await sut.AddAsync((_, _, _) => null)).Should().BeFalse();
        sut.DataChanged.Should().BeFalse();
    }

    [Fact]
    public async Task Add_Duplicate_IsAMessage_AndTheEditorReopensWithWhatWasTyped()
    {
        var sut = Aliases(new MemoryAliasRepository(("a", "alfa")));
        await sut.LoadAsync();
        var calls = new List<AliasEntity?>();

        var added = await sut.AddAsync((current, _, _) =>
        {
            calls.Add(current);
            return calls.Count == 1 ? new AliasEntity { Command = "a", Action = "otra" } : new AliasEntity { Command = "b", Action = "otra" };
        });

        added.Should().BeTrue();
        _prompts.Warnings.Should().Equal(string.Format(Strings.AliasEdit_ErrDuplicate, "a"));
        calls.Should().HaveCount(2);
        calls[0].Should().BeNull();
        calls[1]!.Command.Should().Be("a");
        sut.Items.Select(a => a.Command).Should().Equal("a", "b");
    }

    [Fact]
    public async Task Edit_Duplicate_ThenCancel_LeavesTheListAsItWas()
    {
        var sut = Aliases(new MemoryAliasRepository(("a", "alfa"), ("b", "beta")));
        await sut.LoadAsync();
        sut.Select(1);
        var calls = 0;

        var edited = await sut.EditSelectedAsync((current, isNew, _) =>
        {
            isNew.Should().BeFalse();
            return ++calls == 1 ? new AliasEntity { Id = current!.Id, CharacterId = 1, Command = "a", Action = "beta" } : null;
        });

        edited.Should().BeFalse();
        _prompts.Warnings.Should().ContainSingle();
        sut.Items.Select(a => a.Command).Should().Equal("a", "b");
    }

    [Fact]
    public async Task Edit_KeepsTheElementSelected_EvenIfItMovesInTheOrder()
    {
        var sut = Aliases(new MemoryAliasRepository(("a", "alfa"), ("b", "beta"), ("c", "gamma")));
        await sut.LoadAsync();
        sut.Select(0);

        await sut.EditSelectedAsync((current, _, _) => new AliasEntity { Id = current!.Id, CharacterId = 1, Command = "zz", Action = "alfa" });

        sut.Items.Select(a => a.Command).Should().Equal("b", "c", "zz");
        sut.SelectedIndex.Should().Be(2);
    }

    [Theory]
    [InlineData(0, "b")]
    [InlineData(1, "c")]
    [InlineData(2, "b")] // the last one: the neighbour is the new last
    public async Task Remove_SelectsTheNeighbour_AndAnnounces(int index, string expectedSelection)
    {
        var sut = Aliases(new MemoryAliasRepository(("a", "1"), ("b", "2"), ("c", "3")));
        await sut.LoadAsync();
        sut.Select(index);
        var removed = sut.Selected!.Command;

        (await sut.RemoveSelectedAsync()).Should().BeTrue();

        _prompts.Confirms.Should().Equal(string.Format(Strings.AliasList_ConfirmRemove, removed));
        sut.Items.Should().HaveCount(2);
        sut.Selected!.Command.Should().Be(expectedSelection);
        _announcer.Spoken.Should().Equal(string.Format(Strings.Lst_Removed, removed));
    }

    [Fact]
    public async Task Remove_AnsweredNo_RemovesNothing()
    {
        _prompts.DefaultConfirm = false;
        var sut = Aliases(new MemoryAliasRepository(("a", "1")));
        await sut.LoadAsync();
        (await sut.RemoveSelectedAsync()).Should().BeFalse();
        sut.Items.Should().HaveCount(1);
        sut.DataChanged.Should().BeFalse();
    }

    [Fact]
    public async Task Remove_TheOnlyElement_LeavesNoSelection()
    {
        var sut = Aliases(new MemoryAliasRepository(("a", "1")));
        await sut.LoadAsync();
        await sut.RemoveSelectedAsync();
        sut.SelectedIndex.Should().Be(-1);
    }

    // ── Enable / disable ──

    [Fact]
    public async Task ToggleAlias_IsStored_Announced_AndKeepsTheSelection()
    {
        var repository = new MemoryAliasRepository(("a", "1"), ("b", "2"));
        var sut = Aliases(repository);
        await sut.LoadAsync();
        sut.Select(1);

        await sut.ToggleSelectedAsync();
        (await repository.GetByCharacterAsync(1)).Single(a => a.Command == "b").Enabled.Should().BeFalse();
        sut.Selected!.Command.Should().Be("b");
        sut.Status.Should().Be(string.Format(Strings.AliasList_Disabled, "b"));

        await sut.ToggleSelectedAsync();
        _announcer.Spoken.Should().Equal(string.Format(Strings.AliasList_Disabled, "b"), string.Format(Strings.AliasList_Enabled, "b"));
    }

    [Fact]
    public async Task ToggleTrigger_IsStored_Announced_AndKeepsTheSelection()
    {
        var repository = new MemoryTriggerRepository("uno", "dos", "tres");
        var sut = Triggers(repository);
        await sut.LoadAsync();
        sut.Select(1);

        (await sut.ToggleSelectedAsync()).Should().BeTrue();

        repository.Items.Single(t => t.Name == "dos").Enabled.Should().BeFalse();
        sut.Selected!.Name.Should().Be("dos");
        sut.Selected.Enabled.Should().BeFalse();
        _announcer.Spoken.Should().Equal(string.Format(Strings.TrigList_Disabled, "dos"));

        await sut.ToggleSelectedAsync();
        _announcer.Spoken.Last().Should().Be(string.Format(Strings.TrigList_Enabled, "dos"));
    }

    // ── Reordering ──

    [Fact]
    public async Task MoveTrigger_Down_ThenUp_ChangesTheStoredOrder_AndFollowsTheElement()
    {
        var repository = new MemoryTriggerRepository("uno", "dos", "tres");
        var sut = Triggers(repository);
        await sut.LoadAsync();

        (await sut.MoveSelectedAsync(+1)).Should().BeTrue();
        sut.Items.Select(t => t.Name).Should().Equal("dos", "uno", "tres");
        sut.SelectedIndex.Should().Be(1);
        repository.Items.OrderBy(t => t.SortOrder).Select(t => t.Name).Should().Equal("dos", "uno", "tres");
        _announcer.Spoken.Last().Should().Be(string.Format(Strings.TrigList_Moved, "uno", 2, 3));

        (await sut.MoveSelectedAsync(-1)).Should().BeTrue();
        sut.Items.Select(t => t.Name).Should().Equal("uno", "dos", "tres");
        sut.SelectedIndex.Should().Be(0);
    }

    [Fact]
    public async Task MoveTrigger_PastTheEnds_OnlyAnnounces()
    {
        var sut = Triggers(new MemoryTriggerRepository("uno", "dos"));
        await sut.LoadAsync();

        (await sut.MoveSelectedAsync(-1)).Should().BeFalse();
        _announcer.Spoken.Last().Should().Be(string.Format(Strings.TrigList_AlreadyFirst, "uno"));

        sut.Select(1);
        (await sut.MoveSelectedAsync(+1)).Should().BeFalse();
        _announcer.Spoken.Last().Should().Be(string.Format(Strings.TrigList_AlreadyLast, "dos"));
        sut.DataChanged.Should().BeFalse();
    }

    [Fact]
    public async Task MoveTrigger_WhileSortedByAnotherColumn_ExplainsWhyNot()
    {
        var repository = new MemoryTriggerRepository("b", "a");
        var sut = Triggers(repository);
        await sut.LoadAsync();
        await sut.SortByAsync(TriggerListPresenter.ColName);
        sut.CanReorder.Should().BeFalse();

        (await sut.MoveSelectedAsync(+1)).Should().BeFalse();

        _announcer.Spoken.Last().Should().Be(Strings.TrigList_MoveNeedsOrder);
        repository.Items.OrderBy(t => t.SortOrder).Select(t => t.Name).Should().Equal("b", "a");
    }

    [Fact]
    public async Task TriggerSort_ByPriority_PutsTheHighestFirst()
    {
        var repository = new MemoryTriggerRepository("bajo", "alto");
        repository.Items[0].Priority = 10;
        repository.Items[1].Priority = 90;
        var sut = Triggers(repository);
        await sut.LoadAsync();
        await sut.SortByAsync(TriggerListPresenter.ColPriority);
        sut.Items.Select(t => t.Name).Should().Equal("alto", "bajo");
    }

    [Fact]
    public async Task AddTrigger_AppendsAtTheEnd_WithIdAndDates()
    {
        var repository = new MemoryTriggerRepository("uno");
        var sut = Triggers(repository);
        await sut.LoadAsync();

        await sut.AddAsync((_, _, _) => new TriggerEntity { Id = "", Name = "dos", Pattern = "p", Action = "a", SortOrder = 99 });

        var added = repository.Items.Single(t => t.Name == "dos");
        added.Id.Should().NotBeEmpty();
        added.CharacterId.Should().Be(1);
        added.SortOrder.Should().Be(2);
        added.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        sut.Selected!.Name.Should().Be("dos");
    }

    // ── Paths ──

    [Fact]
    public async Task Paths_DuplicateName_IsAMessage()
    {
        var sut = new PathListPresenter(new MemoryPathRepository(("plaza", "3n")), 1, _prompts, null, _announcer);
        await sut.LoadAsync();
        var calls = 0;

        await sut.AddAsync((_, _, _) => ++calls == 1 ? new PathEntity { Name = "plaza", Path = "2e" } : null);

        _prompts.Warnings.Should().Equal(string.Format(Strings.PathEdit_ErrDuplicateName, "plaza"));
        sut.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Paths_SortByRoute()
    {
        var sut = new PathListPresenter(new MemoryPathRepository(("a", "9s"), ("b", "2e")), 1, _prompts, null, _announcer);
        await sut.LoadAsync();
        await sut.SortByAsync(PathListPresenter.ColPath);
        sut.Items.Select(p => p.Name).Should().Equal("b", "a");
    }

    // ── With real SQLite ──

    [Fact]
    public async Task Sqlite_Aliases_AddDuplicateToggleRemove()
    {
        using var db = new ListsDatabase();
        var sut = new AliasListPresenter(db.Aliases, db.CharacterId, _prompts, new OptionListSortStore(db.Options), _announcer);
        await sut.LoadAsync();

        await sut.AddAsync((_, _, _) => new AliasEntity { Command = "k", Action = "matar" });
        var attempts = 0;
        await sut.AddAsync((_, _, _) => ++attempts == 1 ? new AliasEntity { Command = "k", Action = "otra" } : null);

        _prompts.Warnings.Should().Equal(string.Format(Strings.AliasEdit_ErrDuplicate, "k"));
        (await db.Aliases.GetByCharacterAsync(db.CharacterId)).Should().ContainSingle();

        await sut.ToggleSelectedAsync();
        (await db.Aliases.GetByCharacterAsync(db.CharacterId)).Single().Enabled.Should().BeFalse();

        await sut.RemoveSelectedAsync();
        (await db.Aliases.GetByCharacterAsync(db.CharacterId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Sqlite_Triggers_ReorderAndToggle_ArePersisted()
    {
        using var db = new ListsDatabase();
        foreach (var name in new[] { "uno", "dos", "tres" })
            await db.Triggers.AddAsync(db.NewTrigger(db.CharacterId, name));

        var sut = new TriggerListPresenter(db.Triggers, db.CharacterId, _prompts, new OptionListSortStore(db.Options), _announcer);
        await sut.LoadAsync();
        sut.Select(2);
        await sut.MoveSelectedAsync(-1);
        await sut.MoveSelectedAsync(-1);
        await sut.ToggleSelectedAsync();

        var stored = await db.Triggers.GetByCharacterAsync(db.CharacterId);
        stored.Select(t => t.Name).Should().Equal("tres", "uno", "dos");
        stored[0].Enabled.Should().BeFalse();
        sut.Selected!.Name.Should().Be("tres");
    }

    [Fact]
    public async Task Sqlite_Paths_DuplicateName_IsAMessage_NotAnException()
    {
        using var db = new ListsDatabase();
        await db.Paths.AddAsync(new PathEntity { CharacterId = db.CharacterId, Name = "plaza", Path = "3n" });
        var sut = new PathListPresenter(db.Paths, db.CharacterId, _prompts, null, _announcer);
        await sut.LoadAsync();
        var attempts = 0;

        var act = () => sut.AddAsync((_, _, _) => ++attempts == 1 ? new PathEntity { Name = "plaza", Path = "e" } : null);

        await act.Should().NotThrowAsync();
        _prompts.Warnings.Should().ContainSingle();
    }

    [Fact]
    public async Task Sqlite_SortOrder_IsStoredInTheDatabase_WithoutTouchingTheCharactersOptions()
    {
        using var db = new ListsDatabase();
        var store = new OptionListSortStore(db.Options);

        await store.SaveAsync(db.CharacterId, "triggers", new ListSort("name", true));

        (await store.LoadAsync(db.CharacterId, "triggers")).Should().Be(new ListSort("name", true));
        (await store.LoadAsync(db.CharacterId, "aliases")).Should().BeNull();
        (await store.LoadAsync(db.SecondCharacterId, "triggers")).Should().BeNull();
        // Scope 2 is "the character has its own options": interface state must not switch that on.
        (await db.Options.HasAnyAsync(2, db.CharacterId)).Should().BeFalse();
    }
}
