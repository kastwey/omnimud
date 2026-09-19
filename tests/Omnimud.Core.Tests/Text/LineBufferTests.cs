using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Omnimud.Core.Text;

namespace Omnimud.Core.Tests.Text;

public class LineBufferTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly List<long> _timeouts = [];

    private LineBuffer Create(int delayMs = 150)
    {
        var sut = new LineBuffer(_time, () => TimeSpan.FromMilliseconds(delayMs));
        sut.PromptTimeout += _timeouts.Add;
        return sut;
    }

    [Fact]
    public void Append_CompleteLines_AreReturnedWithoutNewline()
    {
        using var sut = Create();

        sut.Append("uno\ndos\n").Should().Equal("uno", "dos");
        sut.HasPending.Should().BeFalse();
    }

    [Fact]
    public void Append_LineSplitAcrossCalls_IsJoined()
    {
        using var sut = Create();

        sut.Append("Un orco te ").Should().BeEmpty();
        sut.Append("ataca.\nOtra").Should().Equal("Un orco te ataca.");
        sut.HasPending.Should().BeTrue();
    }

    [Fact]
    public void Append_EmptyLines_AreKept()
    {
        using var sut = Create();

        sut.Append("a\n\n\nb\n").Should().Equal("a", "", "", "b");
    }

    [Fact]
    public void Append_CarriageReturns_AreDropped()
    {
        using var sut = Create();

        sut.Append("a\r\nb\n\r").Should().Equal("a", "b");
        sut.HasPending.Should().BeFalse();
    }

    [Fact]
    public void Append_Backspace_DeletesPreviousCharacterEvenAcrossCalls()
    {
        using var sut = Create();

        sut.Append("holx");
        sut.Append("\ba\b\b\bXYZ\n").Should().Equal("hXYZ");
    }

    [Fact]
    public void Append_BackspaceAtStartOfLine_IsIgnored()
    {
        using var sut = Create();

        sut.Append("a\n\b\bb\n").Should().Equal("a", "b");
    }

    [Fact]
    public void PendingText_AfterDelayWithoutData_RaisesTimeoutAndCanBeTaken()
    {
        using var sut = Create();
        sut.Append("Vida: 50> ");

        _time.Advance(TimeSpan.FromMilliseconds(149));
        _timeouts.Should().BeEmpty();
        _time.Advance(TimeSpan.FromMilliseconds(1));

        _timeouts.Should().ContainSingle();
        sut.TakePendingIf(_timeouts[0]).Should().Be("Vida: 50> ");
        sut.HasPending.Should().BeFalse();
    }

    [Fact]
    public void PendingText_NewDataBeforeDelay_RestartsTheWait()
    {
        using var sut = Create();
        sut.Append("par");
        _time.Advance(TimeSpan.FromMilliseconds(100));
        sut.Append("cial");
        _time.Advance(TimeSpan.FromMilliseconds(100));

        _timeouts.Should().BeEmpty();

        _time.Advance(TimeSpan.FromMilliseconds(50));
        _timeouts.Should().ContainSingle();
        sut.TakePendingIf(_timeouts[0]).Should().Be("parcial");
    }

    [Fact]
    public void TakePendingIf_StaleToken_TakesNothing()
    {
        using var sut = Create();
        sut.Append("abc");
        _time.Advance(TimeSpan.FromMilliseconds(150));
        var token = _timeouts.Single();

        sut.Append("def\n").Should().Equal("abcdef");

        sut.TakePendingIf(token).Should().BeNull();
    }

    [Fact]
    public void CompleteLine_CancelsTheTimer()
    {
        using var sut = Create();
        sut.Append("abc");
        sut.Append("\n");

        _time.Advance(TimeSpan.FromSeconds(5));

        _timeouts.Should().BeEmpty();
    }

    [Fact]
    public void TakePending_ReturnsTextOnce()
    {
        using var sut = Create();
        sut.Append("> ");

        sut.TakePending().Should().Be("> ");
        sut.TakePending().Should().BeNull();
        _time.Advance(TimeSpan.FromSeconds(1));
        _timeouts.Should().BeEmpty();
    }

    [Fact]
    public void Reset_DropsPendingText()
    {
        using var sut = Create();
        sut.Append("resto de la conexión anterior");

        sut.Reset();

        sut.HasPending.Should().BeFalse();
        sut.Append("nueva\n").Should().Equal("nueva");
    }

    [Fact]
    public void DelayIsReadEachTime_SoOptionChangesApply()
    {
        var delay = 150;
        using var sut = new LineBuffer(_time, () => TimeSpan.FromMilliseconds(delay));
        sut.PromptTimeout += _timeouts.Add;

        delay = 1000;
        sut.Append("x");
        _time.Advance(TimeSpan.FromMilliseconds(500));
        _timeouts.Should().BeEmpty();
        _time.Advance(TimeSpan.FromMilliseconds(500));
        _timeouts.Should().ContainSingle();
    }

    [Fact]
    public void Dispose_StopsTimeouts()
    {
        var sut = Create();
        sut.Append("x");

        sut.Dispose();
        _time.Advance(TimeSpan.FromSeconds(1));

        _timeouts.Should().BeEmpty();
    }
}
