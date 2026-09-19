using Omnimud.Data.Entities;

namespace Omnimud.Data.Repositories;

public interface IMudRepository
{
    Task<IReadOnlyList<MudEntity>> GetAllAsync(CancellationToken ct = default);
    Task<MudEntity?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<MudEntity?> GetByNameAsync(string name, CancellationToken ct = default);
    /// <exception cref="DuplicateEntityException">Another MUD has that name.</exception>
    Task<int> AddAsync(MudEntity mud, CancellationToken ct = default);
    /// <summary>Persists every column. DefaultCharacterId is validated against the MUD's characters and mirrored to Characters.IsDefault.</summary>
    /// <exception cref="DuplicateEntityException">Another MUD has that name.</exception>
    Task UpdateAsync(MudEntity mud, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task SetMovementModeAsync(int id, bool enabled, CancellationToken ct = default);
    Task SetMessageRuleSetAsync(int id, int? ruleSetId, CancellationToken ct = default);
}
