namespace Omnimud.Data.Entities;

public sealed class CharacterEntity
{
    public int Id { get; set; }
    public int MudId { get; set; }
    public required string Name { get; set; }
    public byte[]? EncryptedPassword { get; set; }
    /// <summary>Kept in step with <see cref="MudEntity.DefaultCharacterId"/> by the repositories.</summary>
    public bool IsDefault { get; set; }
    /// <summary>Numpad movement mode (F2) remembered for this character.</summary>
    public bool MovementMode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
