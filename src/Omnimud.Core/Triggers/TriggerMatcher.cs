using System.Text.RegularExpressions;

namespace Omnimud.Core.Triggers;

public sealed class TriggerMatcher : ITriggerMatcher
{

    public bool TryMatch(string text, TriggerDefinition trigger, out TriggerMatch? match)
    {
        match = null;

        return trigger.PatternType switch
        {
            PatternType.Literal => TryMatchLiteral(text, trigger, out match),
            PatternType.Regex => TryMatchRegex(text, trigger, out match),
            PatternType.Sscanf => TryMatchSscanf(text, trigger, out match),
            _ => false
        };
    }

    private static bool TryMatchLiteral(string text, TriggerDefinition trigger, out TriggerMatch? match)
    {
        match = null;
        var comparison = trigger.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        var pattern = trigger.Pattern;
        var anchorStart = pattern.StartsWith('^');
        var anchorEnd = pattern.EndsWith('$');

        if (anchorStart) pattern = pattern[1..];
        if (anchorEnd) pattern = pattern[..^1];

        if (anchorStart && anchorEnd)
        {
            // Exact line match
            var lines = text.Split('\n');
            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd('\r');
                if (line.Equals(pattern, comparison))
                {
                    match = new TriggerMatch { Trigger = trigger, Captures = [], MatchedText = line };
                    return true;
                }
            }
        }
        else if (anchorStart)
        {
            var lines = text.Split('\n');
            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd('\r');
                if (line.StartsWith(pattern, comparison))
                {
                    match = new TriggerMatch { Trigger = trigger, Captures = [], MatchedText = line };
                    return true;
                }
            }
        }
        else if (anchorEnd)
        {
            var lines = text.Split('\n');
            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd('\r');
                if (line.EndsWith(pattern, comparison))
                {
                    match = new TriggerMatch { Trigger = trigger, Captures = [], MatchedText = line };
                    return true;
                }
            }
        }
        else
        {
            if (text.Contains(pattern, comparison))
            {
                match = new TriggerMatch { Trigger = trigger, Captures = [], MatchedText = pattern };
                return true;
            }
        }

        return false;
    }

    private static bool TryMatchRegex(string text, TriggerDefinition trigger, out TriggerMatch? match)
    {
        match = null;

        try
        {
            var options = RegexOptions.None;
            if (!trigger.CaseSensitive)
                options |= RegexOptions.IgnoreCase;
            // Block triggers see several lines: ^ and $ must still mean "of a line".
            if (trigger.Multiline)
                options |= RegexOptions.Multiline;

            var regex = RegexCache.Get(trigger.Pattern, options);
            if (regex is null)
                return false;

            var m = regex.Match(text);
            if (!m.Success)
                return false;

            var captures = new List<string>();
            for (var i = 1; i < m.Groups.Count; i++)
                captures.Add(m.Groups[i].Value);

            match = new TriggerMatch
            {
                Trigger = trigger,
                Captures = captures,
                MatchedText = m.Value
            };
            return true;
        }
        catch (RegexMatchTimeoutException)
        {
            // Pattern took too long: treat as no match to prevent ReDoS
            return false;
        }
    }

    private static bool TryMatchSscanf(string text, TriggerDefinition trigger, out TriggerMatch? match)
    {
        match = null;
        // As in the original client, a block is matched as one long line.
        var subject = trigger.Multiline ? text.Replace('\n', ' ') : text;
        var captures = SscanfMatcher.Match(subject, trigger.Pattern, trigger.CaseSensitive);

        if (captures is null)
            return false;

        match = new TriggerMatch
        {
            Trigger = trigger,
            Captures = captures,
            MatchedText = text
        };
        return true;
    }
}
