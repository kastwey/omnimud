using FluentAssertions;
using Omnimud.Core.Updates;

namespace Omnimud.Core.Tests.Updates;

public sealed class SemanticVersionTests
{
    private static SemanticVersion V(string text) => SemanticVersion.Parse(text) ?? throw new FormatException(text);

    // ── Parsing ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2.1.0", 2, 1, 0, 0, "")]
    [InlineData("v2.1.0", 2, 1, 0, 0, "")]
    [InlineData("V2.1.0", 2, 1, 0, 0, "")]
    [InlineData("2.1", 2, 1, 0, 0, "")]
    [InlineData("2", 2, 0, 0, 0, "")]
    [InlineData("v3", 3, 0, 0, 0, "")]
    [InlineData("2.0.0.7", 2, 0, 0, 7, "")]
    [InlineData("2.1.0-beta.1", 2, 1, 0, 0, "beta.1")]
    [InlineData("v2.1.0-rc1", 2, 1, 0, 0, "rc1")]
    [InlineData("2.1-alpha", 2, 1, 0, 0, "alpha")]
    [InlineData("2.0.0+abc123", 2, 0, 0, 0, "")]
    [InlineData("2.0.0-beta.2+5f3e1c", 2, 0, 0, 0, "beta.2")]
    [InlineData("  v2.1.0  ", 2, 1, 0, 0, "")]
    [InlineData("10.20.30", 10, 20, 30, 0, "")]
    [InlineData("1.0.0-x-y.z", 1, 0, 0, 0, "x-y.z")]
    [InlineData("01.002.0003", 1, 2, 3, 0, "")]
    public void Parse_AcceptsTheUsualSpellings(string text, int major, int minor, int patch, int revision, string preRelease)
    {
        SemanticVersion.TryParse(text, out var version).Should().BeTrue();
        (version.Major, version.Minor, version.Patch, version.Revision, version.PreRelease)
            .Should().Be((major, minor, patch, revision, preRelease));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("v")]
    [InlineData("latest")]
    [InlineData("two.one")]
    [InlineData("2..1")]
    [InlineData("2.1.")]
    [InlineData(".2.1")]
    [InlineData("2.1.0.0.0")]
    [InlineData("-1.0.0")]
    [InlineData("2.1.0-")]
    [InlineData("2.1.0-beta..1")]
    [InlineData("2.1.0-be ta")]
    [InlineData("2.1.0-béta")]
    [InlineData("2.x.0")]
    [InlineData("2,1,0")]
    [InlineData("2.1.0 beta")]
    [InlineData("99999999999.0.0")]
    [InlineData("vv2.1.0")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("1.0.0\n2.0.0")]
    public void Parse_RejectsGarbage(string? text)
    {
        SemanticVersion.TryParse(text, out _).Should().BeFalse();
        SemanticVersion.Parse(text).Should().BeNull();
    }

    [Fact]
    public void Parse_RejectsAbsurdlyLongText() =>
        SemanticVersion.TryParse("1." + new string('1', 200), out _).Should().BeFalse();

    // ── Comparing ──────────────────────────────────────────────────────────

    /// <summary>The bug of the original client: it removed the dots and compared 1.10 and 1.1 as equal.</summary>
    [Fact]
    public void OneDotTen_IsNewerThanOneDotOne_AndThanOneDotNine()
    {
        (V("1.10") > V("1.1")).Should().BeTrue();
        (V("1.10") > V("1.9")).Should().BeTrue();
        (V("1.10.0.0") > V("1.1.0.0")).Should().BeTrue();
        V("1.10").Should().NotBe(V("1.1"));
    }

    [Theory]
    [InlineData("2.0.1", "2.0.0")]
    [InlineData("2.1.0", "2.0.9")]
    [InlineData("3.0.0", "2.99.99")]
    [InlineData("2.10.0", "2.9.0")]
    [InlineData("2.0.10", "2.0.9")]
    [InlineData("10.0.0", "9.0.0")]
    [InlineData("2.0.0.1", "2.0.0")]
    [InlineData("2.0.0.10", "2.0.0.9")]
    [InlineData("2.1", "2.0.5")]
    [InlineData("v2.1.0", "2.0.0")]
    [InlineData("2.1.0", "2.1.0-beta.1")]
    [InlineData("2.1.0", "2.1.0-rc.9")]
    [InlineData("2.1.0-beta.1", "2.0.0")]
    [InlineData("2.1.0-beta.2", "2.1.0-beta.1")]
    [InlineData("2.1.0-beta.10", "2.1.0-beta.9")]
    [InlineData("2.1.0-beta", "2.1.0-alpha")]
    [InlineData("2.1.0-rc.1", "2.1.0-beta.11")]
    [InlineData("2.1.0-beta.1.1", "2.1.0-beta.1")]
    [InlineData("2.1.0-beta.x", "2.1.0-beta.99")]
    public void Newer_IsGreaterThanOlder(string newer, string older)
    {
        V(newer).CompareTo(V(older)).Should().BePositive();
        V(older).CompareTo(V(newer)).Should().BeNegative();
        (V(newer) > V(older)).Should().BeTrue();
        (V(newer) >= V(older)).Should().BeTrue();
        (V(older) < V(newer)).Should().BeTrue();
        (V(older) <= V(newer)).Should().BeTrue();
        V(newer).Should().NotBe(V(older));
    }

    [Theory]
    [InlineData("2.1.0", "v2.1.0")]
    [InlineData("2.1", "2.1.0")]
    [InlineData("2", "2.0.0.0")]
    [InlineData("2.1.0+build1", "2.1.0+build2")]
    [InlineData("2.1.0-BETA.1", "2.1.0-beta.1")]
    [InlineData("02.01.00", "2.1.0")]
    public void DifferentSpellingsOfTheSameVersion_AreEqual(string a, string b)
    {
        V(a).CompareTo(V(b)).Should().Be(0);
        V(a).Should().Be(V(b));
        V(a).GetHashCode().Should().Be(V(b).GetHashCode());
        (V(a) >= V(b)).Should().BeTrue();
        (V(a) <= V(b)).Should().BeTrue();
    }

    [Fact]
    public void AnythingIsGreaterThanNull() => V("0.0.1").CompareTo(null).Should().BePositive();

    [Fact]
    public void Sorting_PutsVersionsInReleaseOrder()
    {
        var shuffled = new[] { "2.0.0", "1.10.0", "2.0.0-rc.1", "1.9.0", "2.0.0-beta.2", "1.1.0", "2.0.0-beta.10", "2.0.1" };
        shuffled.Select(V).Order().Select(v => v.ToString())
            .Should().Equal("1.1.0", "1.9.0", "1.10.0", "2.0.0-beta.2", "2.0.0-beta.10", "2.0.0-rc.1", "2.0.0", "2.0.1");
    }

    [Theory]
    [InlineData("v2.1", "2.1.0")]
    [InlineData("2.1.0-beta.1+abc", "2.1.0-beta.1")]
    [InlineData("2.0.0.3", "2.0.0.3")]
    [InlineData("2.0.0.0", "2.0.0")]
    public void ToString_IsTheCanonicalForm(string text, string expected) => V(text).ToString().Should().Be(expected);

    [Fact]
    public void Constructor_RejectsNegativeNumbers()
    {
        var act = () => new SemanticVersion(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
