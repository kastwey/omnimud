using FluentAssertions;
using Omnimud.Core.Actions;

namespace Omnimud.Core.Tests.Actions;

/// <summary>Labels and commands come from the MUD: whatever it sends, they end up as one clean, short line.</summary>
public sealed class ActionMenuTextTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("  espada larga  ", "espada larga")]
    [InlineData("poción (3)", "poción (3)")]
    [InlineData("Ñandú añil: ¿cigüeña? ¡sí!", "Ñandú añil: ¿cigüeña? ¡sí!")]
    [InlineData("剣 と 盾", "剣 と 盾")]
    [InlineData("Tom & Jerry", "Tom & Jerry")]
    public void CleanLabel_KeepsOrdinaryText_Trimmed(string? input, string expected)
        => ActionMenuText.CleanLabel(input).Should().Be(expected);

    [Theory]
    [InlineData("uno\ndos", "uno dos")]
    [InlineData("uno\r\ndos", "uno dos")]
    [InlineData("uno\tdos", "uno dos")]
    [InlineData("uno\0dos", "uno dos")]
    [InlineData("uno\u0085dos", "uno dos")]      // NEL
    [InlineData("uno\u2028dos", "uno dos")]      // line separator
    [InlineData("uno\u2029dos", "uno dos")]      // paragraph separator
    [InlineData("\n\nuno\n\n", "uno")]
    [InlineData("\a\b\f\v", "")]
    public void ControlCharactersAndLineBreaks_BecomeOneSpace_InLabelsAndCommands(string input, string expected)
    {
        ActionMenuText.CleanLabel(input).Should().Be(expected);
        ActionMenuText.CleanCommand(input).Should().Be(expected);
    }

    [Fact]
    public void ACommandWithALineBreak_CannotSmuggleASecondCommand()
    {
        var command = ActionMenuText.CleanCommand("coger espada\nabandonar\r\nborrar personaje");

        command.Should().Be("coger espada abandonar borrar personaje");
        command.Should().NotContainAny("\n", "\r");
    }

    [Fact]
    public void AnsiSequences_AreRemoved_NotLeftAsGarbage()
    {
        ActionMenuText.CleanLabel("\u001B[1;32mespada\u001B[0m brillante").Should().Be("espada brillante");
        ActionMenuText.CleanCommand("\u001B[31mcoger\u001B[0m espada").Should().Be("coger espada");
    }

    [Theory]
    [InlineData("abc\u202Etxt.exe", "abc txt.exe")]   // right-to-left override
    [InlineData("a\u2066b\u2069c", "a b c")]          // isolates
    [InlineData("\uFEFFhola", "hola")]                // byte order mark
    public void CharactersThatReorderTheText_AreRemoved(string input, string expected)
        => ActionMenuText.CleanLabel(input).Should().Be(expected);

    [Fact]
    public void Emoji_SurviveWhole_AndLoneSurrogatesAreDropped()
    {
        ActionMenuText.CleanLabel("poción 🧪").Should().Be("poción 🧪");
        ActionMenuText.CleanLabel("a\uD83Eb").Should().Be("ab");
        ActionMenuText.CleanLabel("a\uDDEAb").Should().Be("ab");
    }

    [Fact]
    public void Label_IsCutTo80Characters_EndingWithAnEllipsis()
    {
        var label = ActionMenuText.CleanLabel(new string('x', 500));

        label.Should().HaveLength(ActionMenuLimits.MaxLabelLength);
        label.Should().Be(new string('x', 79) + "…");
        ActionMenuText.CleanLabel(new string('x', 80)).Should().Be(new string('x', 80), "exactly the limit fits");
    }

    [Fact]
    public void Command_IsCutTo255Characters()
    {
        ActionMenuText.CleanCommand(new string('x', 5000)).Should().Be(new string('x', ActionMenuLimits.MaxCommandLength));
        ActionMenuText.CleanCommand(new string('x', 255)).Should().HaveLength(255);
    }

    [Fact]
    public void Cutting_NeverLeavesHalfASurrogatePair()
    {
        var label = ActionMenuText.CleanLabel(new string('x', 78) + "🧪🧪🧪");
        var command = ActionMenuText.CleanCommand(new string('x', 254) + "🧪");

        label.Should().Be(new string('x', 78) + "…");
        command.Should().Be(new string('x', 254));
    }

    [Fact]
    public void InnerSpacesOfACommand_AreRespected()
        => ActionMenuText.CleanCommand("decir hola   mundo").Should().Be("decir hola   mundo");
}
