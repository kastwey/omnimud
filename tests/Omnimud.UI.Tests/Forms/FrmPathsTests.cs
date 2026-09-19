using Omnimud.Data.Entities;
using Omnimud.Data.Exchange;
using Omnimud.UI.Forms;
using Omnimud.UI.Resources;
using Omnimud.UI.Tests.Presenters;

namespace Omnimud.UI.Tests.Forms;

public class FrmPathsTests
{
    private readonly RecordingPrompts _prompts = new();
    private readonly RecordingAnnouncer _announcer = new();

    private FrmPaths Create(MemoryPathRepository repository)
    {
        var form = new FrmPaths(repository, 1, "Gandalf", _prompts, _announcer);
        _ = form.List.Handle;
        StaPump.Wait(form.InitializeAsync());
        return form;
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void PassesTheAccessibilityAudit(string culture) => Sta.Run(() =>
    {
        ListFormsTestSupport.AssertAccessible(culture, () => Create(new MemoryPathRepository(("plaza", "3n"))));
        ListFormsTestSupport.AssertAccessible(culture, () => new FrmPaths(new MemoryPathRepository(), 1, "Gandalf"));
    });

    [Fact]
    public void Texts_AreLocalized_AndPathsHaveNoToggleNorMoveButtons() => Sta.Run(() =>
    {
        ListFormsTestSupport.UseCulture("es");
        using var form = Create(new MemoryPathRepository(("plaza", "3n")));

        form.Text.Should().Be("Paths de Gandalf");
        form.Find<Label>("_lblList").Text.Should().Be("&Paths:");
        form.List.Columns.Cast<ColumnHeader>().Select(c => c.Text).Should().Equal("Nombre", "Camino");
        form.Controls.Find("_btnToggle", true).Should().BeEmpty();
        form.Controls.Find("_btnUp", true).Should().BeEmpty();
        form.HandleListKey(Keys.Space).Should().BeFalse();
    });

    [Fact]
    public void Delete_SelectsTheNeighbour_AndSaysWhatWasRemoved() => Sta.Run(() =>
    {
        using var form = Create(new MemoryPathRepository(("a", "n"), ("b", "s"), ("c", "e")));
        form.List.Items[0].Selected = true;

        form.HandleListKey(Keys.Delete);
        StaPump.Wait(form.LastAction);

        form.List.RowTexts().Should().Equal("b", "c");
        form.List.SelectedTexts().Should().Equal("b");
        form.StatusText.Should().Be(string.Format(Strings.Lst_Removed, "a"));
        _announcer.Spoken.Should().Equal(string.Format(Strings.Lst_Removed, "a"));
    });

    [Fact]
    public void Delete_AnsweredNo_KeepsEverything() => Sta.Run(() =>
    {
        _prompts.DefaultConfirm = false;
        using var form = Create(new MemoryPathRepository(("a", "n")));
        form.HandleListKey(Keys.Delete);
        StaPump.Wait(form.LastAction);
        form.List.Items.Count.Should().Be(1);
        form.Changed.Should().BeFalse();
    });

    [Fact]
    public void DuplicateName_IsAMessage() => Sta.Run(() =>
    {
        using var form = Create(new MemoryPathRepository(("plaza", "3n")));
        var calls = 0;
        form.EditorOverride = (_, _, _) => ++calls == 1 ? new PathEntity { Name = "plaza", Path = "e" } : null;

        form.HandleListKey(Keys.Insert);
        StaPump.Wait(form.LastAction);

        _prompts.Warnings.Should().Equal(string.Format(Strings.PathEdit_ErrDuplicateName, "plaza"));
    });

    [Fact]
    public void Sqlite_LoadsTheDirectionsOfTheMud_AndExportsImports() => Sta.Run(() =>
    {
        using var db = new ListsDatabase();
        db.Directions.AddAsync(new DirectionEntity { MudId = db.MudId, Direction = "norte", Abbreviation = "n", OppositeDirection = "sur" }).GetAwaiter().GetResult();
        db.Paths.AddAsync(new PathEntity { CharacterId = db.CharacterId, Name = "plaza", Path = "3n" }).GetAwaiter().GetResult();
        _prompts.SaveFile = _prompts.OpenFile = db.FilePath("paths.omnimud");

        using (var form = new FrmPaths(db.Paths, db.CharacterId, "Gandalf", _prompts, _announcer, db.Directions, db.MudId, null, db.Exchange, db.Characters, db.Muds,
                   new FixedConflictResolver(ImportDecision.Overwrite)))
        {
            _ = form.List.Handle;
            StaPump.Wait(form.InitializeAsync());
            form.Find<Button>("_btnExport").Press();
            StaPump.Wait(form.LastAction);
        }
        File.Exists(_prompts.SaveFile).Should().BeTrue();

        using var target = new FrmPaths(db.Paths, db.SecondCharacterId, "Frodo", _prompts, _announcer, db.Directions, db.MudId, null, db.Exchange, db.Characters, db.Muds,
            new FixedConflictResolver(ImportDecision.Overwrite));
        _ = target.List.Handle;
        StaPump.Wait(target.InitializeAsync());
        target.List.Items.Count.Should().Be(0);

        target.List.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == Strings.Lst_Import).DropDownItems[0].PerformClick();
        StaPump.Wait(target.LastAction);

        target.List.RowTexts().Should().Equal("plaza");
        target.List.SelectedTexts().Should().Equal("plaza");
        target.Changed.Should().BeTrue();
    });
}
