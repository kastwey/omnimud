using System.Text;
using Omnimud.Core.Scripting;
using Omnimud.Core.Triggers;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Presenters;

/// <summary>What a trigger would do with a sample text. Nothing is really sent, played or spoken.</summary>
public sealed class TriggerTestResult
{
    public bool Matched { get; init; }
    public IReadOnlyList<string> Captures { get; init; } = [];
    /// <summary>The command after replacing %1, %2…; null when the trigger sends none.</summary>
    public string? Command { get; init; }
    public string? Sound { get; init; }
    public bool HidesLine { get; init; }
    /// <summary>For Lua: what the script asked for, in order, already described.</summary>
    public IReadOnlyList<string> ScriptEffects { get; init; } = [];
    public bool ScriptRan { get; init; }
    public string? Error { get; init; }

    /// <summary>First line: what is announced.</summary>
    public string Summary => Error is not null && !Matched ? Error : Matched ? Strings.TrigTest_Matches : Strings.TrigTest_NoMatch;

    /// <summary>The whole report, one fact per line, for the read-only result box.</summary>
    public string Describe()
    {
        var text = new StringBuilder();
        text.AppendLine(Summary);
        if (!Matched) return text.ToString().TrimEnd();

        if (Captures.Count == 0) text.AppendLine(Strings.TrigTest_NoCaptures);
        for (var i = 0; i < Captures.Count; i++)
            text.AppendLine(string.Format(Strings.TrigTest_Capture, i + 1, Captures[i]));

        if (Command is not null) text.AppendLine(string.Format(Strings.TrigTest_Command, Command));
        if (Sound is not null) text.AppendLine(string.Format(Strings.TrigTest_Sound, Sound));
        if (HidesLine) text.AppendLine(Strings.TrigTest_HidesLine);

        if (ScriptRan)
        {
            if (ScriptEffects.Count == 0 && Error is null) text.AppendLine(Strings.TrigTest_ScriptNothing);
            foreach (var effect in ScriptEffects) text.AppendLine(effect);
        }
        if (Error is not null) text.AppendLine(Error);
        return text.ToString().TrimEnd();
    }
}

/// <summary>
/// Runs a trigger against a sample exactly as the session would (same matcher, same %N
/// substitution, same script engine) but against a host that only takes notes.
/// </summary>
public sealed class TriggerTester(IScriptEngine? scriptEngine, ITriggerMatcher? matcher = null)
{
    /// <summary>Tight on purpose: a test must answer quickly even if the script loops forever.</summary>
    public static ScriptLimits TestLimits { get; } = new()
    {
        MaxInstructions = 200_000,
        MaxExecutionTime = TimeSpan.FromSeconds(2),
        MaxTotalTime = TimeSpan.FromSeconds(5),
    };

    private readonly ITriggerMatcher _matcher = matcher ?? new TriggerMatcher();

