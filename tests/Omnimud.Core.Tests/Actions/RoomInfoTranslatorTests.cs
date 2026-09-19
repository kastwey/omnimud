using System.Globalization;
using FluentAssertions;
using Omnimud.Core.Actions;

namespace Omnimud.Core.Tests.Actions;

public sealed class RoomInfoTranslatorTests
{
    private readonly RoomInfoTranslator _sut = new();

    public RoomInfoTranslatorTests() => CultureInfo.CurrentUICulture = new CultureInfo("es");

    internal const string RealPayload = """
        { "id": "/room/plaza", "short": "Plaza de Añil", "long": "Una plaza amplia.\nHay una fuente.",
          "exits": [ "norte", "sur", "arriba" ], "items": [ "una fuente", "Gandalf" ] }
        """;

    [Fact]
    public void Identity_PackageModuleAndLocalizedSection()
    {
        _sut.Package.Should().Be("Room.Info");
        _sut.SupportedModule.Should().Be("Room.Info 1");
        _sut.SectionLabel.Should().Be("Salidas");

        CultureInfo.CurrentUICulture = new CultureInfo("en");
        _sut.SectionLabel.Should().Be("Exits");
    }

    [Fact]
    public void EveryExit_IsAnAction_WhoseCommandIsTheExitItself()
    {
        var nodes = _sut.Translate(RealPayload)!;

        nodes.Select(n => (n.Label, n.Command, n.IsAction)).Should().Equal(
            ("norte", "norte", true), ("sur", "sur", true), ("arriba", "arriba", true));
    }

    [Fact]
    public void ItemsOfTheRoom_AreNotActions()
        => _sut.Translate(RealPayload)!.Select(n => n.Label).Should().NotContain(["una fuente", "Gandalf"]);

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "id": "x", "short": "Sala sin salidas" }""")]
    [InlineData("""{ "exits": [] }""")]
    [InlineData("""{ "exits": null }""")]
    public void NoExits_IsAnEmptySection(string payload)
        => _sut.Translate(payload).Should().NotBeNull().And.BeEmpty();

    [Fact]
    public void ExitsThatAreNotText_OrAreEmpty_AreDropped()
    {
        var nodes = _sut.Translate("""{ "exits": [ "norte", 5, null, "", "  ", { "dir": "sur" }, ["este"], true, "oeste" ] }""")!;

        nodes.Select(n => n.Command).Should().Equal("norte", "oeste");
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("""{ "exits": [ "norte" """)]
    [InlineData("no es json")]
    [InlineData("""[ "norte" ]""")]
    [InlineData("\"norte\"")]
    [InlineData("null")]
    [InlineData("""{ "exits": "norte" }""")]
    [InlineData("""{ "exits": { "norte": "/room/x" } }""")]
    public void MalformedOrUnexpected_IsIgnored_WithoutException(string payload)
        => _sut.Translate(payload).Should().BeNull();

    [Fact]
    public void AnExitWithLineBreaks_IsStillOneCommand()
    {
        var nodes = _sut.Translate("""{ "exits": [ "norte\nabandonar", "s\u0000ur\r\n" ] }""")!;

        nodes.Select(n => n.Command).Should().Equal("norte abandonar", "s ur");
        nodes.Select(n => n.Label).Should().Equal("norte abandonar", "s ur");
    }

    [Fact]
    public void UnicodeAndAmpersand_ArePreserved()
    {
        var nodes = _sut.Translate("""{ "exits": [ "callejón", "puerta & reja", "東" ] }""")!;

        nodes.Select(n => n.Command).Should().Equal("callejón", "puerta & reja", "東");
    }

    [Fact]
    public void ALongExit_IsCut_LabelAndCommandEachToItsLimit()
    {
        var nodes = _sut.Translate($$"""{ "exits": [ "{{new string('n', 400)}}" ] }""")!;

        nodes.Single().Label.Should().HaveLength(80);
        nodes.Single().Command.Should().HaveLength(255);
    }

    [Fact]
    public void PayloadOver256KB_IsIgnored()
    {
        var payload = "{ \"long\": \"" + new string('x', ActionMenuLimits.MaxPayloadLength) + "\", \"exits\": [ \"norte\" ] }";

        _sut.Translate(payload).Should().BeNull();
    }

    [Fact]
    public void HundredsOfExits_AreTranslated_TheStateCutsThem()
    {
        var exits = string.Join(", ", Enumerable.Range(1, 500).Select(i => $"\"salida{i}\""));

        _sut.Translate("{ \"exits\": [ " + exits + " ] }").Should().HaveCount(500);
    }
}
