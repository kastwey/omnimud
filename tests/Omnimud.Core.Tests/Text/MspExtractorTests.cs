using FluentAssertions;
using Omnimud.Core.Text;

namespace Omnimud.Core.Tests.Text;

public class MspExtractorTests
{
    private readonly MspExtractor _sut = new();

    [Fact]
    public void Extract_NoCommands_ReturnsOriginalText()
    {
        var (clean, commands) = _sut.Extract("Just regular text");

        clean.Should().Be("Just regular text");
        commands.Should().BeEmpty();
    }

    [Fact]
    public void Extract_AnywhereMode_SoundCommandInsideTheLine_ExtractsAndRemoves()
    {
        var (clean, commands) = new MspExtractor(MspRecognition.Anywhere).Extract("You hear !!SOUND(thunder.wav) a loud boom");

        clean.Should().Be("You hear  a loud boom");
        commands.Should().HaveCount(1);
        commands[0].Type.Should().Be(SoundType.Sound);
        commands[0].FileName.Should().Be("thunder.wav");
    }

    [Fact]
    public void Extract_MusicCommand_ParsesCorrectly()
    {
        var (_, commands) = _sut.Extract("!!MUSIC(battle.mp3 V=80 L=-1 P=70)");

        commands.Should().HaveCount(1);
        commands[0].Type.Should().Be(SoundType.Music);
        commands[0].FileName.Should().Be("battle.mp3");
        commands[0].Volume.Should().Be(80);
        commands[0].Loop.Should().Be(-1);
        commands[0].Priority.Should().Be(70);
    }

    [Fact]
    public void Extract_AllParameters_ParsesCorrectly()
    {
        var (_, commands) = _sut.Extract("!!SOUND(rain.ogg V=50 L=3 P=30 C=true T=weather U=http://example.com/rain.ogg)");

        commands.Should().HaveCount(1);
        var cmd = commands[0];
        cmd.FileName.Should().Be("rain.ogg");
        cmd.Volume.Should().Be(50);
        cmd.Loop.Should().Be(3);
        cmd.Priority.Should().Be(30);
        cmd.Continue.Should().BeTrue();
        cmd.SoundCategory.Should().Be("weather");
        cmd.Url.Should().Be("http://example.com/rain.ogg");
    }

    [Fact]
    public void Extract_StopCommand_SetsIsStop()
    {
        var (_, commands) = _sut.Extract("!!SOUND(off)");

        commands.Should().HaveCount(1);
        commands[0].IsStop.Should().BeTrue();
        commands[0].FileName.Should().BeEmpty();
    }

    [Fact]
    public void Extract_MusicOff_SetsIsStop()
    {
        var (_, commands) = _sut.Extract("!!MUSIC(off)");

        commands.Should().HaveCount(1);
        commands[0].Type.Should().Be(SoundType.Music);
        commands[0].IsStop.Should().BeTrue();
    }

    [Fact]
    public void Extract_AnywhereMode_MultipleCommands_ExtractsAll()
    {
        var (clean, commands) = new MspExtractor(MspRecognition.Anywhere).Extract("!!SOUND(a.wav) text !!MUSIC(b.mp3)");

        clean.Should().Be(" text ");
        commands.Should().HaveCount(2);
        commands[0].FileName.Should().Be("a.wav");
        commands[1].FileName.Should().Be("b.mp3");
    }

    [Fact]
    public void Extract_VolumeClampedTo100()
    {
        var (_, commands) = _sut.Extract("!!SOUND(loud.wav V=200)");

        commands[0].Volume.Should().Be(100);
    }

    [Fact]
    public void Extract_VolumeClampedTo0()
    {
        var (_, commands) = _sut.Extract("!!SOUND(quiet.wav V=-50)");

        commands[0].Volume.Should().Be(0);
    }

    [Fact]
    public void Extract_EmptyArgs_ReturnsNoCommands()
    {
        var (_, commands) = _sut.Extract("!!SOUND()");

        commands.Should().BeEmpty();
    }

    [Fact]
    public void Extract_CaseInsensitive()
    {
        var (_, commands) = _sut.Extract("!!sound(test.wav)");

        commands.Should().HaveCount(1);
        commands[0].FileName.Should().Be("test.wav");
    }

    [Fact]
    public void Extract_NullOrEmpty_ReturnsAsIs()
    {
        _sut.Extract("").Commands.Should().BeEmpty();
        _sut.Extract(null!).Commands.Should().BeEmpty();
    }
}
