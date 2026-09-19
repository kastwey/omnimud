using Omnimud.Core.Scripting;

namespace Omnimud.Core.Messages;

/// <summary>What a message-rules script produced for one block.</summary>
/// <param name="Messages">Texts passed to om.message, in order. Empty when the script failed.</param>
/// <param name="Error">Null on success; otherwise the engine's description of the failure.</param>
public sealed record MessageScriptResult(IReadOnlyList<string> Messages, string? Error = null, ScriptErrorKind ErrorKind = ScriptErrorKind.None, int? ErrorLine = null)
{
    public bool Success => Error is null;

    /// <summary>True when the script was stopped by a limit (time, instructions, memory) rather than by a mistake in it.</summary>
    public bool LimitExceeded => ErrorKind is ScriptErrorKind.Timeout or ScriptErrorKind.InstructionLimit or ScriptErrorKind.MemoryLimit;
}

/// <summary>
/// A rule set of type "Lua script": the port of the original client's processing rules (DLLs with
/// logic). The script is a PURE FUNCTION of the received block: it reads om.block / om.lines and
/// calls om.message(text) zero or more times. Nothing else it does has any effect (it runs against
/// a collecting host, never against the session), it cannot wait (om.sleep, om.get, om.countdown
/// and om.timer fail) and it runs under limits tight enough never to hold up reception.
/// Used by the session for every block and by the rules editor for "Test", so both behave alike.
/// </summary>
public static class MessageRuleScript
{
    /// <summary>Name under which errors are reported.</summary>
    public const string ScriptName = "message rules";

    /// <summary>100 ms of script time, 50 000 instructions, no waiting.</summary>
    public static ScriptLimits Limits { get; } = new()
    {
        MaxExecutionTime = TimeSpan.FromMilliseconds(100),
        MaxInstructions = 50_000,
        MaxTotalTime = TimeSpan.FromSeconds(1),
        MaxStringLength = 200_000,
        MaxAllocatedBytes = 32L * 1024 * 1024,
        HardAbortGrace = TimeSpan.FromMilliseconds(300)
    };

    public static bool IsScript(string? script) => !string.IsNullOrWhiteSpace(script);

    /// <summary>Splits sample text the way a session delivers a block: one entry per line, no line breaks.</summary>
    public static IReadOnlyList<string> SplitLines(string text)
        => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static readonly string[] WarmUpLines = ["", "Warm up: 'a' [b] <c> **d** e"];

    /// <summary>
    /// Runs the script once over a harmless block and throws the result away, with roomy limits. The
    /// first execution pays for loading the interpreter and compiling the script's regular
    /// expressions, which can take longer than <see cref="Limits"/> allows; a session does this when
    /// it loads its rules so that the first real block is not the one to pay.
    /// </summary>
    public static async Task WarmUpAsync(IScriptEngine engine, string script, CancellationToken ct = default)
    {
        if (!IsScript(script)) return;
        try
        {
            var context = new ScriptContext { ScriptName = ScriptName, Block = string.Join('\n', WarmUpLines), Lines = WarmUpLines, AllowWaiting = false };
            await engine.ExecuteAsync(script, context, ScriptLimits.Default with { MaxTotalTime = TimeSpan.FromSeconds(10) }, ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Warming up is a courtesy; real errors show up with the first block.
        }
    }

    /// <summary>Runs the script over one block. Never throws because of the script.</summary>
    public static async Task<MessageScriptResult> RunAsync(
        IScriptEngine engine,
        string script,
        IReadOnlyList<string> lines,
        string? mudName = null,
        string? characterName = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(lines);
        if (!IsScript(script))
            return new MessageScriptResult([]);

        var block = string.Join('\n', lines);
        var context = new ScriptContext
        {
            ScriptName = ScriptName,
            MatchedLine = lines.Count > 0 ? lines[0] : string.Empty,
            Block = block,
            Lines = lines,
            AllowWaiting = false,
            MudName = mudName,
            CharacterName = characterName
        };

        ScriptResult? result;
        try
        {
            result = await engine.ExecuteAsync(script, context, Limits, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            return new MessageScriptResult([], ex.Message, ScriptErrorKind.Cancelled);
        }
        catch (Exception ex)
        {
            return new MessageScriptResult([], ex.Message, ScriptErrorKind.Runtime);
        }

        if (result is null)
            return new MessageScriptResult([]);
        if (!result.Success)
            return new MessageScriptResult([], result.Error ?? string.Empty, result.ErrorKind, result.ErrorLine);

        return new MessageScriptResult(result.Messages.Where(m => !string.IsNullOrWhiteSpace(m)).ToArray());
    }
}
