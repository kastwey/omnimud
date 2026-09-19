using FluentAssertions;
using NSubstitute;
using Omnimud.Core.Accessibility;
using Omnimud.Core.Accessibility.Readers;
using Xunit;

namespace Omnimud.Core.Tests.Accessibility;

public class ScreenReaderApiTests
{
    [Fact]
    public void IsAvailable_NoReadersRunning_ReturnsFalse()
    {
        var jaws = Substitute.For<IScreenReader>();
        jaws.IsRunning.Returns(false);
        var nvda = Substitute.For<IScreenReader>();
        nvda.IsRunning.Returns(false);

        var api = new ScreenReaderApi([jaws, nvda]);

        api.IsAvailable.Should().BeFalse();
        api.Active.Should().BeNull();
        api.ActiveName.Should().Be("None");
    }

    [Fact]
    public void Active_PicksFirstRunningReader()
    {
        var jaws = Substitute.For<IScreenReader>();
        jaws.Name.Returns("JAWS");
        jaws.IsRunning.Returns(true);
        var nvda = Substitute.For<IScreenReader>();
        nvda.Name.Returns("NVDA");
        nvda.IsRunning.Returns(true);

        var api = new ScreenReaderApi([jaws, nvda]);

        api.Active.Should().Be(jaws);
        api.ActiveName.Should().Be("JAWS");
    }

    [Fact]
    public void Active_SkipsReadersThatAreNotRunning()
    {
        var jaws = Substitute.For<IScreenReader>();
        jaws.Name.Returns("JAWS");
        jaws.IsRunning.Returns(false);
        var nvda = Substitute.For<IScreenReader>();
        nvda.Name.Returns("NVDA");
        nvda.IsRunning.Returns(true);

        var api = new ScreenReaderApi([jaws, nvda]);

        api.Active.Should().Be(nvda);
        api.ActiveName.Should().Be("NVDA");
    }

    [Fact]
    public void Speak_NoActiveReader_ReturnsFalse()
    {
        var api = new ScreenReaderApi([]);
        api.Speak("hello").Should().BeFalse();
    }

    [Fact]
    public void Speak_DelegatesToActiveReader_WithInterrupt()
    {
        var reader = Substitute.For<IScreenReader>();
        reader.IsRunning.Returns(true);
        reader.Speak("hello", true).Returns(true);

        var api = new ScreenReaderApi([reader]);

        api.Speak("hello", interrupt: true).Should().BeTrue();
        reader.Received(1).Speak("hello", true);
    }

    [Fact]
    public void Speak_DelegatesToActiveReader_Queued()
    {
        var reader = Substitute.For<IScreenReader>();
        reader.IsRunning.Returns(true);
        reader.Speak("hello", false).Returns(true);

        var api = new ScreenReaderApi([reader]);

        api.Speak("hello", interrupt: false).Should().BeTrue();
        reader.Received(1).Speak("hello", false);
    }

    [Fact]
    public void Speak_WhenMuted_DoesNotCallReader()
    {
        var reader = Substitute.For<IScreenReader>();
        reader.IsRunning.Returns(true);
        var api = new ScreenReaderApi([reader]) { Muted = true };

        api.Speak("hello").Should().BeFalse();
        reader.DidNotReceive().Speak(Arg.Any<string>(), Arg.Any<bool>());
    }

    [Fact]
    public void Speak_IgnoresEmptyOrWhitespace()
    {
        var reader = Substitute.For<IScreenReader>();
        reader.IsRunning.Returns(true);
        var api = new ScreenReaderApi([reader]);

        api.Speak("").Should().BeFalse();
        api.Speak("   ").Should().BeFalse();
        reader.DidNotReceive().Speak(Arg.Any<string>(), Arg.Any<bool>());
    }

    [Fact]
    public void StopSpeech_DelegatesToActiveReader()
    {
        var reader = Substitute.For<IScreenReader>();
        reader.IsRunning.Returns(true);
        reader.StopSpeech().Returns(true);

        var api = new ScreenReaderApi([reader]);

        api.StopSpeech().Should().BeTrue();
        reader.Received(1).StopSpeech();
    }

    [Fact]
    public void Braille_DelegatesToActiveReader()
    {
        var reader = Substitute.For<IScreenReader>();
        reader.IsRunning.Returns(true);
        reader.Braille("dot").Returns(true);

        var api = new ScreenReaderApi([reader]);

        api.Braille("dot").Should().BeTrue();
        reader.Received(1).Braille("dot");
    }

    [Fact]
    public void Refresh_PicksUpNewlyRunningReader()
    {
        var jaws = Substitute.For<IScreenReader>();
        jaws.Name.Returns("JAWS");
        jaws.IsRunning.Returns(false, true);

        var api = new ScreenReaderApi([jaws]);
        api.Active.Should().BeNull();

        api.Refresh();
        api.Active.Should().Be(jaws);
    }
}
