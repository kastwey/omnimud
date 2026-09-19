using FluentAssertions;
using Omnimud.Core.Paths;

namespace Omnimud.Core.Tests.Paths;

public class DirectionDictionaryTests
{
    private readonly DirectionDictionary _sut = new();

    public DirectionDictionaryTests()
    {
        _sut.Load([
            new DirectionEntry("norte", 'n', "sur"),
            new DirectionEntry("sur", 's', "norte"),
            new DirectionEntry("este", 'e', "oeste"),
            new DirectionEntry("oeste", 'o', "este"),
        ]);
    }

    [Fact]
    public void GetByAbbreviation_KnownAbbr_ReturnsEntry()
    {
        _sut.GetByAbbreviation('n').Should().NotBeNull();
        _sut.GetByAbbreviation('n')!.FullName.Should().Be("norte");
    }

    [Fact]
    public void GetByAbbreviation_Unknown_ReturnsNull()
    {
        _sut.GetByAbbreviation('x').Should().BeNull();
    }

    [Fact]
    public void GetByName_KnownName_ReturnsEntry()
    {
        _sut.GetByName("norte")!.Abbreviation.Should().Be('n');
    }

    [Fact]
    public void GetByName_CaseInsensitive()
    {
        _sut.GetByName("NORTE").Should().NotBeNull();
    }

    [Fact]
    public void IsKnownDirection_ByName_ReturnsTrue()
    {
        _sut.IsKnownDirection("norte").Should().BeTrue();
    }

    [Fact]
    public void IsKnownDirection_ByAbbr_ReturnsTrue()
    {
        _sut.IsKnownDirection("n").Should().BeTrue();
    }

    [Fact]
    public void IsKnownDirection_Unknown_ReturnsFalse()
    {
        _sut.IsKnownDirection("nowhere").Should().BeFalse();
    }

    [Fact]
    public void GetOpposite_KnownDirection_ReturnsOpposite()
    {
        _sut.GetOpposite("norte").Should().Be("sur");
        _sut.GetOpposite("n").Should().Be("sur");
    }

    [Fact]
    public void GetOpposite_Unknown_ReturnsNull()
    {
        _sut.GetOpposite("nowhere").Should().BeNull();
    }

    [Fact]
    public void Normalize_Abbreviation_ReturnsFullName()
    {
        _sut.Normalize("n").Should().Be("norte");
    }

    [Fact]
    public void Normalize_FullName_ReturnsSame()
    {
        _sut.Normalize("norte").Should().Be("norte");
    }

    [Fact]
    public void Normalize_Unknown_ReturnsSame()
    {
        _sut.Normalize("nowhere").Should().Be("nowhere");
    }

    [Fact]
    public void GetAll_ReturnsAllEntries()
    {
        _sut.GetAll().Should().HaveCount(4);
    }

    [Fact]
    public void Load_ReplacesExisting()
    {
        _sut.Load([new DirectionEntry("arriba", 'u', "abajo")]);

        _sut.GetAll().Should().HaveCount(1);
        _sut.GetByName("norte").Should().BeNull();
    }
}
