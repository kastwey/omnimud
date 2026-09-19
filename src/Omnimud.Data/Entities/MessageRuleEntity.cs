namespace Omnimud.Data.Entities;

public sealed class MessageRuleEntity
{
    public int Id { get; set; }
    public int RuleSetId { get; set; }
    /// <summary>Evaluation order. 0 on insert = append.</summary>
    public int SortOrder { get; set; }
    public required string Pattern { get; set; }
    public string Template { get; set; } = "$0";
    public bool CaseSensitive { get; set; }
    public string? Channel { get; set; }
    public bool Enabled { get; set; } = true;
}
