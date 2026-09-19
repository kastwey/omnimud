using FluentAssertions;
using Omnimud.Core.Triggers;

namespace Omnimud.Core.Tests.Triggers;

public class SscanfMatcherTests
{
    [Fact]
    public void Match_DigitPattern_CapturesNumbers()
    {
        var result = SscanfMatcher.Match("You have 100 gold", "You have %d gold", caseSensitive: false);

        result.Should().NotBeNull();
        result![0].Should().Be("100");
    }

    [Fact]
    public void Match_StringPattern_CapturesAny()
    {
        var result = SscanfMatcher.Match("Guard says 'Hello'", "%s says '%s'", caseSensitive: false);

        result.Should().NotBeNull();
        result!.Should().HaveCount(2);
        result[0].Should().Be("Guard");
        result[1].Should().Be("Hello");
    }

    [Fact]
    public void Match_WordPattern_CapturesWord()
    {
        var result = SscanfMatcher.Match("Goblin attacks", "%w attacks", caseSensitive: false);

        result.Should().NotBeNull();
        result![0].Should().Be("Goblin");
    }

    [Fact]
    public void Match_EscapedPercent_TreatedAsLiteral()
    {
        var result = SscanfMatcher.Match("50% done", "%d%% done", caseSensitive: false);

        result.Should().NotBeNull();
        result![0].Should().Be("50");
    }

    [Fact]
    public void Match_NoMatch_ReturnsNull()
    {
        var result = SscanfMatcher.Match("nothing here", "You have %d gold", caseSensitive: false);

        result.Should().BeNull();
    }

    [Fact]
    public void Match_CaseSensitive_Respects()
    {
        SscanfMatcher.Match("HELLO world", "hello %w", caseSensitive: true)
            .Should().BeNull();

        SscanfMatcher.Match("HELLO world", "hello %w", caseSensitive: false)
            .Should().NotBeNull();
    }

    [Fact]
    public void Match_FixedLengthDigit_CapturesExact()
    {
        var result = SscanfMatcher.Match("Code: 42X", "Code: %2dX", caseSensitive: false);

        result.Should().NotBeNull();
        result![0].Should().Be("42");
    }

    [Fact]
    public void Match_MultipleMixedCaptures()
    {
        var result = SscanfMatcher.Match(
            "Player1 hits monster for 25 damage",
            "%w hits %w for %d damage",
            caseSensitive: false);

        result.Should().NotBeNull();
        result!.Should().HaveCount(3);
        result[0].Should().Be("Player1");
        result[1].Should().Be("monster");
        result[2].Should().Be("25");
    }
}
