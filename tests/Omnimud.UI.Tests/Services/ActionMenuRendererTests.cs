using Omnimud.Core.Actions;
using Omnimud.UI.Services;

namespace Omnimud.UI.Tests.Services;

public sealed class ActionMenuRendererTests
{
    private static ActionMenuNode Action(string label, string command) => ActionMenuNode.Action(label, command)!;

    private static ActionMenuNode Menu(string label, params ActionMenuNode[] children) => ActionMenuNode.Submenu(label, children)!;

    private static ToolStripMenuItem[] Children(ToolStripItem item) =>
        ((ToolStripMenuItem)item).DropDownItems.OfType<ToolStripMenuItem>().ToArray();

    [Fact]
    public void Nothing_RendersNothing() => Sta.Run(() =>
        ActionMenuRenderer.Render([], _ => { }).Should().BeEmpty());

    [Fact]
    public void TheTree_BecomesMenusAndSubmenus_InTheSameOrder() => Sta.Run(() =>
    {
        var nodes = new[]
        {
            Menu("Inventario",
                Menu("una espada", Action("Dejar", "dejar espada"), Action("Examinar", "examinar espada")),
                Menu("poción (2)",
                    Menu("poción (1)", Action("Beber", "beber pocion 1")),
                    Menu("poción (2)", Action("Beber", "beber pocion 2")))),
            Menu("Salidas", Action("norte", "norte"), Action("sur", "sur")),
        };

        var items = ActionMenuRenderer.Render(nodes, _ => { });

        items.Select(i => i.Text).Should().Equal("Inventario", "Salidas");
        var inventory = Children(items[0]);
        inventory.Select(i => i.Text).Should().Equal("una espada", "poción (2)");
        Children(inventory[0]).Select(i => i.Text).Should().Equal("Dejar", "Examinar");
        Children(inventory[1]).Select(i => i.Text).Should().Equal("poción (1)", "poción (2)");
        Children(Children(inventory[1])[1]).Select(i => i.Text).Should().Equal("Beber");
        Children(items[1]).Select(i => i.Text).Should().Equal("norte", "sur");
        foreach (var item in items) item.Dispose();
    });

    [Fact]
    public void ClickingAnAction_HandsOverItsNode_AndOnlyThatOne() => Sta.Run(() =>
    {
        var chosen = new List<ActionMenuNode>();
        var nodes = new[] { Menu("poción (2)", Menu("poción (1)", Action("Beber", "beber pocion 1")), Menu("poción (2)", Action("Beber", "beber pocion 2"))) };
        var items = ActionMenuRenderer.Render(nodes, chosen.Add);

        Children(Children(items[0])[1])[0].PerformClick();

        chosen.Should().ContainSingle().Which.Command.Should().Be("beber pocion 2");
    });

    [Fact]
    public void Submenus_DoNothingWhenClicked() => Sta.Run(() =>
    {
        var chosen = new List<ActionMenuNode>();
        var items = ActionMenuRenderer.Render([Menu("Salidas", Action("norte", "norte"))], chosen.Add);

        ((ToolStripMenuItem)items[0]).PerformClick();

        chosen.Should().BeEmpty();
    });

    [Fact]
    public void Information_IsShownDisabled_AndCannotBeChosen() => Sta.Run(() =>
    {
        var chosen = new List<ActionMenuNode>();
        var items = ActionMenuRenderer.Render([Action("norte", "norte"), ActionMenuNode.Information("… y 12 más")!], chosen.Add);

        items[0].Enabled.Should().BeTrue();
        items[1].Enabled.Should().BeFalse();
        items[1].Text.Should().Be("… y 12 más");
        ((ToolStripMenuItem)items[1]).PerformClick();
        chosen.Should().BeEmpty();
    });

    [Theory]
    [InlineData("pico & pala", "pico && pala")]
    [InlineData("&Abrir", "&&Abrir")]
    [InlineData("a && b", "a &&&& b")]
    [InlineData("sin nada", "sin nada")]
    public void Ampersands_AreEscaped_SoTheServerNeverChoosesMnemonics(string label, string expected) => Sta.Run(() =>
    {
        var item = ActionMenuRenderer.Render([Action(label, "x")], _ => { }).Single();

        item.Text.Should().Be(expected);
        Omnimud.UI.Tests.Accessibility.AccessibilityAudit.Mnemonic(item.Text).Should().BeNull();
    });

    [Fact]
    public void EveryItem_KnowsItsNode() => Sta.Run(() =>
    {
        var node = Action("norte", "norte");

        ActionMenuRenderer.Render([node], _ => { }).Single().Tag.Should().BeSameAs(node);
    });

    [Fact]
    public void Replace_KeepsTheFixedItems_SwapsTheRest_AndDisposesWhatLeaves() => Sta.Run(() =>
    {
        using var menu = new ContextMenuStrip();
        var fixedItem = new ToolStripMenuItem("Copiar");
        var old = new ToolStripMenuItem("viejo");
        menu.Items.AddRange([fixedItem, old]);
        var disposed = new List<string?>();
        fixedItem.Disposed += (_, _) => disposed.Add(fixedItem.Text);
        old.Disposed += (_, _) => disposed.Add(old.Text);

        ActionMenuRenderer.Replace(menu.Items, 1, ActionMenuRenderer.Render([Action("norte", "norte"), Action("sur", "sur")], _ => { }));

        menu.Items.Cast<ToolStripItem>().Select(i => i.Text).Should().Equal("Copiar", "norte", "sur");
        disposed.Should().Equal("viejo");

        ActionMenuRenderer.Replace(menu.Items, 1, []);
        menu.Items.Cast<ToolStripItem>().Select(i => i.Text).Should().Equal("Copiar");
    });

    [Fact]
    public void TheBiggestMenuTheLimitsAllow_IsBuiltInNoTime() => Sta.Run(() =>
    {
        // Two sections at their limit: 200 content nodes each.
        var section = Enumerable.Range(1, 20).Select(i => Menu($"objeto {i}", Enumerable.Range(1, 9).Select(a => Action($"Acción {a}", $"a{a} o{i}")).ToArray())).ToArray();
        var nodes = new[] { Menu("Inventario", section), Menu("Salidas", section) };
        ActionMenuRenderer.Render(nodes, _ => { }); // warm up

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var items = ActionMenuRenderer.Render(nodes, _ => { });
        watch.Stop();

        Children(items[0]).Should().HaveCount(20);
        watch.ElapsedMilliseconds.Should().BeLessThan(250, "opening the menu must not be noticeable");
        foreach (var item in items) item.Dispose();
    });
}
