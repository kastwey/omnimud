namespace Omnimud.Data.Entities;

public sealed class OptionEntity
{
    public int Id { get; set; }
    public int Scope { get; set; }
    public int? ScopeId { get; set; }
    public required string Key { get; set; }
    public required string Value { get; set; }
}
