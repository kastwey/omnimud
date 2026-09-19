using System.Windows.Forms.Automation;
using NSubstitute;
using Omnimud.Core.Accessibility;
using Omnimud.Core.Accessibility.Readers;
using Omnimud.Core.Options;
using Omnimud.Core.Session;
using Omnimud.UI.Services.Accessibility;

namespace Omnimud.UI.Tests.Accessibility;

public sealed class AnnouncerTests
{
    private readonly IUiaNotifier _uia = Substitute.For<IUiaNotifier>();
    private readonly IScreenReader _detected = Reader("Detected");
    private readonly IScreenReader _jaws = Reader("JAWS");
    private readonly IScreenReader _nvda = Reader("NVDA");

    private static IScreenReader Reader(string name)
    {
        var reader = Substitute.For<IScreenReader>();
        reader.Name.Returns(name);
        reader.IsRunning.Returns(true);
        reader.Speak(Arg.Any<string>(), Arg.Any<bool>()).Returns(true);
        reader.StopSpeech().Returns(true);
        return reader;
    }

    private Announcer Create(ScreenReaderMode mode = ScreenReaderMode.Automatic) =>
        new(_uia, new ScreenReaderApi([_detected]), new ScreenReaderApi([_jaws]), new ScreenReaderApi([_nvda])) { Mode = mode };

    [Fact]
    public void Automatic_UsesUiAutomationFirst_AndDoesNotDoubleSpeak()
    {
        _uia.Raise(Arg.Any<string>(), Arg.Any<AutomationNotificationProcessing>()).Returns(true);

        Create().Announce("hola", AnnouncePriority.Queue).Should().BeTrue();

        _uia.Received(1).Raise("hola", AutomationNotificationProcessing.All);
        _detected.DidNotReceive().Speak(Arg.Any<string>(), Arg.Any<bool>());
    }

    [Fact]
    public void Automatic_FallsBackToNativeReader_WhenUiAutomationFails()
    {
        _uia.Raise(Arg.Any<string>(), Arg.Any<AutomationNotificationProcessing>()).Returns(false);

        Create().Announce("hola", AnnouncePriority.Interrupt).Should().BeTrue();

        _detected.Received(1).Speak("hola", true);
    }

    [Theory]
    [InlineData(AnnouncePriority.Queue, AutomationNotificationProcessing.All)]
    [InlineData(AnnouncePriority.MostRecent, AutomationNotificationProcessing.MostRecent)]
    [InlineData(AnnouncePriority.Interrupt, AutomationNotificationProcessing.ImportantMostRecent)]
    public void Priority_MapsToUiaProcessing(AnnouncePriority priority, AutomationNotificationProcessing expected)
    {
        _uia.Raise(Arg.Any<string>(), Arg.Any<AutomationNotificationProcessing>()).Returns(true);

        Create().Announce("texto", priority);

        _uia.Received(1).Raise("texto", expected);
    }

    [Fact]
    public void JawsMode_SpeaksThroughJawsOnly_QueuedTextDoesNotInterrupt()
    {
        Create(ScreenReaderMode.Jaws).Announce("texto", AnnouncePriority.Queue);

        _jaws.Received(1).Speak("texto", false);
        _uia.DidNotReceive().Raise(Arg.Any<string>(), Arg.Any<AutomationNotificationProcessing>());
        _nvda.DidNotReceive().Speak(Arg.Any<string>(), Arg.Any<bool>());
    }

    [Fact]
    public void NvdaMode_InterruptPriority_Interrupts()
    {
        Create(ScreenReaderMode.Nvda).Announce("1: mensaje", AnnouncePriority.Interrupt);

        _nvda.Received(1).Speak("1: mensaje", true);
    }

    [Fact]
    public void NoneMode_AnnouncesNothing()
    {
        Create(ScreenReaderMode.None).Announce("texto", AnnouncePriority.Interrupt).Should().BeFalse();

        _uia.DidNotReceiveWithAnyArgs().Raise(default!, default);
        _detected.DidNotReceiveWithAnyArgs().Speak(default!, default);
    }

    [Fact]
    public void Muted_AnnouncesNothing()
    {
        var announcer = Create();
        announcer.Muted = true;

        announcer.Announce("texto", AnnouncePriority.Interrupt).Should().BeFalse();

        _uia.DidNotReceiveWithAnyArgs().Raise(default!, default);
    }

    [Theory]
    [InlineData("[1;31mRojo[0m normal", "Rojo normal")]
    [InlineData("[38;5;208mnaranja[m", "naranja")]
    [InlineData("[2Jpantalla", "pantalla")]
    [InlineData("concampana", "con campana")]
    [InlineData("  espacios  ", "espacios")]
    public void ScreenReader_NeverReceivesEscapeSequencesOrControlCharacters(string input, string expected)
    {
        _uia.Raise(Arg.Any<string>(), Arg.Any<AutomationNotificationProcessing>()).Returns(true);

        Create().Announce(input, AnnouncePriority.Queue);

        _uia.Received(1).Raise(expected, Arg.Any<AutomationNotificationProcessing>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[0m")]
    public void EmptyText_IsNotAnnounced(string input)
    {
        Create().Announce(input, AnnouncePriority.Queue).Should().BeFalse();

        _uia.DidNotReceiveWithAnyArgs().Raise(default!, default);
    }

    [Fact]
    public void ControlUiaNotifier_RaisesFromARealControl_WithoutThrowing() => Sta.Run(() =>
    {
        using var box = new TextBox();
        box.CreateControl();
        var notifier = new ControlUiaNotifier(box);

        // The return value depends on the Windows version; the call itself must be safe.
        var act = () => notifier.Raise("prueba", AutomationNotificationProcessing.All);
        act.Should().NotThrow();
    });

    [Fact]
    public void ControlUiaNotifier_DisposedControl_ReturnsFalse() => Sta.Run(() =>
    {
        var box = new TextBox();
        box.CreateControl();
        var notifier = new ControlUiaNotifier(box);
        box.Dispose();

        notifier.Raise("prueba", AutomationNotificationProcessing.All).Should().BeFalse();
    });
}
