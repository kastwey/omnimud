namespace Omnimud.Core.Triggers;

/// <summary>
/// The triggers of one session. Evaluation order is priority (higher first) and then load
/// order; every trigger that matches is returned, none stops the others.
/// Not thread-safe: the session serializes access.
/// </summary>
public sealed class TriggerEngine : ITriggerEngine
{
    private readonly ITriggerMatcher _matcher;
    private readonly List<TriggerDefinition> _triggers = [];
    private bool _globalEnabled = true;

    public TriggerEngine(ITriggerMatcher matcher)
    {
        _matcher = matcher;
    }

    /// <summary>False after "-triggers": nothing fires, command triggers included.</summary>
    public bool IsEnabled => _globalEnabled;

    /// <summary>Triggers in evaluation order.</summary>
    public IReadOnlyList<TriggerDefinition> Triggers => _triggers;

    /// <summary>Every enabled text trigger, whatever its mode. Kept for callers that do not
    /// distinguish between lines and blocks.</summary>
    public IReadOnlyList<TriggerMatch> Process(string text)
        => Evaluate(text, static t => !t.IsCommandTrigger);

    public IReadOnlyList<TriggerMatch> ProcessLine(string line)
        => Evaluate(line, static t => !t.IsCommandTrigger && !t.Multiline);

    public IReadOnlyList<TriggerMatch> ProcessBlock(string block)
        => Evaluate(block, static t => !t.IsCommandTrigger && t.Multiline);

    public IReadOnlyList<TriggerDefinition> MatchCommand(string firstWord)
    {
        if (!_globalEnabled || string.IsNullOrEmpty(firstWord))
            return [];

        List<TriggerDefinition>? result = null;
        foreach (var trigger in _triggers)
        {
            if (!trigger.Enabled || !trigger.IsCommandTrigger)
                continue;

            var comparison = trigger.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            if (trigger.Pattern.AsSpan(1).Equals(firstWord, comparison))
                (result ??= []).Add(trigger);
        }

        return (IReadOnlyList<TriggerDefinition>?)result ?? [];
    }

    public TriggerDefinition? FindByName(string name)
        => _triggers.Find(t => t.Name.Equals(name, StringComparison.Ordinal))
           ?? _triggers.Find(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public void LoadTriggers(IEnumerable<TriggerDefinition> triggers)
    {
        _triggers.Clear();
        // OrderBy is stable: equal priorities keep their load order.
        _triggers.AddRange(triggers.OrderByDescending(t => t.Priority));
    }

    public void EnableTrigger(string id)
    {
        var trigger = _triggers.Find(t => t.Id == id);
        if (trigger is not null)
            trigger.Enabled = true;
    }

    public void DisableTrigger(string id)
    {
        var trigger = _triggers.Find(t => t.Id == id);
        if (trigger is not null)
            trigger.Enabled = false;
    }

    public void EnableAll() => _globalEnabled = true;
    public void DisableAll() => _globalEnabled = false;

    private IReadOnlyList<TriggerMatch> Evaluate(string text, Func<TriggerDefinition, bool> filter)
    {
        if (!_globalEnabled || string.IsNullOrEmpty(text) || _triggers.Count == 0)
            return [];

        List<TriggerMatch>? matches = null;
        foreach (var trigger in _triggers)
        {
            if (!trigger.Enabled || !filter(trigger))
                continue;

            if (_matcher.TryMatch(text, trigger, out var match) && match is not null)
                (matches ??= []).Add(match);
        }

        return (IReadOnlyList<TriggerMatch>?)matches ?? [];
    }
}
