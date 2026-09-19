using System.Globalization;
using System.Text;
using FluentAssertions;
using Omnimud.Core.Actions;

namespace Omnimud.Core.Tests.Actions;

public sealed class CharInventoryTranslatorTests
{
    private readonly CharInventoryTranslator _sut = new();

    public CharInventoryTranslatorTests() => CultureInfo.CurrentUICulture = new CultureInfo("es");

    /// <summary>What the Reduction mudlib really sends: a leaf and a group of three identical potions.</summary>
    internal const string RealPayload = """
        { "items": [
            { "id": "espada", "short": "una espada larga", "actions": [
                { "action": "dejar", "label": "Dejar", "cmd": "dejar espada" },
                { "action": "examinar", "label": "Examinar", "cmd": "examinar espada" } ] },
            { "id": "pocion", "short": "poción (3)", "children": [
                { "id": "pocion", "short": "poción (1)", "actions": [ { "action": "beber", "label": "Beber", "cmd": "beber pocion 1" } ] },
                { "id": "pocion", "short": "poción (2)", "actions": [ { "action": "beber", "label": "Beber", "cmd": "beber pocion 2" } ] },
                { "id": "pocion", "short": "poción (3)", "actions": [ { "action": "beber", "label": "Beber", "cmd": "beber pocion 3" } ] } ] }
        ] }
        """;

    [Fact]
    public void Identity_PackageModuleAndLocalizedSection()
    {
        _sut.Package.Should().Be("Char.Inventory");
        _sut.SupportedModule.Should().Be("Char.Inventory 1");
        _sut.SectionLabel.Should().Be("Inventario");

        CultureInfo.CurrentUICulture = new CultureInfo("en");
        _sut.SectionLabel.Should().Be("Inventory");
    }

    [Fact]
    public void Leaf_BecomesASubmenuWithOneEntryPerAction()
    {
        var nodes = _sut.Translate(RealPayload)!;

        var sword = nodes[0];
        sword.Label.Should().Be("una espada larga");
        sword.IsSubmenu.Should().BeTrue();
        sword.Children.Select(a => (a.Label, a.Command, a.IsAction)).Should().Equal(
            ("Dejar", "dejar espada", true),
            ("Examinar", "examinar espada", true));
    }

    [Fact]
    public void Group_BecomesASubmenuOfSubmenus()
    {
        var nodes = _sut.Translate(RealPayload)!;

        nodes.Should().HaveCount(2);
        var potions = nodes[1];
        potions.Label.Should().Be("poción (3)");
        potions.Children.Select(c => c.Label).Should().Equal("poción (1)", "poción (2)", "poción (3)");
        potions.Children.Should().OnlyContain(c => c.IsSubmenu);
        potions.Children[1].Children.Single().Command.Should().Be("beber pocion 2");
    }

    [Fact]
    public void NestedGroup_IsTranslatedLevelByLevel()
    {
        const string payload = """
            { "items": [ { "short": "bolsas (2)", "children": [
                { "short": "bolsa (1)", "children": [
                    { "short": "gema (1)", "actions": [ { "label": "Sacar", "cmd": "sacar gema 1" } ] } ] } ] } ] }
            """;

        var gem = _sut.Translate(payload)!.Single().Children.Single().Children.Single();

        gem.Label.Should().Be("gema (1)");
        gem.Children.Single().Command.Should().Be("sacar gema 1");
    }

    [Theory]
    [InlineData(10)]    // valid JSON: the translator stops following it
    [InlineData(200)]   // deeper than the JSON reader accepts: ignored
    public void EndlessNesting_IsNotFollowed(int levels)
    {
        var payload = new StringBuilder("{ \"items\": [ ");
        for (var i = 0; i < levels; i++) payload.Append("{ \"short\": \"caja\", \"children\": [ ");
        payload.Append("{ \"short\": \"fondo\", \"actions\": [ { \"label\": \"x\", \"cmd\": \"x\" } ] }");
        for (var i = 0; i < levels; i++) payload.Append(" ] }");
        payload.Append(" ] }");

        var nodes = _sut.Translate(payload.ToString());

        int Depth(ActionMenuNode n) => 1 + (n.Children.Count == 0 ? 0 : n.Children.Max(Depth));
        (nodes ?? []).Select(Depth).DefaultIfEmpty(0).Max().Should().BeLessThanOrEqualTo(ActionMenuLimits.MaxDepth + 1);
        if (levels > 100) nodes.Should().BeNull();
    }

