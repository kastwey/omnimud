using Omnimud.Data.Entities;

namespace Omnimud.Data.Repositories;

public interface IPathRepository
{
    Task<IReadOnlyList<PathEntity>> GetByCharacterAsync(int characterId, CancellationToken ct = default);
    /// <exception cref="DuplicateEntityException">The character already has a path with that name.</exception>
    Task<int> AddAsync(PathEntity path, CancellationToken ct = default);
    /// <exception cref="DuplicateEntityException">The character already has a path with that name.</exception>
    Task UpdateAsync(PathEntity path, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
