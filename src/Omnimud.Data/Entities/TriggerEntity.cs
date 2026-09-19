namespace Omnimud.Data.Entities;

public sealed class TriggerEntity
{
    public required string Id { get; set; }
    public int CharacterId { get; set; }
    public required string Name { get; set; }
    public required string Pattern { get; set; }
    public int PatternType { get; set; }
    /// <summary>May be empty for sound-only triggers.</summary>
    public required string Action { get; set; }
    /// <summary>0 command, 1 sound, 2 script, 3 command + sound.</summary>
    public int ActionType { get; set; }
    public string? Sound { get; set; }
    public bool Enabled { get; set; } = true;
    public bool CaseSensitive { get; set; }
    public int Priority { get; set; } = 50;
    public bool Multiline { get; set; }
    public bool GagLine { get; set; }
    /// <summary>Creation order within the character (the N of "-trigger N"). 0 on insert = append.</summary>
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