    [Fact]
    public void ItemWithoutActions_IsKeptAsInformation_SoTheMenuStillListsWhatIsCarried()
    {
        var nodes = _sut.Translate("""{ "items": [ { "short": "una piedra", "actions": [] }, { "short": "un palo" } ] }""")!;

        nodes.Select(n => n.Label).Should().Equal("una piedra", "un palo");
        nodes.Should().OnlyContain(n => !n.IsAction && !n.IsSubmenu && n.Command == null);
    }

    [Fact]
    public void ItemWhoseActionsAreAllUnusable_IsInformationToo()
    {
        var nodes = _sut.Translate("""{ "items": [ { "short": "un cofre", "actions": [ { "label": "Abrir" }, { "cmd": "abrir" }, 7, null, "abrir" ] } ] }""")!;

        nodes.Single().Label.Should().Be("un cofre");
        nodes.Single().IsSubmenu.Should().BeFalse();
    }

    [Fact]
    public void ActionWithoutLabel_FallsBackToItsVerb()
    {
        var nodes = _sut.Translate("""{ "items": [ { "short": "pan", "actions": [ { "action": "comer", "cmd": "comer pan" }, { "action": "oler", "label": "  ", "cmd": "oler pan" } ] } ] }""")!;

        nodes.Single().Children.Select(a => a.Label).Should().Equal("comer", "oler");
    }

    [Fact]
    public void ItemsWithoutName_AreDropped_WithTheirActions()
    {
        var nodes = _sut.Translate("""
            { "items": [
                { "actions": [ { "label": "Dejar", "cmd": "dejar" } ] },
                { "short": "", "actions": [ { "label": "Dejar", "cmd": "dejar" } ] },
                { "short": 5, "actions": [ { "label": "Dejar", "cmd": "dejar" } ] },
                { "short": "pan", "actions": [ { "label": "Comer", "cmd": "comer pan" } ] } ] }
            """)!;

        nodes.Select(n => n.Label).Should().Equal("pan");
    }

