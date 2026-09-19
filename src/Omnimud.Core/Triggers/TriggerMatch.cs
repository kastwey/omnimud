namespace Omnimud.Core.Triggers;

public sealed class TriggerMatch
{
    public required TriggerDefinition Trigger { get; init; }
    public required IReadOnlyList<string> Captures { get; init; }
    public required string MatchedText { get; init; }
}
