using System.Text.RegularExpressions;
using Omnimud.Core.Session;

namespace Omnimud.Core.Messages;

/// <summary>
/// The message-extraction rules of a MUD, compiled. Rules are tried in order against one line
/// of text without ANSI; the first that matches produces the message by expanding its template
/// ($1, ${name}, $0). Invalid patterns are skipped; a rule can never throw into the session.
/// </summary>
public sealed class MessageRuleSet
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);
    private readonly List<(Regex Regex, MessageRule Rule)> _rules = [];

    public MessageRuleSet(IEnumerable<MessageRule> rules)
    {
        foreach (var rule in rules)
        {
            if (string.IsNullOrEmpty(rule.Pattern))
                continue;

            try
            {
                var options = RegexOptions.CultureInvariant;
                if (!rule.CaseSensitive) options |= RegexOptions.IgnoreCase;
                _rules.Add((new Regex(rule.Pattern, options, MatchTimeout), rule));
            }
            catch (ArgumentException)
            {
                // A broken rule must not take the others down with it.
            }
        }
    }

    public int Count => _rules.Count;

    public MessageRuleResult? Match(string line)
    {
        if (_rules.Count == 0 || string.IsNullOrEmpty(line))
            return null;

        foreach (var (regex, rule) in _rules)
        {
            try
            {
                var match = regex.Match(line);
                if (!match.Success)
                    continue;

                var text = string.IsNullOrEmpty(rule.Template) ? match.Value : match.Result(rule.Template);
                if (text.Length == 0)
                    continue;

                var sender = match.Groups["sender"] is { Success: true } g ? g.Value : null;
                return new MessageRuleResult(text, rule.Channel, sender);
            }
            catch (RegexMatchTimeoutException)
            {
            }
        }

        return null;
    }
}

public sealed record MessageRuleResult(string Text, string? Channel, string? Sender);
