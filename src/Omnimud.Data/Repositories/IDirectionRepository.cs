using Omnimud.Data.Entities;

namespace Omnimud.Data.Repositories;

/// <summary>Direction dictionary of each MUD (full name, one-character abbreviation, opposite).</summary>
public interface IDirectionRepository
{
    Task<IReadOnlyList<DirectionEntity>> GetByMudAsync(int mudId, CancellationToken ct = default);
    /// <exception cref="DuplicateEntityException">The MUD already has that direction or that abbreviation.</exception>
    /// <exception cref="ArgumentException">The abbreviation is not exactly one character.</exception>
    Task<int> AddAsync(DirectionEntity direction, CancellationToken ct = default);
    /// <exception cref="DuplicateEntityException">The MUD already has that direction or that abbreviation.</exception>
    /// <exception cref="ArgumentException">The abbreviation is not exactly one character.</exception>
    Task UpdateAsync(DirectionEntity direction, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    /// <summary>Replaces the whole dictionary of the MUD in one transaction.</summary>
    Task ReplaceAllAsync(int mudId, IReadOnlyList<DirectionEntity> directions, CancellationToken ct = default);
}
