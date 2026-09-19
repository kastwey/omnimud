using Omnimud.Data.Entities;

namespace Omnimud.Data.Repositories;

public interface ITriggerRepository
{
    /// <summary>In creation order (SortOrder): position N in this list is the N of "-trigger N". The engine sorts by priority itself.</summary>
    Task<IReadOnlyList<TriggerEntity>> GetByCharacterAsync(int characterId, CancellationToken ct = default);
    Task<TriggerEntity?> GetByIdAsync(string id, CancellationToken ct = default);
    /// <summary>SortOrder 0 (or negative) appends at the end and the assigned value is written back to the entity.</summary>
    /// <exception cref="DuplicateEntityException">A trigger with that Id already exists.</exception>
    Task AddAsync(TriggerEntity trigger, CancellationToken ct = default);
    Task UpdateAsync(TriggerEntity trigger, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task SetEnabledAsync(string id, bool enabled, CancellationToken ct = default);
    /// <summary>Renumbers SortOrder following <paramref name="triggerIdsInOrder"/>; triggers not listed keep their relative order after them.</summary>
    Task ReorderAsync(int characterId, IReadOnlyList<string> triggerIdsInOrder, CancellationToken ct = default);
}
