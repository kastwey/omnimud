using System.Data;
using Dapper;
using Omnimud.Data.Entities;

namespace Omnimud.Data.Repositories;

public sealed class SqliteCharacterRepository : ICharacterRepository
{
    private const string SetDefaultSql = """
        UPDATE Characters SET IsDefault = CASE WHEN Id = @CharacterId THEN 1 ELSE 0 END WHERE MudId = @MudId;
        UPDATE Muds SET DefaultCharacterId = (SELECT c.Id FROM Characters c WHERE c.Id = @CharacterId AND c.MudId = @MudId)
        WHERE Id = @MudId;
        """;

    private readonly IDbConnectionFactory _factory;

    public SqliteCharacterRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<CharacterEntity>> GetByMudAsync(int mudId, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        var result = await conn.QueryAsync<CharacterEntity>(
            new CommandDefinition("SELECT * FROM Characters WHERE MudId = @MudId ORDER BY Name COLLATE NOCASE", new { MudId = mudId }, cancellationToken: ct));
        return result.ToList();
    }

    public async Task<CharacterEntity?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        return await conn.QuerySingleOrDefaultAsync<CharacterEntity>(
            new CommandDefinition("SELECT * FROM Characters WHERE Id = @Id", new { Id = id }, cancellationToken: ct));
    }

    public Task<int> AddAsync(CharacterEntity character, CancellationToken ct = default) =>
        SqliteErrors.GuardAsync("Character", character.Name, async () =>
        {
            using var conn = _factory.Create();
            using var tx = conn.BeginTransaction();
            const string sql = """
                INSERT INTO Characters (MudId, Name, EncryptedPassword, IsDefault, MovementMode, CreatedAt, UpdatedAt)
                VALUES (@MudId, @Name, @EncryptedPassword, 0, @MovementMode, datetime('now'), datetime('now'));
                SELECT last_insert_rowid();
                """;
            var id = await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, character, tx, cancellationToken: ct));
            if (character.IsDefault)
                await SetDefaultCoreAsync(conn, tx, character.MudId, id, ct);
            tx.Commit();
            return id;
        });

    public Task UpdateAsync(CharacterEntity character, CancellationToken ct = default) =>
        SqliteErrors.GuardAsync("Character", character.Name, async () =>
        {
            using var conn = _factory.Create();
            using var tx = conn.BeginTransaction();
            const string sql = """
                UPDATE Characters SET Name = @Name, EncryptedPassword = @EncryptedPassword,
                    MovementMode = @MovementMode, UpdatedAt = datetime('now')
                WHERE Id = @Id
                """;
            await conn.ExecuteAsync(new CommandDefinition(sql, character, tx, cancellationToken: ct));

            // The MUD is read from the row: the entity may carry a stale or missing MudId.
            var mudId = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
                "SELECT MudId FROM Characters WHERE Id = @Id", new { character.Id }, tx, cancellationToken: ct));
            if (mudId is not null)
            {
                if (character.IsDefault)
                {
                    await SetDefaultCoreAsync(conn, tx, mudId.Value, character.Id, ct);
                }
                else
                {
                    await conn.ExecuteAsync(new CommandDefinition(
                        """
                        UPDATE Characters SET IsDefault = 0 WHERE Id = @Id;
                        UPDATE Muds SET DefaultCharacterId = NULL WHERE Id = @MudId AND DefaultCharacterId = @Id;
                        """,
                        new { character.Id, MudId = mudId.Value }, tx, cancellationToken: ct));
                }
            }
            tx.Commit();
        });

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        // Muds.DefaultCharacterId is cleared by its ON DELETE SET NULL foreign key.
        await conn.ExecuteAsync(new CommandDefinition("DELETE FROM Characters WHERE Id = @Id", new { Id = id }, cancellationToken: ct));
    }

    public async Task SetDefaultAsync(int mudId, int characterId, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        await SetDefaultCoreAsync(conn, tx, mudId, characterId, ct);
        tx.Commit();
    }

    public async Task ClearDefaultAsync(int mudId, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE Characters SET IsDefault = 0 WHERE MudId = @MudId;
            UPDATE Muds SET DefaultCharacterId = NULL WHERE Id = @MudId;
            """,
            new { MudId = mudId }, tx, cancellationToken: ct));
        tx.Commit();
    }

    public async Task SetMovementModeAsync(int id, bool enabled, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE Characters SET MovementMode = @Enabled WHERE Id = @Id", new { Id = id, Enabled = enabled }, cancellationToken: ct));
    }

    private static Task SetDefaultCoreAsync(IDbConnection conn, IDbTransaction tx, int mudId, int characterId, CancellationToken ct) =>
        conn.ExecuteAsync(new CommandDefinition(SetDefaultSql, new { MudId = mudId, CharacterId = characterId }, tx, cancellationToken: ct));
}
