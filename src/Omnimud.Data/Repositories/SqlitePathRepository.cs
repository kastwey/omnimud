using Dapper;
using Omnimud.Data.Entities;

namespace Omnimud.Data.Repositories;

public sealed class SqlitePathRepository : IPathRepository
{
    private readonly IDbConnectionFactory _factory;

    public SqlitePathRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<PathEntity>> GetByCharacterAsync(int characterId, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        var result = await conn.QueryAsync<PathEntity>(
            new CommandDefinition("SELECT * FROM Paths WHERE CharacterId = @CharacterId ORDER BY Name",
                new { CharacterId = characterId }, cancellationToken: ct));
        return result.ToList();
    }

    public Task<int> AddAsync(PathEntity path, CancellationToken ct = default) =>
        SqliteErrors.GuardAsync("Path", path.Name, async () =>
        {
            using var conn = _factory.Create();
            const string sql = """
                INSERT INTO Paths (CharacterId, Name, Path)
                VALUES (@CharacterId, @Name, @Path);
                SELECT last_insert_rowid();
                """;
            return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, path, cancellationToken: ct));
        });

    public Task UpdateAsync(PathEntity path, CancellationToken ct = default) =>
        SqliteErrors.GuardAsync("Path", path.Name, async () =>
        {
            using var conn = _factory.Create();
            const string sql = "UPDATE Paths SET Name = @Name, Path = @Path WHERE Id = @Id";
            await conn.ExecuteAsync(new CommandDefinition(sql, path, cancellationToken: ct));
        });

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        await conn.ExecuteAsync(new CommandDefinition("DELETE FROM Paths WHERE Id = @Id", new { Id = id }, cancellationToken: ct));
    }
}
