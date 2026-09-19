namespace Omnimud.Data.Entities;

/// <summary>A numpad key bound to a command. Belongs to a MUD or to a character, never both.</summary>
public sealed class MovementEntity
{
    public int Id { get; set; }
    public int? MudId { get; set; }
    public int? CharacterId { get; set; }
    /// <summary>Movement key code, 0-17 (Omnimud.Core.Session.MovementKey): 0-9 numpad, 10-17 arrows, Page Up/Down, Home, End.
    /// An empty <see cref="Command"/> switches the key off (no default command either).</summary>
    public int KeyCode { get; set; }
    public required string Command { get; set; }
}
