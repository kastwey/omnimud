using System.Globalization;
using FluentAssertions;
using Omnimud.Core.Actions;

namespace Omnimud.Core.Tests.Actions;

/// <summary>The menu is built from what a server sends: it must stay small whatever arrives.</summary>
public sealed class ActionMenuLimitsTests
{
    public ActionMenuLimitsTests() => CultureInfo.CurrentUICulture = new CultureInfo("es");

    private static ActionMenuNode Leaf(int i) => ActionMenuNode.Action($"acción {i}", $"cmd {i}")!;

    private static ActionMenuNode[] Leaves(int count) => Enumerable.Range(1, count).Select(Leaf).ToArray();

    private static ActionMenuNode Menu(string label, params ActionMenuNode[] children) => ActionMenuNode.Submenu(label, children)!;

    /// <summary>A chain of submenus: depth 3 = menu → menu → menu → leaf, the leaf at level 4.</summary>
    private static ActionMenuNode Chain(int submenus)
    {
        var node = Leaf(0);
        for (var i = submenus; i >= 1; i--) node = Menu($"nivel {i}", node);
        return node;
    }

    private static int Depth(ActionMenuNode node) => 1 + (node.Children.Count == 0 ? 0 : node.Children.Max(Depth));

    private static int CountContent(IEnumerable<ActionMenuNode> nodes)
        => nodes.Sum(n => (n.IsAction || n.IsSubmenu ? 1 : 0) + CountContent(n.Children));

    private static int CountAll(IEnumerable<ActionMenuNode> nodes) => nodes.Sum(n => 1 + CountAll(n.Children));

    [Fact]
    public void WithinTheLimits_NothingChanges()
    {
        var nodes = new[] { Menu("espada", Leaves(3)), Menu("pociones (2)", Menu("poción (1)", Leaves(2)), Menu("poción (2)", Leaves(2))), Leaf(9) };

        var result = ActionMenuLimits.Apply(nodes);

        result.Select(n => n.Label).Should().Equal("espada", "pociones (2)", "acción 9");
        result[1].Children.Select(n => n.Label).Should().Equal("poción (1)", "poción (2)");
        result[1].Children[0].Children.Select(n => n.Command).Should().Equal("cmd 1", "cmd 2");
        CountAll(result).Should().Be(CountAll(nodes));
    }

    [Fact]
    public void Empty_StaysEmpty() => ActionMenuLimits.Apply([]).Should().BeEmpty();

    [Fact]
    public void MoreThan40Children_AreCut_AndAFinalDisabledNodeSaysHowManyAreMissing()
    {
        var result = ActionMenuLimits.Apply(Leaves(45));

        result.Should().HaveCount(41);
        result.Take(40).Select(n => n.Command).Should().Equal(Enumerable.Range(1, 40).Select(i => $"cmd {i}"));
        var more = result[^1];
        more.Label.Should().Be("… y 5 más");
        more.IsAction.Should().BeFalse("it is information, shown disabled");
        more.IsSubmenu.Should().BeFalse();
    }

    [Fact]
    public void Exactly40Children_Fit_WithoutNotice()
        => ActionMenuLimits.Apply(Leaves(40)).Should().HaveCount(40).And.OnlyContain(n => n.IsAction);

    [Fact]
    public void TheLimitOf40_AppliesInsideEverySubmenu()
    {
        var result = ActionMenuLimits.Apply([Menu("mochila", Leaves(60))]);

        result.Single().Children.Should().HaveCount(41);
        result.Single().Children[^1].Label.Should().Be("… y 20 más");
    }

    [Fact]
    public void TheNotice_IsLocalized()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");

        ActionMenuLimits.Apply(Leaves(41))[^1].Label.Should().Be("… and 1 more");
    }

    [Fact]
    public void ASection_NeverHasMoreThan200ContentNodes()
    {
        // 30 items with 9 actions each = 300 nodes.
        var nodes = Enumerable.Range(1, 30).Select(i => Menu($"objeto {i}", Leaves(9))).ToArray();

        var result = ActionMenuLimits.Apply(nodes);

        CountContent(result).Should().Be(ActionMenuLimits.MaxNodesPerSection);
        result.Count(n => n.IsSubmenu).Should().Be(20, "20 items of 10 nodes fill the section");
        result[^1].Label.Should().Be("… y 10 más");
        result[^1].IsAction.Should().BeFalse();
    }

    [Fact]
    public void WhenTheSectionFillsUpInsideASubmenu_ThatSubmenuIsCutToo()
    {
        // 10 items of 31 nodes: six fit whole (186), the seventh gets itself and 13 of its 30 actions.
        var nodes = Enumerable.Range(1, 10).Select(i => Menu($"objeto {i}", Leaves(30))).ToArray();

        var result = ActionMenuLimits.Apply(nodes);

        CountContent(result).Should().Be(200);
        result.Count(n => n.IsSubmenu).Should().Be(7);
        result[6].Children.Count(n => n.IsAction).Should().Be(13);
        result[6].Children[^1].Label.Should().Be("… y 17 más");
        result[^1].Label.Should().Be("… y 3 más");
    }

    [Fact]
    public void ASubmenuThatOnlyFitsByItself_IsLeftOut_BecauseAnEmptySubmenuIsUseless()
    {
        var filler = Enumerable.Range(1, 4).Select(i => Menu($"caja {i}", Leaves(39)));           // 4 × 40 = 160
        var all = filler.Append(Menu("caja 5", Leaves(38)))                                       // + 39 = 199: one node left
            .Append(Menu("no cabe", Leaves(3))).Append(Leaf(99)).ToArray();

        var result = ActionMenuLimits.Apply(all);

        result.Select(n => n.Label).Should().NotContain("no cabe", "one node left is not enough for a submenu and a child");
        result.Select(n => n.Label).Should().Contain("acción 99", "but a plain action still fits");
        CountContent(result).Should().Be(200);
    }

    [Fact]
    public void Depth4_IsKept_Deeper_IsDropped()
    {
        var result = ActionMenuLimits.Apply([Chain(3), Chain(4), Chain(7), Leaf(5)]);

        result.Select(n => n.Label).Should().Equal(["nivel 1", "acción 5"], "the chains that only lead deeper than 4 levels disappear whole");
        Depth(result[0]).Should().Be(ActionMenuLimits.MaxDepth);
    }

    [Fact]
    public void OnlyTheBranchThatIsTooDeep_IsDropped()
    {
        var item = Menu("grupo", Menu("subgrupo", Menu("objeto", Leaf(1), Menu("demasiado hondo", Leaf(2)))));

        var result = ActionMenuLimits.Apply([item]);

        var deepest = result.Single().Children.Single().Children.Single();
        deepest.Label.Should().Be("objeto");
        deepest.Children.Select(n => n.Label).Should().Equal("acción 1");
    }

    [Fact]
    public void AHugeTree_IsCutQuickly_AndStaysWithinEveryLimit()
    {
        ActionMenuNode Wide(int level) => level == 0
            ? Leaf(level)
            : Menu($"nivel {level}", Enumerable.Range(0, 20).Select(_ => Wide(level - 1)).ToArray());
        var nodes = Enumerable.Range(0, 20).Select(_ => Wide(3)).ToArray();   // 160 000 actions, 4 levels

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = ActionMenuLimits.Apply(nodes);
        watch.Stop();

        CountContent(result).Should().BeLessThanOrEqualTo(200);
        result.Max(Depth).Should().BeLessThanOrEqualTo(4);
        watch.ElapsedMilliseconds.Should().BeLessThan(1000);
    }
}
