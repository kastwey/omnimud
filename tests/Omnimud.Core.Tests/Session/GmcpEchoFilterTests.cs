using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Omnimud.Core.Session;

namespace Omnimud.Core.Tests.Session;

public sealed class GmcpEchoFilterTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly GmcpEchoFilter _sut;

    public GmcpEchoFilterTests() => _sut = new GmcpEchoFilter(_time);

    [Fact]
    public void GmcpFirst_ThenTheSameTextAsALine_IsRecognisedAsACopy()
    {
        _sut.NoteGmcpMessage("hola a todos", "Ana").Should().BeFalse();

        _sut.IsEchoOfGmcpMessage("[Chat] Ana: hola a todos").Should().BeTrue();
    }

    [Fact]
    public void LineFirst_ThenTheSameTextByGmcp_IsReportedAsAlreadySpoken()
    {
        _sut.NoteSpokenLine("Ana te dice: nos vemos luego");

        _sut.NoteGmcpMessage("nos vemos luego", "Ana").Should().BeTrue();
    }

    [Fact]
    public void ACopy_IsConsumedOnce_SoARepeatedMessageIsHeardAgain()
    {
        _sut.NoteGmcpMessage("hola", "Ana");
        _sut.IsEchoOfGmcpMessage("Ana dice: hola").Should().BeTrue();

        _sut.IsEchoOfGmcpMessage("Ana dice: hola").Should().BeFalse();
    }

    [Fact]
    public void AnUnrelatedLine_IsNotSilenced_ByAShortChannelMessage()
    {
        _sut.NoteGmcpMessage("ok", "Ana");

        _sut.IsEchoOfGmcpMessage("El orco te golpea. No estás ok.").Should().BeFalse("the talker is not in that line");
    }

    [Fact]
    public void WithoutTalker_OnlyReasonablyLongTextsAreMatched()
    {
        _sut.NoteGmcpMessage("ok", null);
        _sut.IsEchoOfGmcpMessage("todo ok por aquí").Should().BeFalse();

        _sut.NoteGmcpMessage("el servidor se reiniciará en cinco minutos", null);
        _sut.IsEchoOfGmcpMessage("[Sistema] El servidor se reiniciará en cinco minutos").Should().BeTrue();
    }

    [Fact]
    public void Matching_IgnoresCase_AndExtraWhitespace()
    {
        _sut.NoteGmcpMessage("  Hola   a  todos ", "ANA");

        _sut.IsEchoOfGmcpMessage("ana dice: hola a todos").Should().BeTrue();
    }

    [Fact]
    public void AfterThreeSeconds_TheyAreDifferentEvents()
    {
        _sut.NoteGmcpMessage("hola a todos", "Ana");
        _time.Advance(TimeSpan.FromSeconds(4));

        _sut.IsEchoOfGmcpMessage("Ana dice: hola a todos").Should().BeFalse();
    }

    [Fact]
    public void Reset_ForgetsEverything()
    {
        _sut.NoteGmcpMessage("hola a todos", "Ana");
        _sut.Reset();

        _sut.IsEchoOfGmcpMessage("Ana dice: hola a todos").Should().BeFalse();
    }

    [Fact]
    public void EmptyText_NeverMatches()
    {
        _sut.NoteGmcpMessage("", "Ana").Should().BeFalse();
        _sut.IsEchoOfGmcpMessage("").Should().BeFalse();
    }
}
