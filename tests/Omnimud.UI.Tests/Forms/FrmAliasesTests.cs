using Omnimud.Data.Entities;
using Omnimud.Data.Exchange;
using Omnimud.UI.Forms;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Tests.Presenters;

namespace Omnimud.UI.Tests.Forms;

public class FrmAliasesTests
{
    private readonly RecordingPrompts _prompts = new();
    private readonly RecordingAnnouncer _announcer = new();

    private FrmAliases Create(MemoryAliasRepository repository, IListSortStore? store = null)
    {
        var form = new FrmAliases(repository, 1, "Gandalf", _prompts, _announcer, store);
        _ = form.List.Handle;
        StaPump.Wait(form.InitializeAsync());
        return form;
    }

    private static MemoryAliasRepository Three() => new(("a", "alfa"), ("b", "beta"), ("c", "gamma"));

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void PassesTheAccessibilityAudit(string culture) => Sta.Run(() =>
    {
        ListFormsTestSupport.AssertAccessible(culture, () => Create(Three()));
        ListFormsTestSupport.AssertAccessible(culture, () => new FrmAliases(Three(), 1, "Gandalf"));
        // With a disabled alias selected the toggle button reads "Enable".
        ListFormsTestSupport.AssertAccessible(culture, () => Create(Three()), form =>
        {
            form.HandleListKey(Keys.Space);
            StaPump.Wait(form.LastAction);
            form.Find<Button>("_btnToggle").Text.Should().Be(Strings.Lst_Enable);
        });
    });

    [Fact]
    public void Title_Label_ListName_AndColumns_AreLocalized() => Sta.Run(() =>
    {
        ListFormsTestSupport.UseCulture("es");
        using var form = Create(Three());

        form.Text.Should().Be("Alias de Gandalf");
        form.Find<Label>("_lblList").Text.Should().Be("A&lias:");
        form.List.AccessibleName.Should().Be("Alias");
        form.List.Columns.Cast<ColumnHeader>().Select(c => c.Text).Should().Equal("Comando", "Acción", "Activado");
        form.List.Items[0].SubItems.Cast<ListViewItem.ListViewSubItem>().Select(s => s.Text).Should().Equal("a", "alfa", "Sí");
        form.CancelButton.Should().BeSameAs(form.Find<Button>("_btnClose"));
        form.AcceptButton.Should().BeSameAs(form.Find<Button>("_btnEdit"));
    });

    [Fact]
    public void AfterLoading_TheFirstElementIsSelected() => Sta.Run(() =>
    {
        using var form = Create(Three());
        form.List.SelectedTexts().Should().Equal("a");
        form.Find<Button>("_btnEdit").Enabled.Should().BeTrue();
    });

    [Fact]
    public void EmptyList_DisablesWhatNeedsASelection() => Sta.Run(() =>
    {
        using var form = Create(new MemoryAliasRepository());
        form.Find<Button>("_btnEdit").Enabled.Should().BeFalse();
        form.Find<Button>("_btnRemove").Enabled.Should().BeFalse();
        form.Find<Button>("_btnToggle").Enabled.Should().BeFalse();
        form.Find<Button>("_btnAdd").Enabled.Should().BeTrue();
    });

    [Fact]
    public void WithoutExchangeService_ImportAndExportAreDisabled() => Sta.Run(() =>
    {
        using var form = Create(Three());
        form.Find<Button>("_btnImport").Enabled.Should().BeFalse();
        form.Find<Button>("_btnExport").Enabled.Should().BeFalse();
    });

    [Fact]
    public void Space_TogglesTheAlias_AnnouncesIt_AndKeepsTheSelection() => Sta.Run(() =>
    {
        ListFormsTestSupport.UseCulture("es");
        var repository = Three();
        using var form = Create(repository);
        form.List.Items[1].Selected = true;

        form.HandleListKey(Keys.Space).Should().BeTrue();
        StaPump.Wait(form.LastAction);

        _announcer.Spoken.Should().Equal("Alias b desactivado");
        form.StatusText.Should().Be("Alias b desactivado");
        form.List.SelectedTexts().Should().Equal("b");
        form.List.Items[1].Focused.Should().BeTrue();
        form.List.Items[1].SubItems[2].Text.Should().Be("No");
        form.Find<Button>("_btnToggle").Text.Should().Be("Acti&var");

        form.Find<Button>("_btnToggle").Press();
        StaPump.Wait(form.LastAction);
        _announcer.Spoken.Last().Should().Be("Alias b activado");
        form.Find<Button>("_btnToggle").Text.Should().Be("Desacti&var");
    });

    [Fact]
    public void Delete_RemovesAfterConfirming_AndSelectsTheNeighbour() => Sta.Run(() =>
    {
        using var form = Create(Three());
        form.List.Items[1].Selected = true;

        form.HandleListKey(Keys.Delete).Should().BeTrue();
        StaPump.Wait(form.LastAction);

        _prompts.Confirms.Should().ContainSingle();
        form.List.RowTexts().Should().Equal("a", "c");
        form.List.SelectedTexts().Should().Equal("c");
        form.Changed.Should().BeTrue();
    });

