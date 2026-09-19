namespace Omnimud.Data.Entities;

public sealed class PathEntity
{
    public int Id { get; set; }
    public int CharacterId { get; set; }
    public required string Name { get; set; }
    public required string Path { get; set; }
}
