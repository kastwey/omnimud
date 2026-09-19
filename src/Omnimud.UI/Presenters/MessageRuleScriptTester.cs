using Omnimud.Core.Messages;
using Omnimud.Core.Scripting;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Presenters;

/// <summary>Outcome of trying a message-rules script against a sample block.</summary>
/// <param name="Text">What the result box shows: the numbered messages, "no message" or the error.</param>
/// <param name="ErrorLine">1-based line of the script to put the caret on, when the error has one.</param>
public sealed record MessageRuleScriptTestResult(bool Success, IReadOnlyList<string> Messages, string Text, int? ErrorLine = null);

/// <summary>
/// Validation and "Test" of a rule set of type Lua script, exactly as a session runs it
/// (<see cref="MessageRuleScript"/>: same limits, no waiting, only om.message counts). No WinForms.
/// </summary>
public sealed class MessageRuleScriptTester(IScriptEngine? engine)
{
    /// <summary>A starting point for a new script set: valid, harmless and with the contract in its comments.</summary>
    public static string Template =>
        $"-- {Strings.MsgRules_ScriptTemplate1}\n-- {Strings.MsgRules_ScriptTemplate2}\n" +
        "for _, line in ipairs(om.lines) do\n" +
        "  if om.match(line, [[^\\w+ te dice ]]) then\n" +
        "    om.message(line)\n" +
        "  end\n" +
        "end\n";

    /// <summary>Null when the script can be saved; otherwise the message and, for syntax errors, the line.</summary>
    public EditorIssue? Validate(string? script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new(FieldScript, Strings.MsgRules_ScriptEmpty);

        if (engine?.Validate(script) is not { Length: > 0 } error)
            return null;

        var line = TriggerEditorModel.ErrorLine(error);
        return new(FieldScript, line is null
            ? string.Format(Strings.MsgRules_ScriptError, error)
            : string.Format(Strings.MsgRules_ScriptErrorAtLine, line, error), line);
    }

    public const string FieldScript = "Script";

    /// <summary>Runs the script over the sample (a block of several lines). Never throws because of the script.</summary>
    public async Task<MessageRuleScriptTestResult> TestAsync(string? script, string? sample, CancellationToken ct = default)
    {
        if (Validate(script) is { } issue)
            return new(false, [], issue.Message, issue.Line);
        if (string.IsNullOrWhiteSpace(sample))
            return new(false, [], Strings.MsgRule_TestEmpty);
        if (engine is null)
            return new(false, [], Strings.MsgRules_ScriptTestUnavailable);

        // The first run of a process loads the interpreter; a live session warms up too.
        await MessageRuleScript.WarmUpAsync(engine, script!, ct).ConfigureAwait(false);
        var lines = MessageRuleScript.SplitLines(sample);
        if (lines.Count > 1 && lines[^1].Length == 0)
            lines = lines.Take(lines.Count - 1).ToArray(); // the line break that ends the last line is not one more line

        var result = await MessageRuleScript.RunAsync(engine, script!, lines, ct: ct).ConfigureAwait(false);
        if (!result.Success)
        {
            var error = result.Error ?? string.Empty;
            if (result.ErrorLine is { } line && !error.Contains($"line {line}", StringComparison.OrdinalIgnoreCase))
                error = string.Format(Strings.MsgRules_ScriptErrorAtLine, line, error);
            return new(false, [], string.Format(Strings.MsgRules_ScriptTestError, error), result.ErrorLine);
        }

        if (result.Messages.Count == 0)
            return new(true, [], Strings.MsgRules_ScriptTestNone);

        return new(true, result.Messages, Describe(result.Messages));
    }

    /// <summary>"Messages: 2", then one per line, numbered; the following lines of a wrapped message are indented.</summary>
    internal static string Describe(IReadOnlyList<string> messages)
    {
        var lines = new List<string> { string.Format(Strings.MsgRules_ScriptTestCount, messages.Count) };
        for (var i = 0; i < messages.Count; i++)
        {
            var parts = messages[i].Replace("\r\n", "\n").Split('\n');
            lines.Add($"{i + 1}. {parts[0]}");
            lines.AddRange(parts.Skip(1).Select(p => "   " + p));
        }
        return string.Join('\n', lines);
    }
}
