using FluentAssertions;
using Omnimud.Core.Text;

namespace Omnimud.Core.Tests.Text;

public class MspExtractorLineStartTests
{
    private readonly MspExtractor _sut = new();

    [Fact]
    public void Extract_CommandAtLineStart_IsExtractedAndTheLineIsLeftEmpty()
    {
        var (clean, commands) = _sut.Extract("!!SOUND(thunder.wav V=80)");

        clean.Should().BeEmpty();
        commands.Should().ContainSingle().Which.FileName.Should().Be("thunder.wav");
    }

    [Fact]
    public void Extract_CommandAtLineStart_RestOfTheTextIsReturnedUntouched()
    {
        var (clean, commands) = _sut.Extract("!!MUSIC(city.mp3)  The city gates (north)  ");

        clean.Should().Be("  The city gates (north)  ");
        commands.Should().ContainSingle().Which.Type.Should().Be(SoundType.Music);
    }

    [Theory]
    [InlineData("Bob says: !!SOUND(scream.wav U=http://evil.example)")]
    [InlineData(" !!SOUND(scream.wav)")]
    [InlineData("> !!MUSIC(off)")]
    public void Extract_CommandNotAtLineStart_IsPlainText(string line)
    {
        var (clean, commands) = _sut.Extract(line);

        clean.Should().Be(line);
        commands.Should().BeEmpty();
    }

    [Fact]
    public void Extract_SeveralCommandsGluedAtLineStart_AreAllExtracted()
    {
        var (clean, commands) = _sut.Extract("!!SOUND(a.wav)!!MUSIC(b.mp3 L=-1)text !!SOUND(c.wav)");

        clean.Should().Be("text !!SOUND(c.wav)");
        commands.Select(c => c.FileName).Should().Equal("a.wav", "b.mp3");
        commands[1].Loop.Should().Be(-1);
    }

    [Fact]
    public void Extract_SeveralLines_OnlyLineStartsCount()
    {
        var (clean, commands) = _sut.Extract("first\n!!SOUND(a.wav)\nsay !!SOUND(b.wav)\n!!MUSIC(c.mp3)tail");

        clean.Should().Be("first\n\nsay !!SOUND(b.wav)\ntail");
        commands.Select(c => c.FileName).Should().Equal("a.wav", "c.mp3");
    }

    [Fact]
    public void Extract_UnclosedCommand_IsPlainText()
    {
        var (clean, commands) = _sut.Extract("!!SOUND(thunder.wav V=80");

        clean.Should().Be("!!SOUND(thunder.wav V=80");
        commands.Should().BeEmpty();
    }

    [Theory]
    [InlineData("C=1", true)]
    [InlineData("C=0", false)]
    [InlineData("C=true", true)]
    [InlineData("C=false", false)]
    [InlineData("c=1", true)]
    [InlineData("C=TRUE", true)]
    [InlineData("C=banana", false)]
    [InlineData("", false)]
    public void Extract_Sound_ContinueFlag(string parameter, bool expected)
    {
        var (_, commands) = _sut.Extract($"!!SOUND(a.wav {parameter})");

        commands[0].Continue.Should().Be(expected);
    }

    [Theory]
    [InlineData("C=1", true)]
    [InlineData("C=0", false)]
    [InlineData("C=false", false)]
    [InlineData("", true)]
    public void Extract_Music_ContinuesByDefaultAsTheStandardSays(string parameter, bool expected)
    {
        var (_, commands) = _sut.Extract($"!!MUSIC(a.mp3 {parameter})");

        commands[0].Continue.Should().Be(expected);
    }

    [Fact]
    public void Extract_ParametersInAnyOrderAndLowercase_AreUnderstood()
    {
        var (_, commands) = _sut.Extract("!!sound(u=https://mud.example/snd t=combat c=1 p=70 l=3 v=25 hit*.wav)");

        commands.Should().ContainSingle().Which.Should().BeEquivalentTo(new SoundCommand
        {
            Type = SoundType.Sound,
            FileName = "hit*.wav",
            Volume = 25,
            Loop = 3,
            Priority = 70,
            Continue = true,
            SoundCategory = "combat",
            Url = "https://mud.example/snd"
        });
    }

    [Fact]
    public void Extract_ExtraSpacesAndTabs_AreTolerated()
    {
        var (_, commands) = _sut.Extract("!!SOUND(  a.wav \t V=10   L=2 )");

        commands[0].Should().BeEquivalentTo(new { FileName = "a.wav", Volume = 10, Loop = 2 });
    }

    [Fact]
    public void Extract_UnparsableNumbers_KeepTheDefaults()
    {
        var (_, commands) = _sut.Extract("!!SOUND(a.wav V=loud L= P=1.5)");

        commands[0].Should().BeEquivalentTo(new { Volume = 100, Loop = 1, Priority = 50 });
    }

    [Fact]
    public void Extract_OffWithUrl_KeepsTheDefaultUrl()
    {
        var (_, commands) = _sut.Extract("!!SOUND(Off U=https://mud.example/sounds)");

        commands[0].IsStop.Should().BeTrue();
        commands[0].Url.Should().Be("https://mud.example/sounds");
    }

    [Fact]
    public void Extract_OnlyParameters_IsNotACommandButTheLineIsStillRemoved()
    {
        var (clean, commands) = _sut.Extract("!!SOUND(V=50)");

        clean.Should().BeEmpty();
        commands.Should().BeEmpty();
    }

    [Theory]
    [InlineData("!!SOUND(a.wav)", true)]
    [InlineData("!!music(off)", true)]
    [InlineData("!!SOUND(a.wav) and text", true)]
    [InlineData("text !!SOUND(a.wav)", false)]
    [InlineData("!!SOUND a.wav", false)]
    [InlineData("!!", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsMspLine_OnlyAtLineStart(string? line, bool expected)
    {
        MspExtractor.IsMspLine(line).Should().Be(expected);
    }
}
