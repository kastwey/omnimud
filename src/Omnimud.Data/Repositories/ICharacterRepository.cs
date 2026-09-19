using Omnimud.Data.Entities;

namespace Omnimud.Data.Repositories;

public interface ICharacterRepository
{
    Task<IReadOnlyList<CharacterEntity>> GetByMudAsync(int mudId, CancellationToken ct = default);
    Task<CharacterEntity?> GetByIdAsync(int id, CancellationToken ct = default);
    /// <exception cref="DuplicateEntityException">The MUD already has a character with that name.</exception>
    Task<int> AddAsync(CharacterEntity character, CancellationToken ct = default);
    /// <summary>Persists every column; IsDefault is mirrored to Muds.DefaultCharacterId.</summary>
    /// <exception cref="DuplicateEntityException">The MUD already has a character with that name.</exception>
    Task UpdateAsync(CharacterEntity character, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    /// <summary>Atomically makes the character the only default of its MUD (Characters.IsDefault and Muds.DefaultCharacterId).</summary>
    Task SetDefaultAsync(int mudId, int characterId, CancellationToken ct = default);
    /// <summary>Leaves the MUD without a default character.</summary>
    Task ClearDefaultAsync(int mudId, CancellationToken ct = default);
    Task SetMovementModeAsync(int id, bool enabled, CancellationToken ct = default);
}
