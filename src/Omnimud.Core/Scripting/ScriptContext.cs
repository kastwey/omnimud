namespace Omnimud.Core.Scripting;

/// <summary>
/// Context passed to a script during execution (captures, variables, etc.).
/// </summary>
public sealed class ScriptContext
{
    /// <summary>Name shown in error reports (usually the trigger name).</summary>
    public string ScriptName { get; init; } = string.Empty;

    /// <summary>
    /// The line of text that triggered the script (om.line).
    /// </summary>
    public string MatchedLine { get; init; } = string.Empty;

    /// <summary>The whole block of lines received together (om.block). Same as the line for line triggers.</summary>
    public string Block { get; init; } = string.Empty;

    /// <summary>
    /// The lines of the block (om.lines, 1-based in Lua). Null = <see cref="Block"/> (or the matched
    /// line) split at line feeds.
    /// </summary>
    public IReadOnlyList<string>? Lines { get; init; }

    /// <summary>
    /// False for scripts that must behave as a pure function of their input (message rules): om.sleep,
    /// om.get, om.countdown and om.timer then fail with a Lua error instead of waiting.
    /// </summary>
    public bool AllowWaiting { get; init; } = true;

    /// <summary>
    /// Captured groups from trigger pattern matching (e.g., regex groups or sscanf values) (om.captures).
    /// For command triggers these are the words after the command (also exposed as om.args).
    /// </summary>
    public IReadOnlyList<string> Captures { get; init; } = [];

    /// <summary>For command triggers (@name): the full line the user typed (om.command). Null otherwise.</summary>
    public string? FullCommand { get; init; }

    public string? MudName { get; init; }
    public string? CharacterName { get; init; }

    /// <summary>
    /// Session variables shared across scripts. Only used by the legacy overload that runs
    /// without a host; with a host, variables live in <see cref="IScriptHost"/>.
    /// </summary>
    public Dictionary<string, string> Variables { get; init; } = new();
}
