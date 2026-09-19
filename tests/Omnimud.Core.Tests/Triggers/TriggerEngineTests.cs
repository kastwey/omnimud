using FluentAssertions;
using Omnimud.Core.Triggers;

namespace Omnimud.Core.Tests.Triggers;

public class TriggerEngineTests
{
    private readonly TriggerMatcher _matcher = new();
    private readonly TriggerEngine _sut;

    public TriggerEngineTests()
    {
        _sut = new TriggerEngine(_matcher);
    }

    [Fact]
    public void Process_EmptyText_ReturnsNoMatches()
    {
        _sut.LoadTriggers([MakeTrigger("test")]);

        _sut.Process("").Should().BeEmpty();
    }

    [Fact]
    public void Process_NoTriggers_ReturnsEmpty()
    {
        _sut.LoadTriggers([]);

        _sut.Process("some text").Should().BeEmpty();
    }

    [Fact]
    public void Process_MatchingTrigger_ReturnsMatch()
    {
        _sut.LoadTriggers([MakeTrigger("monster")]);

        var result = _sut.Process("A monster appears!");

        result.Should().HaveCount(1);
        result[0].Trigger.Name.Should().Be("Test");
    }

    [Fact]
    public void Process_DisabledTrigger_IsSkipped()
    {
        var trigger = MakeTrigger("monster");
        trigger.Enabled = false;
        _sut.LoadTriggers([trigger]);

        _sut.Process("A monster appears!").Should().BeEmpty();
    }

    [Fact]
    public void DisableAll_SkipsAllTriggers()
    {
        _sut.LoadTriggers([MakeTrigger("monster")]);
        _sut.DisableAll();

        _sut.Process("A monster appears!").Should().BeEmpty();
    }

    [Fact]
    public void EnableAll_ReenablesProcessing()
    {
        _sut.LoadTriggers([MakeTrigger("monster")]);
        _sut.DisableAll();
        _sut.EnableAll();

        _sut.Process("A monster appears!").Should().HaveCount(1);
    }

    [Fact]
    public void DisableTrigger_DisablesById()
    {
        var trigger = MakeTrigger("monster");
        _sut.LoadTriggers([trigger]);
        _sut.DisableTrigger(trigger.Id);

        _sut.Process("A monster appears!").Should().BeEmpty();
    }

    [Fact]
    public void EnableTrigger_ReenablesById()
    {
        var trigger = MakeTrigger("monster");
        trigger.Enabled = false;
        _sut.LoadTriggers([trigger]);
        _sut.EnableTrigger(trigger.Id);

        _sut.Process("A monster appears!").Should().HaveCount(1);
    }

    [Fact]
    public void Process_MultipleMatches_OrderedByPriority()
    {
        var low = MakeTrigger("attack", priority: 10, name: "Low");
        var high = MakeTrigger("attack", priority: 90, name: "High");

        _sut.LoadTriggers([low, high]);

        var result = _sut.Process("You attack");
        result.Should().HaveCount(2);
        result[0].Trigger.Name.Should().Be("High");
        result[1].Trigger.Name.Should().Be("Low");
    }

    private static TriggerDefinition MakeTrigger(string pattern, int priority = 50, string name = "Test")
    {
        return new TriggerDefinition
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Pattern = pattern,
            PatternType = PatternType.Literal,
            Action = "action",
            Priority = priority
        };
    }
}
