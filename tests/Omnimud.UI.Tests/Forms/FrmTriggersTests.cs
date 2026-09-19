using Omnimud.Core.Scripting;
using Omnimud.Data.Entities;
using Omnimud.UI.Forms;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Tests.Presenters;

namespace Omnimud.UI.Tests.Forms;

public class FrmTriggersTests
{
    private readonly RecordingPrompts _prompts = new();
    private readonly RecordingAnnouncer _announcer = new();

    private FrmTriggers Create(MemoryTriggerRepository repository, IListSortStore? store = null)
    {
        var form = new FrmTriggers(repository, 1, "Gandalf", _prompts, _announcer, scriptEngine: null, store);
        _ = form.List.Handle;
        StaPump.Wait(form.InitializeAsync());
        return form;
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void PassesTheAccessibilityAudit(string culture) => Sta.Run(() =>
    {
        ListFormsTestSupport.AssertAccessible(culture, () => Create(new MemoryTriggerRepository("uno", "dos")));
        ListFormsTestSupport.AssertAccessible(culture, () => new FrmTriggers(new MemoryTriggerRepository(), 1, "Gandalf"));
        ListFormsTestSupport.AssertAccessible(culture, () => Create(new MemoryTriggerRepository("uno")), form =>
        {
            form.HandleListKey(Keys.Space);
            StaPump.Wait(form.LastAction);
            form.Find<Button>("_btnToggle").Text.Should().Be(Strings.Lst_Enable);
        });
    });

    [Fact]
    public void Columns_AreName_Pattern_Type_Action_Priority_Enabled() => Sta.Run(() =>
    {
        ListFormsTestSupport.UseCulture("es");
        var repository = new MemoryTriggerRepository("uno");
        repository.Items[0].PatternType = 1;
        repository.Items[0].ActionType = 3;
        repository.Items[0].Priority = 70;
        using var form = Create(repository);

        form.Text.Should().Be("Triggers de Gandalf");
        form.List.AccessibleName.Should().Be("Triggers");
        form.List.Columns.Cast<ColumnHeader>().Select(c => c.Text).Should().Equal("Nombre", "Patrón", "Tipo", "Acción", "Prioridad", "Activado");
        form.List.Items[0].SubItems.Cast<ListViewItem.ListViewSubItem>().Select(s => s.Text)
            .Should().Equal("uno", "pattern uno", "Expresión regular", "Ambas", "70", "Sí");
    });

    [Fact]
    public void Space_TogglesTheTrigger_AnnouncesIt_AndKeepsSelectionAndFocusedItem() => Sta.Run(() =>
    {
        ListFormsTestSupport.UseCulture("es");
        var repository = new MemoryTriggerRepository("uno", "dos", "tres");
        using var form = Create(repository);
        form.List.Items[2].Selected = true;

        form.HandleListKey(Keys.Space).Should().BeTrue();
        StaPump.Wait(form.LastAction);

        repository.Items.Single(t => t.Name == "tres").Enabled.Should().BeFalse();
        _announcer.Spoken.Should().Equal("Trigger tres desactivado");
        form.List.SelectedTexts().Should().Equal("tres");
        form.List.Items[2].Focused.Should().BeTrue();
        form.Find<Button>("_btnToggle").Text.Should().Be("Acti&var");

        form.HandleListKey(Keys.Space);
        StaPump.Wait(form.LastAction);
        _announcer.Spoken.Last().Should().Be("Trigger tres activado");
    });

    [Fact]
    public void AltArrows_MoveTheTrigger_AndTheSelectionFollowsIt() => Sta.Run(() =>
    {
        ListFormsTestSupport.UseCulture("es");
        var repository = new MemoryTriggerRepository("uno", "dos", "tres");
        using var form = Create(repository);

        form.HandleListKey(Keys.Alt | Keys.Down).Should().BeTrue();
        StaPump.Wait(form.LastAction);
        form.List.RowTexts().Should().Equal("dos", "uno", "tres");
        form.List.SelectedTexts().Should().Equal("uno");
        _announcer.Spoken.Last().Should().Be("uno movido a la posición 2 de 3");

        form.HandleListKey(Keys.Alt | Keys.Up).Should().BeTrue();
        StaPump.Wait(form.LastAction);
        form.List.RowTexts().Should().Equal("uno", "dos", "tres");
        repository.Items.OrderBy(t => t.SortOrder).Select(t => t.Name).Should().Equal("uno", "dos", "tres");

        form.Find<Button>("_btnDown").Press();
        StaPump.Wait(form.LastAction);
        form.List.RowTexts().Should().Equal("dos", "uno", "tres");
    });

    [Fact]
    public void Moving_WhileSortedByName_IsExplained_InTheStatusToo() => Sta.Run(() =>
    {
        using var form = Create(new MemoryTriggerRepository("b", "a"));
        using var menu = new ContextMenuStrip();
        form.FillSortItems(menu.Items);
        menu.Items.Count.Should().Be(7);
        menu.Items["_mnuSort_" + TriggerListPresenter.ColName]!.PerformClick();
        StaPump.Wait(form.LastAction);
        form.List.RowTexts().Should().Equal("a", "b");

        form.HandleListKey(Keys.Alt | Keys.Down);
        StaPump.Wait(form.LastAction);

        form.List.RowTexts().Should().Equal("a", "b");
        form.StatusText.Should().Be(Strings.TrigList_MoveNeedsOrder);
        _announcer.Spoken.Last().Should().Be(Strings.TrigList_MoveNeedsOrder);
    });

    [Fact]
    public void Delete_SelectsTheNeighbour() => Sta.Run(() =>
    {
        using var form = Create(new MemoryTriggerRepository("uno", "dos", "tres"));
        form.List.Items[2].Selected = true;

        form.HandleListKey(Keys.Delete);
        StaPump.Wait(form.LastAction);

        form.List.RowTexts().Should().Equal("uno", "dos");
        form.List.SelectedTexts().Should().Equal("dos");
    });

    [Fact]
    public void Insert_AddsThroughTheEditor_AtTheEnd_AndSelectsIt() => Sta.Run(() =>
    {
        using var form = Create(new MemoryTriggerRepository("uno"));
        form.EditorOverride = (_, isNew, all) =>
        {
            isNew.Should().BeTrue();
            var model = new TriggerEditorModel(null, true, all) { Name = "nuevo", Pattern = "hola", Action = "saludar" };
            model.Validate(null).Should().BeNull();
            return model.ToEntity();
        };

        form.HandleListKey(Keys.Insert);
        StaPump.Wait(form.LastAction);

        form.List.RowTexts().Should().Equal("uno", "nuevo");
        form.List.SelectedTexts().Should().Equal("nuevo");
    });

    [Fact]
    public void ContextMenu_OffersEverything() => Sta.Run(() =>
    {
        ListFormsTestSupport.UseCulture("en");
        using var form = Create(new MemoryTriggerRepository("uno"));
        var texts = form.List.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().Select(i => i.Text).ToList();
        texts.Should().Equal("&Add", "&Edit", "&Remove", "Disa&ble", "Move &up", "Move &down", "&Sort by");
    });

    [Fact]
    public void OwnScriptEngine_IsDisposedWithTheWindow_AGivenOneIsNot() => Sta.Run(() =>
    {
        var engine = new LuaScriptEngine();
        using (new FrmTriggers(new MemoryTriggerRepository(), 1, "x", _prompts, _announcer, engine)) { }
        engine.ExecuteAsync("om.send('x')", new ScriptContext()).GetAwaiter().GetResult().Success.Should().BeTrue();
        engine.Dispose();
    });
}
