using Omnimud.Core.Aliases;
using Omnimud.Core.Paths;
using Omnimud.Core.Text;
using Omnimud.Core.Triggers;

namespace Omnimud.Core.Session;

/// <summary>Everything needed to open one connection. Immutable for the life of the session.</summary>
public sealed record SessionProfile
{
    public required string Title { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public bool UseTls { get; init; }
    public bool ValidateCertificate { get; init; } = true;
    /// <summary>Encoding name for both directions, e.g. "utf-8", "iso-8859-1", "windows-1252".</summary>
    public string Encoding { get; init; } = "utf-8";
    public string? LoginScript { get; init; }
    public string? SaveCommand { get; init; }
    public string? QuitCommand { get; init; }
    public int? MudId { get; init; }
    public string? MudName { get; init; }
    public int? CharacterId { get; init; }
    public string? CharacterName { get; init; }
    public string? CharacterPassword { get; init; }
    /// <summary>Folder with this MUD's sounds. Null = default folder for the MUD.</summary>
    public string? SoundDirectory { get; init; }
}

public enum SessionState
{
    Disconnected,
    Connecting,
    Connected,
    /// <summary>Connection failed and the user chose to work offline (manage aliases, triggers, paths).</summary>
    Offline
}

/// <summary>One line ready to paint, plus its plain text (no ANSI) for logs, triggers and speech.</summary>
public sealed record SessionLine(IReadOnlyList<StyledSegment> Segments, string PlainText, SessionLineKind Kind);

public enum SessionLineKind
{
    /// <summary>Text from the MUD terminated by a newline.</summary>
    Mud,
    /// <summary>Text from the MUD flushed without newline (a prompt).</summary>
    Prompt,
    /// <summary>Text produced by the client itself (command replies, errors, notices).</summary>
    System
}

/// <summary>An entry of the Messages box.</summary>
public sealed record SessionMessage(int Number, DateTime Time, string Text, string? Channel = null, string? Sender = null);

public enum AnnouncePriority
{
    /// <summary>Queued after whatever is being spoken (incoming MUD text).</summary>
    Queue,
    /// <summary>Replaces pending speech of the same kind (state changes).</summary>
    MostRecent,
    /// <summary>Interrupts speech (message review, time of message, errors).</summary>
    Interrupt
}

/// <summary>Windows a typed command can ask the UI to open.</summary>
public enum SessionWindow
{
    Aliases,
    Triggers,
    Paths,
    /// <summary>Add-path dialog prefilled with the recorded path (payload = collapsed path).</summary>
    NewPath
}

/// <summary>
/// Persistence seen from the session. Core does not reference the data layer; the adapter
/// lives in Omnimud.Data. Sessions without a character get empty lists and failed writes.
/// </summary>
public interface ISessionStore
{
    Task<IReadOnlyList<AliasDefinition>> GetAliasesAsync(int characterId, CancellationToken ct = default);
    /// <summary>Returns false if an alias with that command already exists.</summary>
    Task<bool> AddAliasAsync(int characterId, string command, string action, CancellationToken ct = default);
    /// <summary>Returns false if it did not exist.</summary>
    Task<bool> RemoveAliasAsync(int characterId, string command, CancellationToken ct = default);

    Task<IReadOnlyList<TriggerDefinition>> GetTriggersAsync(int characterId, CancellationToken ct = default);
    Task SetTriggerEnabledAsync(string triggerId, bool enabled, CancellationToken ct = default);

    Task<IReadOnlyList<PathDefinition>> GetPathsAsync(int characterId, CancellationToken ct = default);

    /// <summary>Direction dictionary of the MUD (full name, one-character abbreviation, opposite).</summary>
    Task<IReadOnlyList<DirectionEntry>> GetDirectionsAsync(int mudId, CancellationToken ct = default);

    /// <summary>Numpad key (0-9) → command. The character's own if it has any, else the MUD's.</summary>
    Task<IReadOnlyDictionary<int, string>> GetMovementsAsync(int? mudId, int? characterId, CancellationToken ct = default);
    Task<bool> GetMovementModeAsync(int? mudId, int? characterId, CancellationToken ct = default);
    Task SetMovementModeAsync(int? mudId, int? characterId, bool enabled, CancellationToken ct = default);

    /// <summary>Message extraction rules of the MUD's rule set, in evaluation order. Empty = no rule set.</summary>
    Task<IReadOnlyList<MessageRule>> GetMessageRulesAsync(int mudId, CancellationToken ct = default);

    /// <summary>
    /// Lua script of the MUD's rule set when it is of type "script" (see
    /// <see cref="Omnimud.Core.Messages.MessageRuleScript"/>); null or empty when the set works with
    /// patterns or there is no set. A set with a script does not evaluate its patterns.
    /// Default implementation so that existing stores keep compiling: no script.
    /// </summary>
    Task<string?> GetMessageRuleScriptAsync(int mudId, CancellationToken ct = default) => Task.FromResult<string?>(null);
}

/// <summary>
/// One message-extraction rule: when <see cref="Pattern"/> (a .NET regex, evaluated per line on
/// text without ANSI) matches, <see cref="Template"/> is expanded with the match
/// ($1, ${name}, $0) and the result goes to the Messages box.
/// </summary>
public sealed record MessageRule(string Pattern, string Template, bool CaseSensitive = false, string? Channel = null);
