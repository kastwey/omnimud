using FluentAssertions;
using Omnimud.Core.Commands;

namespace Omnimud.Core.Tests.Commands;

public class InputProcessorParsingTests
{
    [Theory]
    [InlineData("norte;sur", new[] { "norte", "sur" })]
    [InlineData("decir hola;;adiós", new[] { "decir hola;adiós" })]
    [InlineData("a;;b;c", new[] { "a;b", "c" })]
    [InlineData("a;;;b", new[] { "a;", "b" })]
    [InlineData(";a;;;;b;", new[] { "a;;b" })]
    [InlineData(";;;", new[] { ";" })]
    [InlineData(";", new string[0])]
    [InlineData("sin separador", new[] { "sin separador" })]
    public void SplitConcatenated_SplitsAndUnescapes(string input, string[] expected)
    {
        InputProcessor.SplitConcatenated(input, ';').Should().Equal(expected);
    }

    [Fact]
    public void SplitConcatenated_UsesTheConfiguredCharacterForTheEscapeToo()
    {
        InputProcessor.SplitConcatenated("decir a||b|mirar;ahora", '|').Should().Equal("decir a|b", "mirar;ahora");
    }

    [Theory]
    [InlineData("3#norte", true, 3, "norte")]
    [InlineData("50#x", true, 50, "x")]
    [InlineData("51#x", true, 50, "x")]
    [InlineData("99999999999999#x", true, 50, "x")]
    [InlineData("0#x", true, 0, "x")]
    [InlineData("2#decir #1", true, 2, "decir #1")]
    [InlineData("#x", false, 0, "")]
    [InlineData("3#", false, 0, "")]
    [InlineData("-3#x", false, 0, "")]
    [InlineData("tres#x", false, 0, "")]
    [InlineData("decir 3#x", false, 0, "")]
    [InlineData("norte", false, 0, "")]
    public void TryParseRepeat_ParsesCountAndCommand(string input, bool expected, int count, string command)
    {
        InputProcessor.TryParseRepeat(input, '#', out var actualCount, out var actualCommand).Should().Be(expected);
        actualCount.Should().Be(count);
        actualCommand.Should().Be(command);
    }
}
