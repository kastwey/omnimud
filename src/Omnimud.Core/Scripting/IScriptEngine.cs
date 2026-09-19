namespace Omnimud.Core.Scripting;

/// <summary>
/// Engine for executing sandboxed scripts in response to trigger matches.
/// One instance per session: it owns that session's timers and countdowns, and disposing it
/// cancels every running script.
/// </summary>
public interface IScriptEngine : IDisposable
{
    /// <summary>
    /// Executes a script within the sandbox, collecting its effects in the result instead of
    /// applying them. Kept for tests and simple callers.
    /// </summary>
    Task<ScriptResult> ExecuteAsync(string script, ScriptContext context, ScriptLimits? limits = null, CancellationToken ct = default);

    /// <summary>
    /// Executes a script whose om.* calls act on <paramref name="host"/> as they happen.
    /// Never throws for script errors: they are reported through the result and
    /// <see cref="IScriptHost.ReportScriptError"/>.
    /// </summary>
    Task<ScriptResult> ExecuteAsync(string script, ScriptContext context, IScriptHost host, ScriptLimits? limits = null, CancellationToken ct = default);

    /// <summary>Compiles without running. Returns null if the script is valid, else the error text.</summary>
    string? Validate(string script);
}