    [Fact]
    public void Insert_Adds_F2_Edits_ThroughTheEditor_AndTheEditedElementStaysSelected() => Sta.Run(() =>
    {
        using var form = Create(Three());
        var opened = new List<(string? Command, bool IsNew)>();
        form.EditorOverride = (current, isNew, _) =>
        {
            opened.Add((current?.Command, isNew));
            return isNew
                ? new AliasEntity { Command = "bb", Action = "nuevo" }
                : new AliasEntity { Id = current!.Id, CharacterId = 1, Command = "zz", Action = current.Action };
        };

        form.HandleListKey(Keys.Insert).Should().BeTrue();
        StaPump.Wait(form.LastAction);
        form.List.RowTexts().Should().Equal("a", "b", "bb", "c");
        form.List.SelectedTexts().Should().Equal("bb");

        form.List.Items[0].Selected = true;
        form.HandleListKey(Keys.F2).Should().BeTrue();
        StaPump.Wait(form.LastAction);
        form.List.RowTexts().Should().Equal("b", "bb", "c", "zz");
        form.List.SelectedTexts().Should().Equal("zz");

        opened.Should().Equal((null, true), ("a", false));
    });

    [Fact]
    public void DuplicateFromTheEditor_IsAMessage_NotAnException() => Sta.Run(() =>
    {
        using var form = Create(Three());
        var calls = 0;
        form.EditorOverride = (_, _, _) => ++calls == 1 ? new AliasEntity { Command = "a", Action = "otra" } : null;

        form.HandleListKey(Keys.Insert);
        StaPump.Wait(form.LastAction);

        _prompts.Warnings.Should().Equal(string.Format(Strings.AliasEdit_ErrDuplicate, "a"));
        form.List.Items.Count.Should().Be(3);
    });

    [Fact]
    public void OtherKeys_AreNotTaken_AndAliasesCannotBeMoved() => Sta.Run(() =>
    {
        using var form = Create(Three());
        form.HandleListKey(Keys.A).Should().BeFalse();
        form.HandleListKey(Keys.Alt | Keys.Up).Should().BeFalse();
        form.Controls.Find("_btnUp", true).Should().BeEmpty();
    });

    [Fact]
    public void SortMenu_ListsEveryColumn_ChecksTheCurrentOne_AndSortsKeepingTheSelection() => Sta.Run(() =>
    {
        ListFormsTestSupport.UseCulture("es");
        var store = new MemoryListSortStore();
        using var form = Create(new MemoryAliasRepository(("a", "zeta"), ("b", "alfa")), store);
        using var menu = new ContextMenuStrip();

        form.FillSortItems(menu.Items);
        menu.Items.Cast<ToolStripMenuItem>().Select(i => i.Text).Should().Equal("Comando (ascendente)", "Acción", "Activado");
        menu.Items.Cast<ToolStripMenuItem>().Select(i => i.Checked).Should().Equal(true, false, false);

        menu.Items[1].PerformClick();
        StaPump.Wait(form.LastAction);

        form.List.RowTexts().Should().Equal("b", "a");
        form.List.SelectedTexts().Should().Equal("a");
        _announcer.Spoken.Should().Equal("Ordenado por Acción, ascendente.");
        store.LoadAsync(1, "aliases").Result.Should().Be(new ListSort(AliasListPresenter.ColAction));
    });

    [Fact]
    public void ImportFromAnotherCharacter_EndToEnd_RefreshesTheListKeepingTheSelection() => Sta.Run(() =>
    {
        using var db = new ListsDatabase();
        db.Aliases.AddAsync(new AliasEntity { CharacterId = db.CharacterId, Command = "m", Action = "mio" }).GetAwaiter().GetResult();
        db.Aliases.AddAsync(new AliasEntity { CharacterId = db.ThirdCharacterId, Command = "a", Action = "ajeno" }).GetAwaiter().GetResult();

        using var form = new FrmAliases(db.Aliases, db.CharacterId, "Gandalf", _prompts, _announcer, null, db.Exchange, db.Characters, db.Muds,
            new FixedConflictResolver(ImportDecision.Skip));
        _ = form.List.Handle;
        StaPump.Wait(form.InitializeAsync());
        form.Find<Button>("_btnImport").Enabled.Should().BeTrue();
        form.Find<Button>("_btnExport").Enabled.Should().BeTrue();

        IReadOnlyList<CharacterChoice> offered = [];
        form.PickCharacterOverride = choices =>
        {
            offered = choices;
            return choices.Single(c => c.Name == "Aragorn");
        };
        form.List.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == Strings.Lst_Import)
            .DropDownItems[1].PerformClick();
        StaPump.Wait(form.LastAction);

        offered.Select(c => c.Name).Should().BeEquivalentTo("Aragorn", "Frodo");
        form.List.RowTexts().Should().Equal("a", "m");
        form.List.SelectedTexts().Should().Equal("m");
        form.Changed.Should().BeTrue();
    });
}
