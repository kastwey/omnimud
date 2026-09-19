using FluentAssertions;
using Omnimud.Core.Paths;

namespace Omnimud.Core.Tests.Paths;

public class PathEngineTests
{
    private readonly DirectionDictionary _dictionary;
    private readonly PathEngine _sut;

    public PathEngineTests()
    {
        _dictionary = new DirectionDictionary();
        _dictionary.Load([
            new DirectionEntry("norte", 'n', "sur"),
            new DirectionEntry("sur", 's', "norte"),
            new DirectionEntry("este", 'e', "oeste"),
            new DirectionEntry("oeste", 'o', "este"),
            new DirectionEntry("arriba", 'u', "abajo"),
            new DirectionEntry("abajo", 'd', "arriba"),
        ]);
        _sut = new PathEngine(_dictionary);
    }

    [Fact]
    public void Expand_SimpleDirections_ExpandsCorrectly()
    {
        var result = _sut.Expand("nse");

        result.Should().BeEquivalentTo(["norte", "sur", "este"]);
    }

    [Fact]
    public void Expand_WithCounts_RepeatsDirections()
    {
        var result = _sut.Expand("3s2e");

        result.Should().BeEquivalentTo(["sur", "sur", "sur", "este", "este"]);
    }

    [Fact]
    public void Expand_EmptyPath_ReturnsEmpty()
    {
        _sut.Expand("").Should().BeEmpty();
        _sut.Expand("  ").Should().BeEmpty();
    }

    [Fact]
    public void Collapse_RepeatedDirections_Compresses()
    {
        var result = _sut.Collapse(["sur", "sur", "sur", "este", "este"]);

        result.Should().Be("3s2e");
    }

    [Fact]
    public void Collapse_SingleDirections_NoCount()
    {
        var result = _sut.Collapse(["norte", "este", "sur"]);

        result.Should().Be("nes");
    }

    [Fact]
    public void Reverse_SimplePath_ReversesCorrectly()
    {
        var result = _sut.Reverse("3sn");

        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(["sur", "norte", "norte", "norte"]);
    }

    [Fact]
    public void Reverse_UnknownDirection_ReturnsNull()
    {
        // 'x' is not in the dictionary
        var engine = new PathEngine(new DirectionDictionary());
        engine.Reverse("x").Should().BeNull();
    }

    [Fact]
    public void IsValid_AllKnownDirections_ReturnsTrue()
    {
        _sut.IsValid("3s2en").Should().BeTrue();
    }

    [Fact]
    public void IsValid_UnknownDirection_ReturnsFalse()
    {
        _sut.IsValid("3sx").Should().BeFalse();
    }

    [Fact]
    public void IsValid_EmptyPath_ReturnsFalse()
    {
        _sut.IsValid("").Should().BeFalse();
    }

    [Fact]
    public void IsValid_OnlyNumbers_ReturnsFalse()
    {
        _sut.IsValid("123").Should().BeFalse();
    }
}
