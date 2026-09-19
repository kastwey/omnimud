using System.Globalization;
using FluentAssertions;
using Omnimud.Core.Actions;

namespace Omnimud.Core.Tests.Actions;

public sealed class ActionMenuStateTests
{
    private const string Inventory = CharInventoryTranslatorTests.RealPayload;
    private const string Room = RoomInfoTranslatorTests.RealPayload;

    private readonly ActionMenuState _sut = new();

    public ActionMenuStateTests() => CultureInfo.CurrentUICulture = new CultureInfo("es");

    [Fact]
    public void StartsEmpty_AndNeverNull()
    {
        _sut.Current.Should().BeSameAs(ActionMenu.Empty);
        _sut.Current.IsEmpty.Should().BeTrue();
        _sut.Current.Sections.Should().BeEmpty();
    }

    [Fact]
    public void TwoSections_AlwaysInventoryFirstThenExits_WhateverArrivesFirst()
    {
        _sut.Update("Room.Info", Room).Should().BeTrue();
        _sut.Current.Sections.Select(s => s.Label).Should().Equal("Salidas");

        _sut.Update("Char.Inventory", Inventory).Should().BeTrue();

        _sut.Current.Sections.Select(s => s.Label).Should().Equal("Inventario", "Salidas");
        _sut.Current.Sections.Should().OnlyContain(s => s.IsSubmenu);
        _sut.Current.Sections[0].Children.Select(n => n.Label).Should().Equal("una espada larga", "poción (3)");
        _sut.Current.Sections[1].Children.Select(n => n.Command).Should().Equal("norte", "sur", "arriba");
    }

    [Theory]
    [InlineData("char.inventory")]
    [InlineData("CHAR.INVENTORY")]
    [InlineData("Char.Inventory")]
    public void PackageNames_AreNotCaseSensitive(string package)
    {
        _sut.Update(package, Inventory).Should().BeTrue();

        _sut.Current.Sections.Single().Label.Should().Be("Inventario");
    }

    [Fact]
    public void EveryPackage_ReplacesItsSection_AndOnlyItsSection()
    {
        _sut.Update("Char.Inventory", Inventory);
        _sut.Update("Room.Info", Room);

        _sut.Update("Char.Inventory", """{ "items": [ { "short": "pan", "actions": [ { "label": "Comer", "cmd": "comer pan" } ] } ] }""");

        _sut.Current.Sections[0].Children.Select(n => n.Label).Should().Equal(["pan"], "the sword and the potions are gone: nothing is merged");
        _sut.Current.Sections[1].Children.Should().HaveCount(3, "the exits did not change");
    }

