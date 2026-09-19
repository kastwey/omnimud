namespace Omnimud.Core.Scripting;

/// <summary>
/// What a Lua script can do to the session. This is the port of the original client's
/// CTriggerFunctions (OmSend, OmGet, OmDisplay, SayText, AddMessage, PlaySound, variables...).
/// Implemented by the session; every member must be thread-safe because scripts run outside
/// the UI thread.
/// </summary>
public interface IScriptHost
{
    /// <summary>om.send — through the full input pipeline (aliases, paths, internal commands, @triggers, ; and #).</summary>
    void Send(string command);

    /// <summary>om.sendraw — straight to the MUD.</summary>
    void SendRaw(string command);

    /// <summary>
    /// om.get — sends <paramref name="command"/> straight to the MUD and captures the reply, which is
    /// not displayed, logged, spoken nor run through triggers. With <paramref name="expectedPattern"/>
    /// only lines matching that regex are captured (the rest flow normally). Capture ends at the
    /// next prompt or after a short quiet period. Returns null on timeout.
    /// Requests are queued per session so concurrent scripts never mix replies.
    /// </summary>
    Task<string?> GetAsync(string command, string? expectedPattern, TimeSpan timeout, bool keepColors, CancellationToken ct);

    /// <summary>om.display — injects text as if it came from the MUD (ANSI, triggers, messages, log, speech).</summary>
    void Display(string text);

    /// <summary>om.echo — paints text only; no triggers, no log.</summary>
    void Echo(string text);

    /// <summary>om.say / om.notify — speaks through the screen reader.</summary>
    void Say(string text, bool interrupt);

    /// <summary>om.message — adds to the Messages box.</summary>
    void AddMessage(string text);

    /// <summary>om.playsound — looks up the file as given, then in the MUD's sound folder, then in the
    /// application's sounds folder. loop: 1 = once, N = N times, -1 = forever.</summary>
    void PlaySound(string name, int loop, int volume, int priority);

    /// <summary>om.stopsound — true if it was playing.</summary>
    bool StopSound(string name);

    void SetVariable(string name, string value);
    string? GetVariable(string name);
    bool RemoveVariable(string name);
    bool IsVariableSet(string name);

    /// <summary>om.status — status bar text.</summary>
    void SetStatus(string text);

    /// <summary>om.log — writes to the session log only.</summary>
    void Log(string text);

    /// <summary>om.gag — hides the line that fired the trigger. Only effective for line triggers.</summary>
    void Gag();

    /// <summary>om.lastactivity — seconds since the last command was sent to the MUD.</summary>
    double SecondsSinceLastActivity { get; }

    /// <summary>A script failed (runtime error, limits exceeded). Shown to the user; never fatal.</summary>
    void ReportScriptError(string scriptName, string error);
}
