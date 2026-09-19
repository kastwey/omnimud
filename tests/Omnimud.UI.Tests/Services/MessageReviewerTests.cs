using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using Omnimud.Core.Session;
using Omnimud.UI.Services;

namespace Omnimud.UI.Tests.Services;

public sealed class MessageReviewerTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly MessageReviewer _sut;

    public MessageReviewerTests()
    {
        Omnimud.UI.Resources.Strings.Culture = CultureInfo.GetCultureInfo("es");
        _sut = new MessageReviewer(_time);
    }

    /// <summary>Oldest first, as the session stores them: "mensaje 1" is the oldest.</summary>
    private static List<SessionMessage> Messages(int count) =>
        Enumerable.Range(1, count).Select(i => new SessionMessage(i, DateTime.Now, $"mensaje {i}")).ToList();

    [Fact]
    public void Digit1_ReadsTheMostRecentMessage()
    {
        _sut.PressDigit(1, Messages(5)).Should().Be("1: mensaje 5");
    }

    [Fact]
    public void Digit3_ReadsTheThirdMostRecent()
    {
        _sut.PressDigit(3, Messages(5)).Should().Be("3: mensaje 3");
    }

    [Fact]
    public void Digit0_MeansTheTenth()
    {
        _sut.PressDigit(0, Messages(12)).Should().Be("10: mensaje 3");
    }

    [Fact]
    public void TwoDigitsWithin400ms_AreConcatenated()
    {
        var messages = Messages(20);
        _sut.PressDigit(1, messages);
        _time.Advance(TimeSpan.FromMilliseconds(300));

        _sut.PressDigit(5, messages).Should().Be("15: mensaje 6");
    }

    [Fact]
    public void ZeroAsSecondDigit_IsAZero_NotTen()
    {
        var messages = Messages(25);
        _sut.PressDigit(2, messages);
        _time.Advance(TimeSpan.FromMilliseconds(100));

        _sut.PressDigit(0, messages).Should().Be("20: mensaje 6");
    }

    [Fact]
    public void DigitsMoreThan400msApart_AreIndependent()
    {
        var messages = Messages(20);
        _sut.PressDigit(1, messages);
        _time.Advance(TimeSpan.FromMilliseconds(450));

        _sut.PressDigit(5, messages).Should().Be("5: mensaje 16");
    }

    [Fact]
    public void FourthDigit_IsRejected_AndSequenceRestarts()
    {
        var messages = Messages(1000);
        _sut.PressDigit(1, messages);
        _sut.PressDigit(2, messages);
        _sut.PressDigit(3, messages).Should().Be("123: mensaje 878");

        _sut.PressDigit(4, messages).Should().BeNull();
        _sut.PressDigit(4, messages).Should().Be("4: mensaje 997");
    }

    [Fact]
    public void Cancel_StopsConcatenation()
    {
        var messages = Messages(20);
        _sut.PressDigit(1, messages);
        _sut.Cancel();

        _sut.PressDigit(5, messages).Should().Be("5: mensaje 16");
    }

    [Fact]
    public void NoMessages_SaysSo()
    {
        _sut.PressDigit(1, []).Should().Be("No hay mensajes.");
        _sut.PressSequential([]).Should().Be("No hay mensajes.");
    }

    [Fact]
    public void NumberBeyondCount_SaysHowManyThereAre()
    {
        _sut.PressDigit(7, Messages(3)).Should().Be("Solo hay 3 mensajes.");
        _sut.PressDigit(7, Messages(1)).Should().Be("Solo hay un mensaje.");
    }

    [Fact]
    public void Sequential_WalksBackwards_WhilePressedQuickly()
    {
        var messages = Messages(5);
        _sut.PressSequential(messages).Should().Be("1: mensaje 5");
        _time.Advance(TimeSpan.FromMilliseconds(500));
        _sut.PressSequential(messages).Should().Be("2: mensaje 4");
        _time.Advance(TimeSpan.FromMilliseconds(700));
        _sut.PressSequential(messages).Should().Be("3: mensaje 3");
    }

    [Fact]
    public void Sequential_AfterAPause_StartsAgainFromTheMostRecent()
    {
        var messages = Messages(5);
        _sut.PressSequential(messages);
        _sut.PressSequential(messages);
        _time.Advance(TimeSpan.FromMilliseconds(900));

        _sut.PressSequential(messages).Should().Be("1: mensaje 5");
    }

    [Fact]
    public void Sequential_PastTheOldest_WarnsAndStaysThere()
    {
        var messages = Messages(2);
        _sut.PressSequential(messages);
        _sut.PressSequential(messages);

        _sut.PressSequential(messages).Should().Be("Solo hay 2 mensajes.");
        _sut.PressSequential(messages).Should().Be("Solo hay 2 mensajes.");
    }

    [Fact]
    public void Sequential_DoesNotConcatenateWithDigits()
    {
        var messages = Messages(20);
        _sut.PressDigit(1, messages);
        _sut.PressSequential(messages);

        _sut.PressDigit(5, messages).Should().Be("5: mensaje 16");
    }
}