    [Fact]
    public void EmptySections_AreLeftOut_AndAnEmptyMenuIsTheEmptyMenu()
    {
        _sut.Update("Char.Inventory", Inventory);
        _sut.Update("Room.Info", Room);

        _sut.Update("Char.Inventory", """{ "items": [] }""").Should().BeTrue();
        _sut.Current.Sections.Select(s => s.Label).Should().Equal("Salidas");

        _sut.Update("Room.Info", """{ "exits": [] }""").Should().BeTrue();
        _sut.Current.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Snapshots_AreImmutable_AnUpdateNeverTouchesTheOneAlreadyHandedOut()
    {
        _sut.Update("Room.Info", Room);
        var before = _sut.Current;

        _sut.Update("Room.Info", """{ "exits": [ "abajo" ] }""");

        before.Sections.Single().Children.Select(n => n.Command).Should().Equal("norte", "sur", "arriba");
        _sut.Current.Should().NotBeSameAs(before);
        _sut.Current.Sections.Single().Children.Select(n => n.Command).Should().Equal("abajo");
    }

    [Theory]
    [InlineData("Char.Inventory", "{ roto")]
    [InlineData("Char.Inventory", "[]")]
    [InlineData("Char.Inventory", """{ "items": 7 }""")]
    [InlineData("Room.Info", "")]
    [InlineData("Room.Info", """{ "exits": "norte" }""")]
    [InlineData("Char.Vitals", """{ "hp": 10 }""")]
    [InlineData("Comm.Channel.Text", """{ "text": "hola" }""")]
    [InlineData("", "{}")]
    [InlineData(null, "{}")]
    [InlineData("Room.Info", null)]
    public void BadPayloadsAndUnknownPackages_ChangeNothing(string? package, string? payload)
    {
        _sut.Update("Char.Inventory", Inventory);
        _sut.Update("Room.Info", Room);
        var before = _sut.Current;

        _sut.Update(package, payload).Should().BeFalse();

        _sut.Current.Should().BeSameAs(before);
    }

    [Fact]
    public void Clear_ForgetsEverything_AndSaysWhetherThereWasAnything()
    {
        _sut.Clear().Should().BeFalse("it was already empty");
        _sut.Update("Char.Inventory", Inventory);
        _sut.Update("Room.Info", Room);

        _sut.Clear().Should().BeTrue();

        _sut.Current.IsEmpty.Should().BeTrue();
        _sut.Update("Room.Info", Room);
        _sut.Current.Sections.Select(s => s.Label).Should().Equal(["Salidas"], "the old inventory does not come back");
    }

    [Fact]
    public void Limits_AreAppliedToWhateverATranslatorReturns()
    {
        var exits = string.Join(", ", Enumerable.Range(1, 500).Select(i => $"\"salida{i}\""));

        _sut.Update("Room.Info", "{ \"exits\": [ " + exits + " ] }");

        var section = _sut.Current.Sections.Single();
        section.Children.Should().HaveCount(41);
        section.Children[^1].Label.Should().Be("… y 460 más");
        section.Children[^1].IsAction.Should().BeFalse();
    }

    [Fact]
    public void ABigInventory_StaysWithin200Nodes()
    {
        static string Action(int item, int action) => $$"""{ "label": "Acción {{action}}", "cmd": "accion{{action}} objeto{{item}}" }""";
        static string Item(int item)
        {
            var actions = string.Join(", ", Enumerable.Range(1, 9).Select(a => Action(item, a)));
            return $$"""{ "short": "objeto {{item}}", "actions": [ {{actions}} ] }""";
        }
        var items = string.Join(", ", Enumerable.Range(1, 60).Select(Item));

        _sut.Update("Char.Inventory", "{ \"items\": [ " + items + " ] }");

        static int Count(IEnumerable<ActionMenuNode> nodes) => nodes.Sum(n => (n.IsAction || n.IsSubmenu ? 1 : 0) + Count(n.Children));
        Count(_sut.Current.Sections.Single().Children).Should().Be(200);
    }

    [Fact]
    public void SupportedModules_ComeFromTheTranslators()
        => _sut.SupportedModules.Should().Equal("Char.Inventory 1", "Room.Info 1");

    // ── Another MUD, another package: only a translator is needed ──────────

    private sealed class SpellsTranslator : IActionMenuTranslator
    {
        public string Package => "Otro.Hechizos";
        public string SupportedModule => "Otro.Hechizos 2";
        public string SectionLabel => "Hechizos";
        public bool Throw { get; set; }

        public IReadOnlyList<ActionMenuNode>? Translate(string payload)
        {
            if (Throw) throw new InvalidOperationException("traductor defectuoso");
            return payload.Split(',').Select(s => ActionMenuNode.Action(s, "formular " + s)!).ToArray();
        }
    }

    [Fact]
    public void AnotherTranslator_AddsItsSection_InTheOrderGiven()
    {
        var sut = new ActionMenuState([new SpellsTranslator(), new RoomInfoTranslator()]);

        sut.Update("Room.Info", Room);
        sut.Update("otro.hechizos", "luz,bola de fuego");

        sut.SupportedModules.Should().Equal("Otro.Hechizos 2", "Room.Info 1");
        sut.Current.Sections.Select(s => s.Label).Should().Equal("Hechizos", "Salidas");
        sut.Current.Sections[0].Children.Select(n => n.Command).Should().Equal("formular luz", "formular bola de fuego");
        sut.Update("Char.Inventory", Inventory).Should().BeFalse("this state was not given that translator");
    }

    [Fact]
    public void ATranslatorThatThrows_IsIgnored_AndTheMenuSurvives()
    {
        var spells = new SpellsTranslator();
        var sut = new ActionMenuState([spells]);
        sut.Update("Otro.Hechizos", "luz");
        spells.Throw = true;

        var act = () => sut.Update("Otro.Hechizos", "oscuridad");

        act.Should().NotThrow();
        sut.Current.Sections.Single().Children.Single().Command.Should().Be("formular luz");
    }

    [Fact]
    public void SectionLabels_FollowTheLanguage()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");

        _sut.Update("Char.Inventory", Inventory);
        _sut.Update("Room.Info", Room);

        _sut.Current.Sections.Select(s => s.Label).Should().Equal("Inventory", "Exits");
    }
}
