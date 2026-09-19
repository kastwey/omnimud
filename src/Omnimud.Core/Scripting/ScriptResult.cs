namespace Omnimud.Core.Scripting;

/// <summary>Why a script failed. Lets callers localise or react without parsing <see cref="ScriptResult.Error"/>.</summary>
public enum ScriptErrorKind
{
    None = 0,
    Syntax,
    Runtime,
    InstructionLimit,
    /// <summary>MaxExecutionTime or MaxTotalTime exceeded.</summary>
    Timeout,
    MemoryLimit,
    /// <summary>The caller's token was cancelled.</summary>
    Cancelled,
    /// <summary>The engine was disposed before or while the script ran.</summary>
    EngineDisposed
}

/// <summary>
/// Represents the result of executing a script.
/// The effect lists are only filled by the overload that runs WITHOUT a host; with a host the
/// effects have already been applied and the lists are empty.
/// </summary>
public sealed class ScriptResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public ScriptErrorKind ErrorKind { get; init; }

    /// <summary>1-based line of the script where the error happened, when known.</summary>
    public int? ErrorLine { get; init; }

    /// <summary>om.send, om.sendraw and om.get commands, in order.</summary>
    public IReadOnlyList<string> CommandsToSend { get; init; } = [];

    /// <summary>om.display, om.echo, print and om.log (prefixed with "[LOG] ").</summary>
    public IReadOnlyList<string> DisplayMessages { get; init; } = [];

    /// <summary>om.say and om.notify.</summary>
    public IReadOnlyList<string> Notifications { get; init; } = [];

    /// <summary>om.message.</summary>
    public IReadOnlyList<string> Messages { get; init; } = [];

    /// <summary>om.playsound names.</summary>
    public IReadOnlyList<string> SoundsToPlay { get; init; } = [];

    /// <summary>True if the script called om.gag().</summary>
    public bool Gagged { get; init; }

    public static ScriptResult Ok(IReadOnlyList<string> commands, IReadOnlyList<string> displayMessages, IReadOnlyList<string> notifications)
        => new() { Success = true, CommandsToSend = commands, DisplayMessages = displayMessages, Notifications = notifications };

    public static ScriptResult Ok() => new() { Success = true };

    public static ScriptResult Fail(string error)
        => new() { Success = false, Error = error, ErrorKind = ScriptErrorKind.Runtime };

    public static ScriptResult Fail(ScriptErrorKind kind, string error, int? line = null)
        => new() { Success = false, Error = error, ErrorKind = kind, ErrorLine = line };
}
