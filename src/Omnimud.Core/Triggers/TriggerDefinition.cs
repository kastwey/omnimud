namespace Omnimud.Core.Triggers;

public sealed class TriggerDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>
    /// Text that fires the trigger. When it starts with '@' this is a COMMAND trigger: it is
    /// matched against the first word the user types (after aliases) instead of MUD text, the
    /// remaining words become the captures, and the command is not sent to the MUD.
    /// </summary>
    public required string Pattern { get; init; }
    public PatternType PatternType { get; init; } = PatternType.Literal;
    public required string Action { get; init; }
    public TriggerActionType ActionType { get; init; } = TriggerActionType.SendCommand;
    public string? Sound { get; init; }
    public bool Enabled { get; set; } = true;
    public bool CaseSensitive { get; init; }
    public int Priority { get; init; } = 50;

    /// <summary>False (default): evaluated against each complete line. True: evaluated against the
    /// whole block of lines received together, as the original client did.</summary>
    public bool Multiline { get; init; }

    /// <summary>Hide the line that fired the trigger. Line triggers only.</summary>
    public bool GagLine { get; init; }

    public bool IsCommandTrigger => Pattern.StartsWith('@');
}

public enum PatternType
{
    Literal,
    Regex,
    Sscanf
}

public enum TriggerActionType
{
    SendCommand = 0,
    PlaySound = 1,
    Script = 2,
    /// <summary>Send the command and play the sound ("ambas" in the original client).</summary>
    SendCommandAndPlaySound = 3
}
