using Dapper;
using Omnimud.Data.Entities;

namespace Omnimud.Data.Repositories;

public sealed class SqliteDirectionRepository : IDirectionRepository
{
    private const string InsertSql = """
        INSERT INTO Directions (MudId, Direction, Abbreviation, OppositeDirection)
        VALUES (@MudId, @Direction, @Abbreviation, @OppositeDirection);
        SELECT last_insert_rowid();
        """;

    private readonly IDbConnectionFactory _factory;

    public SqliteDirectionRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<DirectionEntity>> GetByMudAsync(int mudId, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        var result = await conn.QueryAsync<DirectionEntity>(
            new CommandDefinition("SELECT * FROM Directions WHERE MudId = @MudId ORDER BY Id", new { MudId = mudId }, cancellationToken: ct));
        return result.ToList();
    }

    public Task<int> AddAsync(DirectionEntity direction, CancellationToken ct = default)
    {
        Validate(direction);
        return SqliteErrors.GuardAsync("Direction", direction.Direction, async () =>
        {
            using var conn = _factory.Create();
            return await conn.ExecuteScalarAsync<int>(new CommandDefinition(InsertSql, direction, cancellationToken: ct));
        });
    }

    public Task UpdateAsync(DirectionEntity direction, CancellationToken ct = default)
    {
        Validate(direction);
        return SqliteErrors.GuardAsync("Direction", direction.Direction, async () =>
        {
            using var conn = _factory.Create();
            const string sql = "UPDATE Directions SET Direction = @Direction, Abbreviation = @Abbreviation, OppositeDirection = @OppositeDirection WHERE Id = @Id";
            await conn.ExecuteAsync(new CommandDefinition(sql, direction, cancellationToken: ct));
        });
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        await conn.ExecuteAsync(new CommandDefinition("DELETE FROM Directions WHERE Id = @Id", new { Id = id }, cancellationToken: ct));
    }

    public Task ReplaceAllAsync(int mudId, IReadOnlyList<DirectionEntity> directions, CancellationToken ct = default)
    {
        foreach (var direction in directions) Validate(direction);
        return SqliteErrors.GuardAsync("Direction", null, async () =>
        {
            using var conn = _factory.Create();
            using var tx = conn.BeginTransaction();
            await conn.ExecuteAsync(new CommandDefinition("DELETE FROM Directions WHERE MudId = @MudId", new { MudId = mudId }, tx, cancellationToken: ct));
            foreach (var direction in directions)
            {
                direction.MudId = mudId;
                direction.Id = await conn.ExecuteScalarAsync<int>(new CommandDefinition(InsertSql, direction, tx, cancellationToken: ct));
            }
            tx.Commit();
        });
    }

    private static void Validate(DirectionEntity direction)
    {
        if (string.IsNullOrEmpty(direction.Direction))
            throw new ArgumentException("The direction name cannot be empty.", nameof(direction));
        if (direction.Abbreviation is not { Length: 1 })
            throw new ArgumentException("The abbreviation must be exactly one character.", nameof(direction));
    }
}