    [Theory]
    [InlineData("""{ "items": [ 1, "dos", null, true, [], { "short": "pan" } ] }""")]
    [InlineData("""{ "items": [ { "short": "pan", "actions": "comer" } ] }""")]
    [InlineData("""{ "items": [ { "short": "pan", "children": 3, "actions": { "label": "x" } } ] }""")]
    [InlineData("""{ "items": [ { "short": "pan", "actions": [ { "label": 1, "cmd": 2 }, { "label": "Comer", "cmd": ["comer"] } ] } ] }""")]
    public void WrongTypesInside_AreSkipped_AndTheRestIsKept(string payload)
    {
        var nodes = _sut.Translate(payload)!;

        nodes.Select(n => n.Label).Should().Equal("pan");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "items": [] }""")]
    [InlineData("""{ "items": null }""")]
    [InlineData("""{ "otra": "cosa" }""")]
    public void NoItems_IsAnEmptyInventory(string payload)
        => _sut.Translate(payload).Should().NotBeNull().And.BeEmpty();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("""{ "items": [ { "short": "pan" """)]
    [InlineData("esto no es json")]
    [InlineData("[]")]
    [InlineData("""[ { "short": "pan" } ]""")]
    [InlineData("\"texto\"")]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("""{ "items": "muchos" }""")]
    [InlineData("""{ "items": { "short": "pan" } }""")]
    public void MalformedOrUnexpected_IsIgnored_WithoutException(string payload)
        => _sut.Translate(payload).Should().BeNull();

    [Fact]
    public void Null_IsIgnored() => _sut.Translate(null!).Should().BeNull();

    [Fact]
    public void PayloadOver256KB_IsIgnored_WithoutEvenParsingIt()
    {
        var item = """{ "short": "pan", "actions": [ { "label": "Comer", "cmd": "comer pan" } ] }""";
        string Payload(int items) => "{ \"items\": [ " + string.Join(", ", Enumerable.Repeat(item, items)) + " ] }";
        var small = Payload(100);
        var big = Payload(1 + ActionMenuLimits.MaxPayloadLength / item.Length);

        big.Length.Should().BeGreaterThan(ActionMenuLimits.MaxPayloadLength);
        _sut.Translate(big).Should().BeNull();
        _sut.Translate(small).Should().HaveCount(100);
    }

    [Fact]
    public void LineBreaksInCmd_CannotSmuggleASecondCommand()
    {
        var nodes = _sut.Translate("""{ "items": [ { "short": "pan", "actions": [ { "label": "Comer", "cmd": "comer pan\nabandonar\r\nsuicidarse" } ] } ] }""")!;

        var command = nodes.Single().Children.Single().Command!;
        command.Should().Be("comer pan abandonar suicidarse");
        command.Should().NotContainAny("\n", "\r");
    }

    [Fact]
    public void ControlCharactersAndAnsi_AreRemovedFromLabels()
    {
        var nodes = _sut.Translate("""{ "items": [ { "short": "\u001B[1;33muna\tllave\u0007 dorada\u001B[0m", "actions": [ { "label": "De\njar", "cmd": "dejar llave" } ] } ] }""")!;

        nodes.Single().Label.Should().Be("una llave dorada");
        nodes.Single().Children.Single().Label.Should().Be("De jar");
    }

    [Fact]
    public void Ampersand_IsKeptAsText_TheMenuEscapesIt()
    {
        var nodes = _sut.Translate("""{ "items": [ { "short": "pico & pala", "actions": [ { "label": "Usar & tirar", "cmd": "usar pico & pala" } ] } ] }""")!;

        nodes.Single().Label.Should().Be("pico & pala");
        nodes.Single().Children.Single().Label.Should().Be("Usar & tirar");
        nodes.Single().Children.Single().Command.Should().Be("usar pico & pala");
    }

    [Fact]
    public void UnicodeAccentsAndEnye_ArePreserved()
    {
        var nodes = _sut.Translate("""{ "items": [ { "short": "cañón de Ñuño — 龍", "actions": [ { "label": "Encender más", "cmd": "encender cañón" }, { "label": "Ábrelo", "cmd": "abrir cañón" } ] } ] }""")!;

        nodes.Single().Label.Should().Be("cañón de Ñuño — 龍");
        nodes.Single().Children.Select(a => (a.Label, a.Command)).Should().Equal(("Encender más", "encender cañón"), ("Ábrelo", "abrir cañón"));
    }

    [Fact]
    public void LongTexts_AreCut()
    {
        var payload = $$"""{ "items": [ { "short": "{{new string('a', 300)}}", "actions": [ { "label": "{{new string('b', 300)}}", "cmd": "{{new string('c', 1000)}}" } ] } ] }""";

        var item = _sut.Translate(payload)!.Single();

        item.Label.Should().HaveLength(80);
        item.Children.Single().Label.Should().HaveLength(80);
        item.Children.Single().Command.Should().HaveLength(255);
    }

    [Fact]
    public void ItemWithChildrenAndActions_ShowsBoth()
    {
        var nodes = _sut.Translate("""
            { "items": [ { "short": "bolsa", "actions": [ { "label": "Abrir", "cmd": "abrir bolsa" } ],
                           "children": [ { "short": "gema", "actions": [ { "label": "Sacar", "cmd": "sacar gema" } ] } ] } ] }
            """)!;

        nodes.Single().Children.Select(c => c.Label).Should().Equal("gema", "Abrir");
    }

    [Fact]
    public void Translation_IsPure_SamePayloadSameResult()
    {
        var first = _sut.Translate(RealPayload)!;
        var second = _sut.Translate(RealPayload)!;

        second.Select(n => n.ToString()).Should().Equal(first.Select(n => n.ToString()));
        second[1].Children[2].Children[0].Command.Should().Be(first[1].Children[2].Children[0].Command);
    }
}
