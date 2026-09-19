using FluentAssertions;
using Omnimud.Core.Triggers;

namespace Omnimud.Core.Tests.Triggers;

public class TriggerMatcherTests
{
    private readonly TriggerMatcher _sut = new();

    #region Literal Matching

    [Fact]
    public void Literal_SubstringMatch_ReturnsTrue()
    {
        var trigger = MakeTrigger("monster attacks", PatternType.Literal);

        _sut.TryMatch("The monster attacks you!", trigger, out var match).Should().BeTrue();
        match!.Trigger.Should().BeSameAs(trigger);
    }

    [Fact]
    public void Literal_NoMatch_ReturnsFalse()
    {
        var trigger = MakeTrigger("dragon", PatternType.Literal);

        _sut.TryMatch("The monster attacks", trigger, out _).Should().BeFalse();
    }

    [Fact]
    public void Literal_CaseInsensitive_MatchesAnyCase()
    {
        var trigger = MakeTrigger("HELLO", PatternType.Literal, caseSensitive: false);

        _sut.TryMatch("hello world", trigger, out _).Should().BeTrue();
    }

    [Fact]
    public void Literal_CaseSensitive_RequiresExactCase()
    {
        var trigger = MakeTrigger("Hello", PatternType.Literal, caseSensitive: true);

        _sut.TryMatch("hello world", trigger, out _).Should().BeFalse();
        _sut.TryMatch("Hello world", trigger, out _).Should().BeTrue();
    }

    [Fact]
    public void Literal_AnchorStart_MatchesLineBeginning()
    {
        var trigger = MakeTrigger("^You see", PatternType.Literal);

        _sut.TryMatch("You see a monster", trigger, out _).Should().BeTrue();
        _sut.TryMatch("Before: You see nothing", trigger, out _).Should().BeFalse();
    }

    [Fact]
    public void Literal_AnchorEnd_MatchesLineEnd()
    {
        var trigger = MakeTrigger("is dead.$", PatternType.Literal);

        _sut.TryMatch("The monster is dead.", trigger, out _).Should().BeTrue();
        _sut.TryMatch("The monster is dead. You win!", trigger, out _).Should().BeFalse();
    }

    [Fact]
    public void Literal_BothAnchors_MatchesExactLine()
    {
        var trigger = MakeTrigger("^You are dead.$", PatternType.Literal);

        _sut.TryMatch("You are dead.", trigger, out _).Should().BeTrue();
        _sut.TryMatch("Oh no! You are dead.", trigger, out _).Should().BeFalse();
    }

    [Fact]
    public void Literal_MultilineText_MatchesPerLine()
    {
        var trigger = MakeTrigger("^Line two$", PatternType.Literal);

        _sut.TryMatch("Line one\nLine two\nLine three", trigger, out _).Should().BeTrue();
    }

    #endregion

    #region Regex Matching

    [Fact]
    public void Regex_SimplePattern_Matches()
    {
        var trigger = MakeTrigger(@"(\d+) gold coins", PatternType.Regex);

        _sut.TryMatch("You find 500 gold coins", trigger, out var match).Should().BeTrue();
        match!.Captures.Should().ContainSingle().Which.Should().Be("500");
    }

    [Fact]
    public void Regex_NoMatch_ReturnsFalse()
    {
        var trigger = MakeTrigger(@"dragon (\w+)", PatternType.Regex);

        _sut.TryMatch("a simple monster", trigger, out _).Should().BeFalse();
    }

    [Fact]
    public void Regex_MultipleCaptures_ReturnsAll()
    {
        var trigger = MakeTrigger(@"(\w+) says '(.+)'", PatternType.Regex);

        _sut.TryMatch("Guard says 'Halt!'", trigger, out var match).Should().BeTrue();
        match!.Captures.Should().HaveCount(2);
        match.Captures[0].Should().Be("Guard");
        match.Captures[1].Should().Be("Halt!");
    }

    [Fact]
    public void Regex_CatastrophicBacktracking_TimesOutGracefully()
    {
        // This regex causes catastrophic backtracking
        var trigger = MakeTrigger(@"(a+)+b", PatternType.Regex);
        var text = new string('a', 30); // No 'b' at end → causes backtracking

        _sut.TryMatch(text, trigger, out _).Should().BeFalse();
        // Should NOT throw or hang
    }

    [Fact]
    public void Regex_InvalidPattern_ReturnsFalse()
    {
        var trigger = MakeTrigger(@"[invalid", PatternType.Regex);

        _sut.TryMatch("some text", trigger, out _).Should().BeFalse();
    }

    [Fact]
    public void Regex_CaseInsensitive_Works()
    {
        var trigger = MakeTrigger(@"HELLO (\w+)", PatternType.Regex, caseSensitive: false);

        _sut.TryMatch("hello world", trigger, out var match).Should().BeTrue();
        match!.Captures[0].Should().Be("world");
    }

    #endregion

    #region Sscanf Matching

    [Fact]
    public void Sscanf_DigitCapture_Works()
    {
        var trigger = MakeTrigger("Tienes %d monedas", PatternType.Sscanf);

        _sut.TryMatch("Tienes 500 monedas de oro", trigger, out var match).Should().BeTrue();
        match!.Captures[0].Should().Be("500");
    }

    [Fact]
    public void Sscanf_StringCapture_Works()
    {
        var trigger = MakeTrigger("%s dice '%s'", PatternType.Sscanf);

        _sut.TryMatch("Guard dice 'Hola viajero'", trigger, out var match).Should().BeTrue();
        match!.Captures.Should().HaveCount(2);
        match.Captures[0].Should().Be("Guard");
    }

    [Fact]
    public void Sscanf_WordCapture_Works()
    {
        var trigger = MakeTrigger("%w ataca", PatternType.Sscanf);

        _sut.TryMatch("Goblin ataca con furia", trigger, out var match).Should().BeTrue();
        match!.Captures[0].Should().Be("Goblin");
    }

    [Fact]
    public void Sscanf_NoMatch_ReturnsFalse()
    {
        var trigger = MakeTrigger("Tienes %d monedas", PatternType.Sscanf);

        _sut.TryMatch("No tienes nada", trigger, out _).Should().BeFalse();
    }

    #endregion

    private static TriggerDefinition MakeTrigger(string pattern, PatternType type, bool caseSensitive = false)
    {
        return new TriggerDefinition
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Test",
            Pattern = pattern,
            PatternType = type,
            Action = "test",
            CaseSensitive = caseSensitive
        };
    }
}
