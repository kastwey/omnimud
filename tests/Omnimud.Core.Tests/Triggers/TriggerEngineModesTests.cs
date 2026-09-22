using FluentAssertions;
using Omnimud.Core.Triggers;

namespace Omnimud.Core.Tests.Triggers;

public class TriggerEngineModesTests
{
    private readonly TriggerEngine _sut = new(new TriggerMatcher());

    private static TriggerDefinition Make(string pattern, string name, int priority = 50, bool multiline = false,
        PatternType type = PatternType.Literal, bool caseSensitive = false)
        => new()
        {
            Id = name,
            Name = name,
            Pattern = pattern,
            PatternType = type,
            Action = "x",
            Priority = priority,
            Multiline = multiline,
            CaseSensitive = caseSensitive
        };

    [Fact]
    public void ProcessLine_IgnoresMultilineAndCommandTriggers()
    {
        _sut.LoadTriggers([Make("orco", "linea"), Make("orco", "bloque", multiline: true), Make("@orco", "comando")]);

        _sut.ProcessLine("un orco").Select(m => m.Trigger.Name).Should().Equal("linea");
    }

    [Fact]
    public void ProcessBlock_OnlyMultilineTriggers()
    {
        _sut.LoadTriggers([Make("orco", "linea"), Make("orco", "bloque", multiline: true)]);

        _sut.ProcessBlock("un orco\ny otro").Select(m => m.Trigger.Name).Should().Equal("bloque");
    }

    [Fact]
    public void Process_NeverReturnsCommandTriggers()
    {
        _sut.LoadTriggers([Make("@mirar", "comando")]);

        _sut.Process("@mirar").Should().BeEmpty();
    }

    [Fact]
    public void ProcessLine_OrdersByPriorityThenLoadOrder()
    {
        _sut.LoadTriggers(
        [
            Make("a", "p50-primero"),
            Make("a", "p90", priority: 90),
            Make("a", "p50-segundo"),
            Make("a", "p10", priority: 10),
            Make("a", "p50-tercero")
        ]);

        _sut.ProcessLine("a").Select(m => m.Trigger.Name)
            .Should().Equal("p90", "p50-primero", "p50-segundo", "p50-tercero", "p10");
    }

    [Fact]
    public void MatchCommand_RespectsCaseSensitivity()
    {
        _sut.LoadTriggers([Make("@Cura", "sensible", caseSensitive: true), Make("@cura", "insensible")]);

        _sut.MatchCommand("cura").Select(t => t.Name).Should().Equal("insensible");
        _sut.MatchCommand("Cura").Select(t => t.Name).Should().Equal("sensible", "insensible");
        _sut.MatchCommand("curar").Should().BeEmpty();
    }

    [Fact]
    public void DisableAll_AlsoDisablesCommandTriggers()
    {
        _sut.LoadTriggers([Make("@cura", "c")]);

        _sut.DisableAll();

        _sut.MatchCommand("cura").Should().BeEmpty();
        _sut.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void RegexLineTrigger_AnchorsAreThoseOfTheLine()
    {
        _sut.LoadTriggers([Make(@"^(\w+) llega\.$", "r", type: PatternType.Regex)]);

        _sut.ProcessLine("Gandalf llega.").Single().Captures.Should().Equal("Gandalf");
        _sut.ProcessLine("Dicen que Gandalf llega.").Should().BeEmpty();
    }

    [Fact]
    public void RegexMultilineTrigger_AnchorsMatchInsideTheBlock()
    {
        _sut.LoadTriggers([Make(@"^(\w+) llega\.$", "r", type: PatternType.Regex, multiline: true)]);

        var match = _sut.ProcessBlock("Hace frío.\nGandalf llega.\nNieva.").Single();

        match.Captures.Should().Equal("Gandalf");
    }

    [Fact]
    public void RegexMultilineTrigger_DotCrossesLineBreaks_LikeTheOriginalClient()
    {
        // The original compiled its regexes with Singleline: "." also matched a line break, so a block
        // trigger could capture across lines.
        _sut.LoadTriggers([Make(@"Te dice: '(.+)'", "r", type: PatternType.Regex, multiline: true)]);

        var match = _sut.ProcessBlock("Gandalf te dice: 'Ven al\nconsejo ahora'").Single();

        match.Captures.Should().Equal("Ven al\nconsejo ahora");
    }

    [Fact]
    public void RegexTrigger_NamedAndNumberedGroupsBecomeCaptures()
    {
        _sut.LoadTriggers([Make(@"(\d+)/(\d+) pv", "r", type: PatternType.Regex)]);

        _sut.ProcessLine("Tienes 40/120 pv").Single().Captures.Should().Equal("40", "120");
    }

    [Fact]
    public void InvalidRegex_NeverThrowsNorMatches()
    {
        _sut.LoadTriggers([Make("(sin cerrar", "r", type: PatternType.Regex)]);

        _sut.ProcessLine("(sin cerrar").Should().BeEmpty();
    }

    [Fact]
    public void SscanfTrigger_LeadingCaretAndTrailingDollarAreAnchors()
    {
        _sut.LoadTriggers([Make("^%s ha muerto.$", "s", type: PatternType.Sscanf)]);

        _sut.ProcessLine("El orco ha muerto.").Single().Captures.Should().Equal("El orco");
        _sut.ProcessLine("Dicen que el orco ha muerto. Bien").Should().BeEmpty();
    }

    [Fact]
    public void SscanfMultilineTrigger_TreatsTheBlockAsOneLine()
    {
        _sut.LoadTriggers([Make("%s te dice: '%s'", "s", type: PatternType.Sscanf, multiline: true)]);

        _sut.ProcessBlock("Ana te dice: 'hola\nqué tal'").Single().Captures.Should().Equal("Ana", "hola qué tal");
    }

    [Fact]
    public void FindByName_PrefersExactCase()
    {
        _sut.LoadTriggers([Make("a", "Cura"), Make("b", "cura")]);

        _sut.FindByName("cura")!.Pattern.Should().Be("b");
        _sut.FindByName("CURA")!.Pattern.Should().Be("a");
        _sut.FindByName("nada").Should().BeNull();
    }

    [Theory]
    [InlineData("decir %1 y %2", "decir uno y dos")]
    [InlineData("%2%1", "dosuno")]
    [InlineData("100%% de %1", "100%% de uno")]
    [InlineData("%3 no existe", "%3 no existe")]
    public void SubstituteCaptures_ReplacesPlaceholders(string action, string expected)
    {
        TriggerCaptures.Substitute(action, ["uno", "dos"]).Should().Be(expected);
    }

    [Fact]
    public void SubstituteCaptures_TenthCaptureIsNotEatenByTheFirst()
    {
        var captures = Enumerable.Range(1, 10).Select(i => $"c{i}").ToArray();

        TriggerCaptures.Substitute("%10-%1-%9", captures).Should().Be("c10-c1-c9");
    }

    [Fact]
    public void SubstituteCaptures_WithNineCaptures_Percent10IsCaptureOnePlusZero()
    {
        var captures = Enumerable.Range(1, 9).Select(i => $"c{i}").ToArray();

        TriggerCaptures.Substitute("%10", captures).Should().Be("c10");
    }

    [Fact]
    public void SubstituteCaptures_CapturedTextIsNotSubstitutedAgain()
    {
        TriggerCaptures.Substitute("%1 %2", ["%2", "x"]).Should().Be("%2 x");
    }
}
