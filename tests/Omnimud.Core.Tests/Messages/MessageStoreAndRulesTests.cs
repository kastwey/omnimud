using FluentAssertions;
using Omnimud.Core.Messages;
using Omnimud.Core.Session;

namespace Omnimud.Core.Tests.Messages;

public class MessageStoreAndRulesTests
{
    private static readonly DateTime When = new(2026, 3, 4, 12, 0, 0);

    [Fact]
    public void Add_ReturnsMessageNumberedOne_WithItsTime()
    {
        var sut = new MessageStore();

        var message = sut.Add(When, "hola", "chat", "Ana");

        message.Should().Be(new SessionMessage(1, When, "hola", "chat", "Ana"));
    }

    [Fact]
    public void Snapshot_IsOldestFirst_AndNumberOneIsTheMostRecent()
    {
        var sut = new MessageStore();
        sut.Add(When, "primero");
        sut.Add(When.AddSeconds(1), "segundo");
        sut.Add(When.AddSeconds(2), "tercero");

        var snapshot = sut.Snapshot();

        snapshot.Select(m => m.Text).Should().Equal("primero", "segundo", "tercero");
        snapshot.Select(m => m.Number).Should().Equal(3, 2, 1);
        sut.GetByNumber(1)!.Text.Should().Be("tercero");
        sut.GetByNumber(3)!.Text.Should().Be("primero");
        sut.GetByNumber(4).Should().BeNull();
        sut.GetByNumber(0).Should().BeNull();
    }

    [Fact]
    public void Add_BeyondCapacity_DropsTheOldest()
    {
        var sut = new MessageStore();
        for (var i = 1; i <= 1005; i++)
            sut.Add(When, $"m{i}");

        sut.Count.Should().Be(1000);
        sut.Snapshot()[0].Text.Should().Be("m6");
        sut.GetByNumber(1)!.Text.Should().Be("m1005");
        sut.GetByNumber(1000)!.Text.Should().Be("m6");
    }

    [Fact]
    public void Rules_FirstMatchWins_AndTemplateIsExpanded()
    {
        var sut = new MessageRuleSet(
        [
            new MessageRule(@"^(\w+) te dice: '(.*)'$", "$1: $2", Channel: "decir"),
            new MessageRule(@"te dice", "no debería llegar")
        ]);

        var result = sut.Match("Ana te dice: 'hola'");

        result.Should().Be(new MessageRuleResult("Ana: hola", "decir", null));
    }

    [Fact]
    public void Rules_NamedGroupsAndWholeMatch_AreSupported()
    {
        var sut = new MessageRuleSet([new MessageRule(@"\[(?<canal>\w+)\] (?<sender>\w+): (?<texto>.+)", "${canal}> ${sender} dijo ${texto} ($0)")]);

        var result = sut.Match("[chat] Bob: buenas")!;

        result.Text.Should().Be("chat> Bob dijo buenas ([chat] Bob: buenas)");
        result.Sender.Should().Be("Bob");
    }

    [Fact]
    public void Rules_EmptyTemplate_UsesTheMatchedText()
    {
        var sut = new MessageRuleSet([new MessageRule(@"\w+ grita: .+", "")]);

        sut.Match("Ana grita: ¡eh!")!.Text.Should().Be("Ana grita: ¡eh!");
    }

    [Fact]
    public void Rules_CaseSensitivityIsPerRule()
    {
        var sut = new MessageRuleSet([new MessageRule("TE DICE", "$0", CaseSensitive: true)]);
        var relaxed = new MessageRuleSet([new MessageRule("TE DICE", "$0")]);

        sut.Match("Ana te dice algo").Should().BeNull();
        relaxed.Match("Ana te dice algo").Should().NotBeNull();
    }

    [Fact]
    public void Rules_InvalidPattern_IsSkippedWithoutThrowing()
    {
        var sut = new MessageRuleSet([new MessageRule("(roto", "x"), new MessageRule("hola", "ok")]);

        sut.Count.Should().Be(1);
        sut.Match("hola")!.Text.Should().Be("ok");
    }

    [Fact]
    public void Rules_CatastrophicPattern_TimesOutWithoutThrowing_AndLaterRulesStillApply()
    {
        // The pattern backtracks exponentially on this input; the match timeout must cut it short.
        var sut = new MessageRuleSet([new MessageRule(@"^(?<sender>(a+)+)b (.*)$", "x"), new MessageRule("a+!$", "ok")]);
        MessageRuleResult? result = null;

        var act = () => result = sut.Match("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa!");

        act.Should().NotThrow();
        result!.Text.Should().Be("ok");
    }

    [Fact]
    public void Rules_NoRulesOrNoMatch_ReturnsNull()
    {
        new MessageRuleSet([]).Match("algo").Should().BeNull();
        new MessageRuleSet([new MessageRule("x", "y")]).Match("").Should().BeNull();
    }
}
