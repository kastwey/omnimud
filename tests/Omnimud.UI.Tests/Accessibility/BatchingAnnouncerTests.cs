using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Omnimud.Core.Session;
using Omnimud.UI.Services.Accessibility;

namespace Omnimud.UI.Tests.Accessibility;

/// <summary>
/// Regression: with one notification per line the screen reader dropped about every other line
/// of a multi-line screen (reported while creating a character on reduction-mud.org).
/// </summary>
public sealed class BatchingAnnouncerTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly IAnnouncer _inner = Substitute.For<IAnnouncer>();
    private readonly BatchingAnnouncer _sut;

    public BatchingAnnouncerTests()
    {
        _inner.Announce(Arg.Any<string>(), Arg.Any<AnnouncePriority>()).Returns(true);
        _sut = new BatchingAnnouncer(_inner, _time);
    }

    private static readonly string[] FactionScreen =
    [
        "[Paso 3 de 8 — Facción]  (atrás | cancelar)",
        "¿A qué facción te acercas?",
        "  1) Los Insumisos — gente de acción contra la IA.",
        "     No se someten.",
        "  2) Los Juncos — pragmáticos que se doblan con",
        "     el viento sin partirse. Sobreviven.",
        "  3) Sin facción — todavía no me alineo con nadie.",
        "  Escribe 1, 2 o 3.  'ayuda insumisos' o 'ayuda juncos'",
        "  para saber más de cada facción.",
        ">",
    ];

    [Fact]
    public void AScreenOfLines_IsSpokenAsOneAnnouncement_WithEveryLineInOrder()
    {
        foreach (var line in FactionScreen)
            _sut.Announce(line, AnnouncePriority.Queue);

        _inner.DidNotReceiveWithAnyArgs().Announce(default!, default);

        _time.Advance(BatchingAnnouncer.DefaultQuietTime);

        _inner.Received(1).Announce(string.Join("\n", FactionScreen), AnnouncePriority.Queue);
    }

    [Fact]
    public void LinesArrivingWithSmallGaps_StillFormOneAnnouncement()
    {
        foreach (var line in FactionScreen)
        {
            _sut.Announce(line, AnnouncePriority.Queue);
            _time.Advance(TimeSpan.FromMilliseconds(10));
        }
        _time.Advance(BatchingAnnouncer.DefaultQuietTime);

        _inner.Received(1).Announce(Arg.Any<string>(), AnnouncePriority.Queue);
    }

    [Fact]
    public void ASeparateLaterLine_IsASeparateAnnouncement()
    {
        _sut.Announce("primera", AnnouncePriority.Queue);
        _time.Advance(TimeSpan.FromMilliseconds(200));
        _sut.Announce("segunda", AnnouncePriority.Queue);
        _time.Advance(TimeSpan.FromMilliseconds(200));

        Received.InOrder(() =>
        {
            _inner.Announce("primera", AnnouncePriority.Queue);
            _inner.Announce("segunda", AnnouncePriority.Queue);
        });
    }

    [Fact]
    public void ContinuousText_IsNeverHeldLongerThanTheMaximumDelay()
    {
        // A line every 40 ms never leaves a 60 ms quiet gap.
        for (var i = 0; i < 20; i++)
        {
            _sut.Announce($"linea {i}", AnnouncePriority.Queue);
            _time.Advance(TimeSpan.FromMilliseconds(40));
        }

        _inner.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IAnnouncer.Announce))
            .Should().BeGreaterThanOrEqualTo(2, "speech must start while text keeps arriving");
    }

    [Fact]
    public void UrgentAnnouncement_FirstFlushesPendingText_SoOrderIsPreserved()
    {
        _sut.Announce("Un orco llega.", AnnouncePriority.Queue);
        _sut.Announce("1: Ana te dice: hola", AnnouncePriority.Interrupt);

        Received.InOrder(() =>
        {
            _inner.Announce("Un orco llega.", AnnouncePriority.Queue);
            _inner.Announce("1: Ana te dice: hola", AnnouncePriority.Interrupt);
        });
    }

    [Fact]
    public void BlankLines_AreNotAnnounced()
    {
        _sut.Announce("", AnnouncePriority.Queue);
        _sut.Announce("   ", AnnouncePriority.Queue);
        _time.Advance(TimeSpan.FromSeconds(1));

        _inner.DidNotReceiveWithAnyArgs().Announce(default!, default);
    }

    [Fact]
    public void StopSpeech_DropsPendingText()
    {
        _sut.Announce("texto largo", AnnouncePriority.Queue);
        _sut.StopSpeech();
        _time.Advance(TimeSpan.FromSeconds(1));

        _inner.DidNotReceiveWithAnyArgs().Announce(default!, default);
        _inner.Received(1).StopSpeech();
    }

    [Fact]
    public void Muting_DropsPendingText()
    {
        _sut.Announce("texto", AnnouncePriority.Queue);
        _sut.Muted = true;
        _time.Advance(TimeSpan.FromSeconds(1));

        _inner.DidNotReceiveWithAnyArgs().Announce(default!, default);
        _inner.Received().Muted = true;
    }

    [Fact]
    public void Dispose_StopsEverything()
    {
        _sut.Announce("texto", AnnouncePriority.Queue);
        _sut.Dispose();
        _time.Advance(TimeSpan.FromSeconds(1));

        _inner.DidNotReceiveWithAnyArgs().Announce(default!, default);
    }

    [Fact]
    public void TheJoinedText_KeepsLineBreaks_ThroughTheRealAnnouncerCleaning()
    {
        // Line breaks are what makes the screen reader pause between lines.
        Announcer.Clean("uno\ndos").Should().Be("uno\ndos");
    }
}
