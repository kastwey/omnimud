using Omnimud.Data.Entities;

namespace Omnimud.Data.Repositories;

/// <summary>Movement keys (codes 0-17, see Omnimud.Core.Session.MovementKey) bound to commands, owned by a MUD or by a character.</summary>
public interface IMovementRepository
{
    /// <summary>The MUD's own movements (not those of its characters).</summary>
    Task<IReadOnlyList<MovementEntity>> GetByMudAsync(int mudId, CancellationToken ct = default);
    Task<IReadOnlyList<MovementEntity>> GetByCharacterAsync(int characterId, CancellationToken ct = default);
    /// <exception cref="DuplicateEntityException">The owner already has that key.</exception>
    /// <exception cref="ArgumentException">Not exactly one owner, or key outside 0-17.</exception>
    Task<int> AddAsync(MovementEntity movement, CancellationToken ct = default);
    /// <exception cref="DuplicateEntityException">The owner already has that key.</exception>
    Task UpdateAsync(MovementEntity movement, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    /// <summary>Insert or update the command of one key. Exactly one of the owners must be given.</summary>
    Task SetAsync(int? mudId, int? characterId, int keyCode, string command, CancellationToken ct = default);
    /// <summary>Replaces every movement of the owner in one transaction. An empty map leaves it without movements (a character then inherits the MUD's).</summary>
    Task ReplaceAllAsync(int? mudId, int? characterId, IReadOnlyDictionary<int, string> commandsByKey, CancellationToken ct = default);
}
