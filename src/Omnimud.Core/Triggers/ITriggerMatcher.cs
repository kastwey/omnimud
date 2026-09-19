namespace Omnimud.Core.Triggers;

public interface ITriggerMatcher
{
    bool TryMatch(string text, TriggerDefinition trigger, out TriggerMatch? match);
}
