using Omnimud.Data.Entities;

namespace Omnimud.Data.Repositories;

/// <summary>
/// Raw key/value storage of options. Scope: 0 global, 1 MUD, 2 character. For the global scope the
/// id is irrelevant: null and 0 are the same row set.
/// </summary>
public interface IOptionRepository
{
    Task<IReadOnlyList<OptionEntity>> GetByScope(int scope, int? scopeId, CancellationToken ct = default);
    Task<string?> GetValueAsync(int scope, int? scopeId, string key, CancellationToken ct = default);
    /// <summary>Atomic upsert.</summary>
    Task SetValueAsync(int scope, int? scopeId, string key, string value, CancellationToken ct = default);
    Task DeleteAsync(int scope, int? scopeId, string key, CancellationToken ct = default);
    Task DeleteAllByScopeAsync(int scope, int? scopeId, CancellationToken ct = default);
    Task<bool> HasAnyAsync(int scope, int? scopeId, CancellationToken ct = default);
    /// <summary>Replaces every row of the scope with <paramref name="values"/> in one transaction.</summary>
    Task ReplaceAllAsync(int scope, int? scopeId, IReadOnlyDictionary<string, string> values, CancellationToken ct = default);
}
