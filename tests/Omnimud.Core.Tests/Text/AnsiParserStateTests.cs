using FluentAssertions;
using Omnimud.Core.Text;

namespace Omnimud.Core.Tests.Text;

public class AnsiParserStateTests
{
    private const string Esc = "\x1b";
    private readonly AnsiParser _sut = new();

    [Fact]
    public void ParseLine_ColorSetInOneLine_ContinuesInTheNext()
    {
        _sut.ParseLine($"{Esc}[31mrojo");
        var second = _sut.ParseLine("sigue rojo");

        second.Segments.Should().ContainSingle();
        second.Segments[0].Style.Foreground.Should().Be(AnsiColor.Red);
        second.PlainText.Should().Be("sigue rojo");
    }

    [Fact]
    public void ParseLine_ResetInLaterLine_EndsTheColor()
    {
        _sut.ParseLine($"{Esc}[1;32mverde");
        _sut.ParseLine($"aún verde{Esc}[0m");
        var third = _sut.ParseLine("normal");

        third.Segments[0].Style.Should().Be(AnsiStyle.Default);
        _sut.CurrentStyle.Should().Be(AnsiStyle.Default);
    }

    [Fact]
    public void ParseLine_EmptyLine_KeepsTheStyle()
    {
        _sut.ParseLine($"{Esc}[34mazul");
        var empty = _sut.ParseLine("");
        var next = _sut.ParseLine("azul todavía");

        empty.Segments.Should().BeEmpty();
        empty.PlainText.Should().BeEmpty();
        next.Segments[0].Style.Foreground.Should().Be(AnsiColor.Blue);
    }

    [Fact]
    public void ParseLine_PlainText_HasNoEscapeSequences()
    {
        var line = _sut.ParseLine($"{Esc}[1mHola{Esc}[0m {Esc}[33mmundo{Esc}[0m");

        line.PlainText.Should().Be("Hola mundo");
        string.Concat(line.Segments.Select(s => s.Text)).Should().Be("Hola mundo");
    }

    [Fact]
    public void Reset_GoesBackToDefault()
    {
        _sut.ParseLine($"{Esc}[31mrojo");

        _sut.Reset();

        _sut.ParseLine("texto").Segments[0].Style.Should().Be(AnsiStyle.Default);
    }

    [Fact]
    public void Parse_IsStateless_EvenAfterParseLine()
    {
        _sut.ParseLine($"{Esc}[31mrojo");

        _sut.Parse("texto")[0].Style.Should().Be(AnsiStyle.Default);
    }

    [Theory]
    [InlineData("\x1b[2J")]
    [InlineData("\x1b[H")]
    [InlineData("\x1b[K")]
    [InlineData("\x1b[10;20H")]
    [InlineData("\x1b[?25l")]
    [InlineData("\x1b[1A")]
    public void Parse_NonSgrCsiSequence_IsDiscarded(string sequence)
    {
        var segments = _sut.Parse($"antes{sequence}después");

        string.Concat(segments.Select(s => s.Text)).Should().Be("antesdespués");
        segments.Should().OnlyContain(s => s.Style == AnsiStyle.Default);
    }

    [Fact]
    public void Parse_NonSgrSequence_DoesNotEatFollowingTextUpToAnM()
    {
        // The original client swallowed everything up to the next 'm'.
        var segments = _sut.Parse($"{Esc}[2Jcamino de montaña");

        string.Concat(segments.Select(s => s.Text)).Should().Be("camino de montaña");
    }

    [Fact]
    public void Parse_TruncatedSequenceAtEnd_IsDropped()
    {
        var segments = _sut.Parse($"texto{Esc}[3");

        string.Concat(segments.Select(s => s.Text)).Should().Be("texto");
    }

    [Fact]
    public void Parse_LoneEscapeAndTwoCharacterEscapes_AreDropped()
    {
        string.Concat(_sut.Parse($"a{Esc}7b{Esc}").Select(s => s.Text)).Should().Be("ab");
    }

    [Fact]
    public void Parse_OscSequence_IsDropped()
    {
        string.Concat(_sut.Parse($"a{Esc}]0;título\ab").Select(s => s.Text)).Should().Be("ab");
    }

    [Fact]
    public void Parse_SameStyleTwice_MergesSegments()
    {
        var segments = _sut.Parse($"{Esc}[31muno{Esc}[31mdos");

        segments.Should().ContainSingle().Which.Text.Should().Be("unodos");
    }

    [Fact]
    public void Strip_RemovesEverySequence()
    {
        AnsiParser.Strip($"{Esc}[1;31mHola{Esc}[0m{Esc}[2J mundo").Should().Be("Hola mundo");
        AnsiParser.Strip("sin códigos").Should().Be("sin códigos");
        AnsiParser.Strip("").Should().BeEmpty();
    }
}
