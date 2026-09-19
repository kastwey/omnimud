using FluentAssertions;
using Omnimud.Core.Text;

namespace Omnimud.Core.Tests.Text;

public class AnsiParserTests
{
    private readonly AnsiParser _sut = new();

    [Fact]
    public void Parse_PlainText_ReturnsSingleDefaultSegment()
    {
        var result = _sut.Parse("Hello World");

        result.Should().HaveCount(1);
        result[0].Text.Should().Be("Hello World");
        result[0].Style.Should().Be(AnsiStyle.Default);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsEmptyList()
    {
        _sut.Parse("").Should().BeEmpty();
        _sut.Parse(null!).Should().BeEmpty();
    }

    [Fact]
    public void Parse_RedText_ReturnsForegroundRed()
    {
        var result = _sut.Parse("\x1b[31mRed Text");

        result.Should().HaveCount(1);
        result[0].Text.Should().Be("Red Text");
        result[0].Style.Foreground.Should().Be(AnsiColor.Red);
    }

    [Fact]
    public void Parse_BoldGreen_ReturnsBoldAndGreen()
    {
        var result = _sut.Parse("\x1b[1;32mBold Green");

        result.Should().HaveCount(1);
        result[0].Text.Should().Be("Bold Green");
        result[0].Style.Bold.Should().BeTrue();
        result[0].Style.Foreground.Should().Be(AnsiColor.Green);
    }

    [Fact]
    public void Parse_Reset_ReturnsDefault()
    {
        var result = _sut.Parse("\x1b[31mRed\x1b[0mNormal");

        result.Should().HaveCount(2);
        result[0].Text.Should().Be("Red");
        result[0].Style.Foreground.Should().Be(AnsiColor.Red);
        result[1].Text.Should().Be("Normal");
        result[1].Style.Should().Be(AnsiStyle.Default);
    }

    [Fact]
    public void Parse_BackgroundColor_ParsesCorrectly()
    {
        var result = _sut.Parse("\x1b[44mBlue Background");

        result.Should().HaveCount(1);
        result[0].Style.Background.Should().Be(AnsiColor.Blue);
    }

    [Fact]
    public void Parse_BrightColors_ParsesCorrectly()
    {
        var result = _sut.Parse("\x1b[91mBright Red");

        result.Should().HaveCount(1);
        result[0].Style.Foreground.Should().Be(AnsiColor.BrightRed);
    }

    [Fact]
    public void Parse_256Color_ParsesCorrectly()
    {
        var result = _sut.Parse("\x1b[38;5;200mExtended");

        result.Should().HaveCount(1);
        result[0].Style.Foreground.Type.Should().Be(AnsiColorType.Extended256);
        result[0].Style.Foreground.Value.Should().Be(200);
    }

    [Fact]
    public void Parse_TrueColor_ParsesCorrectly()
    {
        var result = _sut.Parse("\x1b[38;2;100;150;200mTrueColor");

        result.Should().HaveCount(1);
        result[0].Style.Foreground.Type.Should().Be(AnsiColorType.TrueColor);
        result[0].Style.Foreground.R.Should().Be(100);
        result[0].Style.Foreground.G.Should().Be(150);
        result[0].Style.Foreground.B.Should().Be(200);
    }

    [Fact]
    public void Parse_Underline_ParsesCorrectly()
    {
        var result = _sut.Parse("\x1b[4mUnderlined");

        result.Should().HaveCount(1);
        result[0].Style.Underline.Should().BeTrue();
    }

    [Fact]
    public void Parse_Inverse_ParsesCorrectly()
    {
        var result = _sut.Parse("\x1b[7mInversed");

        result.Should().HaveCount(1);
        result[0].Style.Inverse.Should().BeTrue();
    }

    [Fact]
    public void Parse_ResetAttribute_OnlyResetsTarget()
    {
        var result = _sut.Parse("\x1b[1;4mBoldUnder\x1b[24mBoldOnly");

        result.Should().HaveCount(2);
        result[0].Style.Bold.Should().BeTrue();
        result[0].Style.Underline.Should().BeTrue();
        result[1].Style.Bold.Should().BeTrue();
        result[1].Style.Underline.Should().BeFalse();
    }

    [Fact]
    public void Parse_EmptyEscapeSequence_ResetsToDefault()
    {
        // ESC[m is the same as ESC[0m
        var result = _sut.Parse("\x1b[31mRed\x1b[mDefault");

        result.Should().HaveCount(2);
        result[1].Style.Should().Be(AnsiStyle.Default);
    }

    [Fact]
    public void Parse_TextBetweenEscapes_PreservesAll()
    {
        var result = _sut.Parse("before\x1b[31mred\x1b[0mafter");

        result.Should().HaveCount(3);
        result[0].Text.Should().Be("before");
        result[1].Text.Should().Be("red");
        result[2].Text.Should().Be("after");
    }

    [Fact]
    public void Parse_MultipleStylesAccumulate()
    {
        var result = _sut.Parse("\x1b[1m\x1b[31m\x1b[42mStyled");

        result.Should().HaveCount(1);
        result[0].Style.Bold.Should().BeTrue();
        result[0].Style.Foreground.Should().Be(AnsiColor.Red);
        result[0].Style.Background.Should().Be(AnsiColor.Green);
    }
}