    public async Task<TriggerTestResult> TestAsync(TriggerDefinition trigger, string sample, CancellationToken ct = default)
    {
        sample = (sample ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        if (sample.Length == 0) return new TriggerTestResult { Error = Strings.TrigTest_ErrNoSample };

        IReadOnlyList<string> captures;
        string matchedLine;
        string? fullCommand = null;

        if (trigger.IsCommandTrigger)
        {
            // As the input processor does: first word against the command, the rest are the arguments.
            var words = sample.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var comparison = trigger.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            if (words.Length == 0 || !trigger.Pattern.AsSpan(1).Equals(words[0], comparison))
                return new TriggerTestResult();
            captures = words[1..];
            matchedLine = sample;
            fullCommand = sample;
        }
        else
        {
            if (trigger.PatternType == PatternType.Regex && !IsValidRegex(trigger.Pattern))
                return new TriggerTestResult { Error = Strings.TrigTest_ErrBadRegex };

            // A line trigger sees one line at a time; a multiline one sees the whole block.
            var subjects = trigger.Multiline ? [sample] : sample.Split('\n');
            TriggerMatch? match = null;
            matchedLine = sample;
            foreach (var subject in subjects)
            {
                if (subject.Length == 0 || !_matcher.TryMatch(subject, trigger, out match) || match is null) continue;
                matchedLine = subject;
                break;
            }
            if (match is null) return new TriggerTestResult();
            captures = match.Captures;
        }

        var sendsCommand = trigger.ActionType is TriggerActionType.SendCommand or TriggerActionType.SendCommandAndPlaySound;
        var playsSound = trigger.ActionType is TriggerActionType.PlaySound or TriggerActionType.SendCommandAndPlaySound;
        var hides = trigger is { GagLine: true, Multiline: false, IsCommandTrigger: false };

        if (trigger.ActionType != TriggerActionType.Script)
        {
            return new TriggerTestResult
            {
                Matched = true,
                Captures = captures,
                Command = sendsCommand ? TriggerCaptures.Substitute(trigger.Action, captures) : null,
                Sound = playsSound && !string.IsNullOrWhiteSpace(trigger.Sound) ? trigger.Sound : null,
                HidesLine = hides,
            };
        }

        if (scriptEngine is null)
            return new TriggerTestResult { Matched = true, Captures = captures, HidesLine = hides, Error = Strings.TrigTest_ErrNoEngine };

        var host = new RecordingScriptHost();
        var context = new ScriptContext
        {
            ScriptName = trigger.Name,
            MatchedLine = matchedLine,
            Block = sample,
            Captures = captures,
            FullCommand = fullCommand,
        };

        string? error;
        try
        {
            var result = await scriptEngine.ExecuteAsync(trigger.Action, context, host, TestLimits, ct).ConfigureAwait(false);
            error = result.Success ? null : DescribeError(result);
        }
        catch (Exception ex) // the engine promises not to throw; a test must not take the dialog down anyway
        {
            error = string.Format(Strings.TrigTest_ScriptError, ex.Message);
        }

        return new TriggerTestResult
        {
            Matched = true,
            Captures = captures,
            HidesLine = hides || host.Gagged,
            ScriptRan = true,
            ScriptEffects = host.Effects,
            Error = error,
        };
    }

    private static string DescribeError(ScriptResult result)
    {
        var text = result.Error ?? result.ErrorKind.ToString();
        return result.ErrorLine is { } line && !text.Contains($"line {line}", StringComparison.OrdinalIgnoreCase)
            ? string.Format(Strings.TrigTest_ScriptErrorAtLine, line, text)
            : string.Format(Strings.TrigTest_ScriptError, text);
    }

    private static bool IsValidRegex(string pattern)
    {
        try
        {
            _ = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

/// <summary>A script host that does nothing but write down, in order, what the script asked for.</summary>
public sealed class RecordingScriptHost : IScriptHost
{
    private readonly object _lock = new();
    private readonly List<string> _effects = [];
    private readonly Dictionary<string, string> _variables = new(StringComparer.Ordinal);

    public IReadOnlyList<string> Effects
    {
        get { lock (_lock) return _effects.ToArray(); }
    }

    public bool Gagged { get; private set; }

    private void Note(string format, params object[] args)
    {
        lock (_lock) _effects.Add(string.Format(format, args));
    }

    public void Send(string command) => Note(Strings.TrigTest_FxSend, command);
    public void SendRaw(string command) => Note(Strings.TrigTest_FxSendRaw, command);

    public Task<string?> GetAsync(string command, string? expectedPattern, TimeSpan timeout, bool keepColors, CancellationToken ct)
    {
        Note(Strings.TrigTest_FxGet, command);
        return Task.FromResult<string?>(null); // there is no MUD to answer
    }

    public void Display(string text) => Note(Strings.TrigTest_FxDisplay, text);
    public void Echo(string text) => Note(Strings.TrigTest_FxEcho, text);
    public void Say(string text, bool interrupt) => Note(Strings.TrigTest_FxSay, text);
    public void AddMessage(string text) => Note(Strings.TrigTest_FxMessage, text);
    public void PlaySound(string name, int loop, int volume, int priority) => Note(Strings.TrigTest_FxPlaySound, name);

    public bool StopSound(string name)
    {
        Note(Strings.TrigTest_FxStopSound, name);
        return false;
    }

    public void SetVariable(string name, string value)
    {
        lock (_lock) _variables[name] = value;
        Note(Strings.TrigTest_FxSetVar, name, value);
    }

    public string? GetVariable(string name)
    {
        lock (_lock) return _variables.GetValueOrDefault(name);
    }

    public bool RemoveVariable(string name)
    {
        Note(Strings.TrigTest_FxRemoveVar, name);
        lock (_lock) return _variables.Remove(name);
    }

    public bool IsVariableSet(string name)
    {
        lock (_lock) return _variables.ContainsKey(name);
    }

    public void SetStatus(string text) => Note(Strings.TrigTest_FxStatus, text);
    public void Log(string text) => Note(Strings.TrigTest_FxLog, text);

    public void Gag()
    {
        Gagged = true;
    }

    public double SecondsSinceLastActivity => 0;

    /// <summary>The failure already comes back in the result; nothing to show here.</summary>
    public void ReportScriptError(string scriptName, string error) { }
}
