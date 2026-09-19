using Omnimud.Core.Triggers;

namespace Omnimud.Data.Exchange;

/// <summary>What an .omnimud file carries. Informational: the importer acts on the sections that are present.</summary>
public enum ExchangeKind
{
    Mud,
    Character,
    Aliases,
    Triggers,
    Paths,
    Movements,
    Options,
    /// <summary>Several loose lists at once (used when copying from another character).</summary>
    Lists
}

/// <summary>
/// Root of an .omnimud file (JSON, camelCase, UTF-8). Identifiers of the database never travel:
/// everything is referenced by name. Passwords are never part of the model.
/// </summary>
public sealed class ExchangeDocument
{
    public const string FormatName = "omnimud";
    public const int CurrentFormatVersion = 1;

    public string Format { get; set; } = FormatName;
    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public ExchangeKind Kind { get; set; }
    public DateTime? ExportedAt { get; set; }

    public MudDto? Mud { get; set; }
    public CharacterDto? Character { get; set; }
    public List<AliasDto>? Aliases { get; set; }
    public List<TriggerDto>? Triggers { get; set; }
    public List<PathDto>? Paths { get; set; }
    public List<MovementDto>? Movements { get; set; }
    /// <summary>Option key → invariant text, as produced by OptionsSerializer.</summary>
    public Dictionary<string, string>? Options { get; set; }
}

public sealed class MudDto
{
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public bool UseTls { get; set; }
    public bool ValidateCertificate { get; set; } = true;
    public string Encoding { get; set; } = "utf-8";
    public string? SaveCommand { get; set; }
    public string? QuitCommand { get; set; }
    public string? LoginScript { get; set; }
    public string? SoundDirectory { get; set; }
    public bool MovementMode { get; set; }
    /// <summary>Name of the default character; only meaningful when characters travel too.</summary>
    public string? DefaultCharacter { get; set; }
    public List<DirectionDto> Directions { get; set; } = [];
    public List<MovementDto> Movements { get; set; } = [];
    /// <summary>Null = the MUD inherits the global options.</summary>
    public Dictionary<string, string>? Options { get; set; }
    public MessageRuleSetDto? MessageRuleSet { get; set; }
    /// <summary>Null when the MUD was exported without characters.</summary>
    public List<CharacterDto>? Characters { get; set; }
}

public sealed class CharacterDto
{
    public string Name { get; set; } = string.Empty;
    /// <summary>MUD the character was exported from (informational).</summary>
    public string? MudName { get; set; }
    public bool MovementMode { get; set; }
    public List<AliasDto> Aliases { get; set; } = [];
    public List<TriggerDto> Triggers { get; set; } = [];
    public List<PathDto> Paths { get; set; } = [];
    public List<MovementDto> Movements { get; set; } = [];
    /// <summary>Null = the character inherits.</summary>
    public Dictionary<string, string>? Options { get; set; }
}

public sealed class AliasDto
{
    public string Command { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

/// <summary>Triggers travel in creation order and get a new identifier on import.</summary>
public sealed class TriggerDto
{
    public string Name { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
    public PatternType PatternType { get; set; }
    public string Action { get; set; } = string.Empty;
    public TriggerActionType ActionType { get; set; }
    public string? Sound { get; set; }
    public bool Enabled { get; set; } = true;
    public bool CaseSensitive { get; set; }
    public int Priority { get; set; } = 50;
    public bool Multiline { get; set; }
    public bool GagLine { get; set; }
}

public sealed class PathDto
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

public sealed class DirectionDto
{
    public string Direction { get; set; } = string.Empty;
    public string Abbreviation { get; set; } = string.Empty;
    public string? Opposite { get; set; }
}

public sealed class MovementDto
{
    /// <summary>Movement key code, 0-17 (Omnimud.Core.Session.MovementKey): 0-9 numpad, 10-17 arrows, Page Up/Down, Home, End.</summary>
    public int Key { get; set; }
    public string Command { get; set; } = string.Empty;
}

public sealed class MessageRuleSetDto
{
    public string Name { get; set; } = string.Empty;
    /// <summary>Lua script of a set of type "script". Optional: files written before it existed simply lack it.</summary>
    public string? Script { get; set; }
    public List<MessageRuleDto> Rules { get; set; } = [];
}

public sealed class MessageRuleDto
{
    public string Pattern { get; set; } = string.Empty;
    public string Template { get; set; } = "$0";
    public bool CaseSensitive { get; set; }
    public string? Channel { get; set; }
    public bool Enabled { get; set; } = true;
}
