using System.Text.RegularExpressions;
using Omnimud.Core.Messages;
using Omnimud.Core.Session;
using Omnimud.Data.Entities;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Presenters;

public enum MessageRuleField
{
    Pattern,
    Template
}

/// <summary>Outcome of trying rules against a sample text.</summary>
/// <param name="RuleNumber">1-based position of the rule that matched, when a whole set was tried.</param>
public sealed record MessageRuleTestResult(bool Matched, string Text, string? Message = null, string? Channel = null, int? RuleNumber = null);

/// <summary>Tries message rules against sample text exactly as a session would (<see cref="MessageRuleSet"/>).</summary>
public static class MessageRuleTester
{
    /// <summary>Generous compared with the 100 ms of a live session: here a slow pattern must be reported, not hidden.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(500);

    /// <summary>Null when the pattern is a valid regular expression; otherwise the localized reason.</summary>
    public static string? PatternError(string pattern, bool caseSensitive)
    {
        if (string.IsNullOrEmpty(pattern)) return Strings.MsgRule_PatternRequired;
        try
        {
            _ = new Regex(pattern, Options(caseSensitive), Timeout);
            return null;
        }
        catch (ArgumentException ex)
        {
            return string.Format(Strings.MsgRule_PatternInvalid, ex.Message);
        }
    }

    /// <summary>Every line of the sample is tried against the enabled rules in order; the first match wins.</summary>
    public static MessageRuleTestResult Test(IReadOnlyList<MessageRuleEntity> rules, string sample)
    {
        if (string.IsNullOrWhiteSpace(sample))
            return new(false, Strings.MsgRule_TestEmpty);

        foreach (var line in sample.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0))
        {
            for (var i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (!rule.Enabled || PatternError(rule.Pattern, rule.CaseSensitive) is not null) continue;

                try
                {
                    // MessageRuleSet swallows timeouts (a session must never stop): detect them here first.
                    _ = new Regex(rule.Pattern, Options(rule.CaseSensitive), Timeout).IsMatch(line);
                }
                catch (RegexMatchTimeoutException)
                {
                    return new(false, string.Format(Strings.MsgRule_TestTimeout, i + 1), RuleNumber: i + 1);
                }

                var set = new MessageRuleSet([new MessageRule(rule.Pattern, rule.Template, rule.CaseSensitive, rule.Channel)]);
                if (set.Match(line) is not { } result) continue;

                var text = rules.Count == 1
                    ? string.Format(Strings.MsgRule_TestMatch, result.Text)
                    : string.Format(Strings.MsgRule_TestMatchRule, i + 1, result.Text);
                if (!string.IsNullOrEmpty(result.Channel))
                    text = string.Format(Strings.MsgRule_TestChannel, text, result.Channel);
                return new(true, text, result.Text, result.Channel, i + 1);
            }
        }

        return new(false, Strings.MsgRule_TestNoMatch);
    }

    private static RegexOptions Options(bool caseSensitive) =>
        RegexOptions.CultureInvariant | (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
}

/// <summary>State, validation and "Test" of the add/edit message rule dialog.</summary>
public sealed class MessageRuleEditorModel
{
    private readonly MessageRuleEntity? _existing;

    public MessageRuleEditorModel(MessageRuleEntity? existing = null)
    {
        _existing = existing;
        if (existing is null) return;
        Pattern = existing.Pattern;
        Template = existing.Template;
        CaseSensitive = existing.CaseSensitive;
        Channel = existing.Channel ?? string.Empty;
        Enabled = existing.Enabled;
    }

    public bool IsNew => _existing is null;
    public string Title => IsNew ? Strings.MsgRule_TitleAdd : Strings.MsgRule_TitleEdit;

    public string Pattern { get; set; } = string.Empty;
    /// <summary>$1, ${name}, $0 (the whole match). Empty means $0.</summary>
    public string Template { get; set; } = "$0";
    public bool CaseSensitive { get; set; }
    public string Channel { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    public FieldError<MessageRuleField>? Validate() =>
        MessageRuleTester.PatternError(Pattern, CaseSensitive) is { } error ? new(MessageRuleField.Pattern, error) : null;

    /// <summary>The rule as edited. Keeps id, set and position of the rule being edited.</summary>
    public MessageRuleEntity ToEntity(int ruleSetId) => new()
    {
        Id = _existing?.Id ?? 0,
        RuleSetId = _existing?.RuleSetId ?? ruleSetId,
        SortOrder = _existing?.SortOrder ?? 0,
        Pattern = Pattern,
        Template = string.IsNullOrEmpty(Template) ? "$0" : Template,
        CaseSensitive = CaseSensitive,
        Channel = string.IsNullOrWhiteSpace(Channel) ? null : Channel.Trim(),
        Enabled = Enabled,
    };

    /// <summary>Tries the rule being edited (even if disabled) against the sample.</summary>
    public MessageRuleTestResult Test(string sample)
    {
        if (Validate() is { } error) return new(false, error.Message);
        var rule = ToEntity(0);
        rule.Enabled = true;
        return MessageRuleTester.Test([rule], sample);
    }
}
