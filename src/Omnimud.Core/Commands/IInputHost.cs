using Omnimud.Core.Options;
using Omnimud.Core.Session;
using Omnimud.Core.Triggers;

namespace Omnimud.Core.Commands;

/// <summary>
/// What the <see cref="InputProcessor"/> needs from the session that owns it. Every member is
/// called from inside the session's serialized processing, never concurrently.
/// </summary>
public interface IInputHost
{
    OmnimudOptions Options { get; }
    SessionState State { get; }
    bool PasswordMode { get; }
    int? CharacterId { get; }
    ISessionStore Store { get; }

    /// <summary>"-triggers" / "+triggers": session-only switch for the whole trigger system.</summary>
    bool TriggersEnabled { get; set; }
    bool SilentMode { get; set; }

    /// <summary>Finds a trigger by name for "-trigger N" / "+trigger N".</summary>
    TriggerDefinition? FindTrigger(string name);

    /// <summary>Runs the @command triggers bound to <paramref name="word"/>. True if any matched
    /// (the command is then not sent to the MUD).</summary>
    Task<bool> RunCommandTriggersAsync(string word, IReadOnlyList<string> args, string fullCommand, int depth);

    /// <summary>Sends one line to the MUD (encoding, trailing newline, last activity). When
    /// <paramref name="log"/> is false nothing reaches the log (passwords).</summary>
    Task SendLineAsync(string line, bool log);

    /// <summary>Shows a reply of the client itself: painted, logged and announced, never run through triggers.</summary>
    void WriteSystem(string text);

    void RequestWindow(SessionWindow window, string? payload);
    void RequestClear();
    void PlayUiSound(string name);
    void SetStatus(string text);
    /// <summary>Yes/no question to the user. True when nobody can answer.</summary>
    bool Confirm(string question);
}
