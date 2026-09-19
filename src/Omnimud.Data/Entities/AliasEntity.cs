namespace Omnimud.Data.Entities;

public sealed class AliasEntity
{
    public int Id { get; set; }
    public int CharacterId { get; set; }
    public required string Command { get; set; }
    public required string Action { get; set; }
    public bool Enabled { get; set; } = true;
}
