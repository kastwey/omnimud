namespace Omnimud.Data.Entities;

public sealed class MessageRuleSetEntity
{
    public int Id { get; set; }
    public required string Name { get; set; }
    /// <summary>True for the four sets seeded from the original client.</summary>
    public bool IsBuiltIn { get; set; }
    /// <summary>
    /// Lua script (Omnimud.Core.Messages.MessageRuleScript). Not null and not blank = the set is of
    /// type "script": the script decides the messages and the pattern rules are not evaluated.
    /// </summary>
    public string? Script { get; set; }

    public bool IsScript => !string.IsNullOrWhiteSpace(Script);
}
