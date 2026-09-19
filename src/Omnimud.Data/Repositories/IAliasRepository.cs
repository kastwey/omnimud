using Omnimud.Data.Entities;

namespace Omnimud.Data.Repositories;

public interface IAliasRepository
{
    Task<IReadOnlyList<AliasEntity>> GetByCharacterAsync(int characterId, CancellationToken ct = default);
    /// <exception cref="DuplicateEntityException">The character already has that command.</exception>
    Task<int> AddAsync(AliasEntity alias, CancellationToken ct = default);
    /// <exception cref="DuplicateEntityException">The character already has that command.</exception>
    Task UpdateAsync(AliasEntity alias, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    /// <summary>Case-insensitive delete kept for existing callers.</summary>
    Task DeleteByCommandAsync(int characterId, string command, CancellationToken ct = default);
    /// <summary>Exact (case-sensitive, as aliases are matched) delete. Returns false when it did not exist.</summary>
    Task<bool> RemoveByCommandAsync(int characterId, string command, CancellationToken ct = default);
}
