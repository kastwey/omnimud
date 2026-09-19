namespace Omnimud.Data.Entities;

/// <summary>One entry of a MUD's direction dictionary.</summary>
public sealed class DirectionEntity
{
    public int Id { get; set; }
    public int MudId { get; set; }
    public required string Direction { get; set; }
    /// <summary>Exactly one character.</summary>
    public required string Abbreviation { get; set; }
    public string? OppositeDirection { get; set; }
}
