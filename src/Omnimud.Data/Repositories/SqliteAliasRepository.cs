using Dapper;
using Omnimud.Data.Entities;

namespace Omnimud.Data.Repositories;

public sealed class SqliteAliasRepository : IAliasRepository
{
    private readonly IDbConnectionFactory _factory;

    public SqliteAliasRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<AliasEntity>> GetByCharacterAsync(int characterId, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        var result = await conn.QueryAsync<AliasEntity>(
            new CommandDefinition("SELECT * FROM Aliases WHERE CharacterId = @CharacterId ORDER BY Command",
                new { CharacterId = characterId }, cancellationToken: ct));
        return result.ToList();
    }

    public Task<int> AddAsync(AliasEntity alias, CancellationToken ct = default) =>
        SqliteErrors.GuardAsync("Alias", alias.Command, async () =>
        {
            using var conn = _factory.Create();
            const string sql = """
                INSERT INTO Aliases (CharacterId, Command, Action, Enabled)
                VALUES (@CharacterId, @Command, @Action, @Enabled);
                SELECT last_insert_rowid();
                """;
            return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, alias, cancellationToken: ct));
        });

    public Task UpdateAsync(AliasEntity alias, CancellationToken ct = default) =>
        SqliteErrors.GuardAsync("Alias", alias.Command, async () =>
        {
            using var conn = _factory.Create();
            const string sql = "UPDATE Aliases SET Command = @Command, Action = @Action, Enabled = @Enabled WHERE Id = @Id";
            await conn.ExecuteAsync(new CommandDefinition(sql, alias, cancellationToken: ct));
        });

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        await conn.ExecuteAsync(new CommandDefinition("DELETE FROM Aliases WHERE Id = @Id", new { Id = id }, cancellationToken: ct));
    }

    public async Task DeleteByCommandAsync(int characterId, string command, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM Aliases WHERE CharacterId = @CharacterId AND Command = @Command COLLATE NOCASE",
            new { CharacterId = characterId, Command = command }, cancellationToken: ct));
    }

    public async Task<bool> RemoveByCommandAsync(int characterId, string command, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM Aliases WHERE CharacterId = @CharacterId AND Command = @Command",
            new { CharacterId = characterId, Command = command }, cancellationToken: ct));
        return affected > 0;
    }
}
