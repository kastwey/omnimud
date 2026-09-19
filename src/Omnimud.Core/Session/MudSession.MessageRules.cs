using Omnimud.Core.Messages;
using Omnimud.Core.Resources;
using Omnimud.Core.Scripting;

namespace Omnimud.Core.Session;

// Message rules: a set works either with patterns (one regex per line, MessageRuleSet) or with a
// Lua script that sees the whole received block (MessageRuleScript), like the original client's
// processing rules did. Never both.
public sealed partial class MudSession
{
    /// <summary>Consecutive blocks stopped by a limit after which the script is switched off until the next reload.</summary>
    public const int MaxConsecutiveLimitFailures = 3;

    /// <summary>One line handed to the rules script, and what reception did with it.</summary>
    private readonly record struct ScriptedLine(string Text, bool Announced, bool GmcpCopy);

    private string? _ruleScript;            // null = the set works with patterns (or there is no set)
    private bool _ruleScriptErrorReported;  // one System line per load, not one per block
    private int _ruleScriptLimitFailures;

    private void LoadMessageRules(IReadOnlyList<MessageRule> rules, string? script)
    {
        _ruleScriptErrorReported = false;
        _ruleScriptLimitFailures = 0;

        if (!MessageRuleScript.IsScript(script))
        {
            _ruleScript = null;
            _rules = new MessageRuleSet(rules);
            return;
        }

        // A set of type script does not evaluate its patterns.
        _rules = new MessageRuleSet([]);
        _ruleScript = script;

        string? syntaxError = null;
        try
        {
            syntaxError = _scripts.Validate(script!);
        }
        catch (Exception ex)
        {
            syntaxError = ex.Message;
        }

        if (!string.IsNullOrEmpty(syntaxError))
        {
            _ruleScript = null;
            _ruleScriptErrorReported = true;
            WriteSystem(string.Format(Strings.Session_MessageScriptError, syntaxError), AnnouncePriority.Queue);
        }
    }

    /// <summary>
    /// Runs the rules script over the lines of one block and adds what it produced to Messages.
    /// Awaited inside the block: the script has no host, so it cannot re-enter the queue, and its
    /// limits (<see cref="MessageRuleScript.Limits"/>) keep reception moving whatever it does.
    /// </summary>
    private async Task RunMessageScriptAsync(IReadOnlyList<ScriptedLine> lines)
    {
        var script = _ruleScript;
        if (script is null) return;

        MessageScriptResult result;
        try
        {
            result = await MessageRuleScript.RunAsync(
                _scripts, script, lines.Select(l => l.Text).ToArray(),
                Profile.MudName ?? Profile.Title, Profile.CharacterName, _cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result = new MessageScriptResult([], ex.Message, ScriptErrorKind.Runtime);
        }

        if (!result.Success)
        {
            HandleMessageScriptFailure(result);
            return;
        }

        _ruleScriptLimitFailures = 0;
        foreach (var message in result.Messages)
        {
            var (text, alreadyAnnounced) = Reconcile(message, lines);
            if (text is not null)
                AddMessageCore(text, null, null, alreadyAnnounced);
        }
    }

    private void HandleMessageScriptFailure(MessageScriptResult result)
    {
        // Closing the session is not the script's fault.
        if (result.ErrorKind is ScriptErrorKind.Cancelled or ScriptErrorKind.EngineDisposed)
            return;

        if (!_ruleScriptErrorReported)
        {
            _ruleScriptErrorReported = true;
            WriteSystem(string.Format(Strings.Session_MessageScriptError, result.Error), AnnouncePriority.Queue);
        }

        if (!result.LimitExceeded)
        {
            _ruleScriptLimitFailures = 0;
            return;
        }

        // A script that runs into its limits on every block costs up to 100 ms each time: stop paying.
        if (++_ruleScriptLimitFailures >= MaxConsecutiveLimitFailures)
        {
            _ruleScript = null;
            WriteSystem(Strings.Session_MessageScriptDisabled, AnnouncePriority.Queue);
        }
    }

    /// <summary>
    /// Decides what becomes of one message of the script given the lines it came from:
    ///  * lines that are the text copy of a GMCP message are already in Messages: they are taken out,
    ///    and a message made only of them is dropped (null);
    ///  * a message wrapped over several lines can be the copy of a GMCP message as a whole;
    ///  * the message is not spoken again when the lines it is made of were already spoken.
    /// </summary>
    private (string? Text, bool AlreadyAnnounced) Reconcile(string message, IReadOnlyList<ScriptedLine> lines)
    {
        var parts = message.Replace("\r\n", "\n").Split('\n');
        var kept = new List<string>(parts.Length);
        var sources = 0;
        var sourcesAnnounced = 0;
        var droppedCopy = false;

        foreach (var part in parts)
        {
            var probe = part.Trim();
            if (probe.Length == 0)
            {
                kept.Add(part);
                continue;
            }

            var index = IndexOfSource(lines, probe);
            if (index >= 0 && lines[index].GmcpCopy)
            {
                droppedCopy = true;
                continue;
            }

            if (index >= 0)
            {
                sources++;
                if (lines[index].Announced) sourcesAnnounced++;
            }
            kept.Add(part);
        }

        var text = string.Join('\n', kept).Trim('\n');
        if (string.IsNullOrWhiteSpace(text))
            return (null, true);

        if (!droppedCopy && kept.Count > 1 && GmcpEcho.IsEchoOfGmcpMessage(text))
            return (null, true);

        // A message the script composed by itself has no source line: go by the block as a whole.
        var announced = sources > 0 ? sourcesAnnounced == sources : lines.Any(l => l.Announced);
        return (text, announced);
    }

    private static int IndexOfSource(IReadOnlyList<ScriptedLine> lines, string probe)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Text.Length > 0 && lines[i].Text.Contains(probe, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }
}
