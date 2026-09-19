using Omnimud.Data.Entities;

namespace Omnimud.Data.Repositories;

/// <summary>Message rule sets (a MUD picks one or none) and their ordered regex rules.</summary>
public interface IMessageRuleRepository
{
    Task<IReadOnlyList<MessageRuleSetEntity>> GetRuleSetsAsync(CancellationToken ct = default);
    Task<MessageRuleSetEntity?> GetRuleSetByIdAsync(int id, CancellationToken ct = default);
    /// <summary>Case-insensitive.</summary>
    Task<MessageRuleSetEntity?> GetRuleSetByNameAsync(string name, CancellationToken ct = default);
    /// <exception cref="DuplicateEntityException">Another set has that name (case-insensitive).</exception>
    Task<int> AddRuleSetAsync(MessageRuleSetEntity ruleSet, CancellationToken ct = default);
    /// <exception cref="DuplicateEntityException">Another set has that name (case-insensitive).</exception>
    Task UpdateRuleSetAsync(MessageRuleSetEntity ruleSet, CancellationToken ct = default);
    /// <summary>Deletes the set and its rules; MUDs that used it are left without rule set.</summary>
    Task DeleteRuleSetAsync(int id, CancellationToken ct = default);
    /// <summary>Copies a set and its rules under a new name (never built-in). Returns the new id.</summary>
    /// <exception cref="DuplicateEntityException">Another set has that name.</exception>
    Task<int> DuplicateRuleSetAsync(int sourceRuleSetId, string newName, CancellationToken ct = default);

    /// <summary>Every rule of the set, enabled or not, in evaluation order.</summary>
    Task<IReadOnlyList<MessageRuleEntity>> GetRulesAsync(int ruleSetId, CancellationToken ct = default);
    /// <summary>SortOrder 0 (or negative) appends at the end and the assigned value is written back to the entity.</summary>
    Task<int> AddRuleAsync(MessageRuleEntity rule, CancellationToken ct = default);
    Task UpdateRuleAsync(MessageRuleEntity rule, CancellationToken ct = default);
    Task DeleteRuleAsync(int id, CancellationToken ct = default);
    /// <summary>Replaces the rules of the set in one transaction; SortOrder follows the list order.</summary>
    Task ReplaceRulesAsync(int ruleSetId, IReadOnlyList<MessageRuleEntity> rules, CancellationToken ct = default);
}
