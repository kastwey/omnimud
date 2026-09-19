using Dapper;
using Omnimud.Data.Entities;

namespace Omnimud.Data.Repositories;

public sealed class SqliteMovementRepository : IMovementRepository
{
    private const string InsertSql = """
        INSERT INTO Movements (MudId, CharacterId, KeyCode, Command)
        VALUES (@MudId, @CharacterId, @KeyCode, @Command);
        SELECT last_insert_rowid();
        """;

    private readonly IDbConnectionFactory _factory;

    public SqliteMovementRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<MovementEntity>> GetByMudAsync(int mudId, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        var result = await conn.QueryAsync<MovementEntity>(
            new CommandDefinition("SELECT * FROM Movements WHERE MudId = @MudId ORDER BY KeyCode",
                new { MudId = mudId }, cancellationToken: ct));
        return result.ToList();
    }

    public async Task<IReadOnlyList<MovementEntity>> GetByCharacterAsync(int characterId, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        var result = await conn.QueryAsync<MovementEntity>(
            new CommandDefinition("SELECT * FROM Movements WHERE CharacterId = @CharacterId ORDER BY KeyCode",
                new { CharacterId = characterId }, cancellationToken: ct));
        return result.ToList();
    }

    public Task<int> AddAsync(MovementEntity movement, CancellationToken ct = default)
    {
        Validate(movement.MudId, movement.CharacterId, movement.KeyCode);
        return SqliteErrors.GuardAsync("Movement", movement.KeyCode.ToString(System.Globalization.CultureInfo.InvariantCulture), async () =>
        {
            using var conn = _factory.Create();
            return await conn.ExecuteScalarAsync<int>(new CommandDefinition(InsertSql, movement, cancellationToken: ct));
        });
    }

    public Task UpdateAsync(MovementEntity movement, CancellationToken ct = default)
    {
        ValidateKey(movement.KeyCode);
        return SqliteErrors.GuardAsync("Movement", movement.KeyCode.ToString(System.Globalization.CultureInfo.InvariantCulture), async () =>
        {
            using var conn = _factory.Create();
            // The owner of a movement never changes.
            const string sql = "UPDATE Movements SET KeyCode = @KeyCode, Command = @Command WHERE Id = @Id";
            await conn.ExecuteAsync(new CommandDefinition(sql, movement, cancellationToken: ct));
        });
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        await conn.ExecuteAsync(new CommandDefinition("DELETE FROM Movements WHERE Id = @Id", new { Id = id }, cancellationToken: ct));
    }

    public async Task SetAsync(int? mudId, int? characterId, int keyCode, string command, CancellationToken ct = default)
    {
        Validate(mudId, characterId, keyCode);
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        var args = new { MudId = mudId, CharacterId = characterId, KeyCode = keyCode, Command = command };
        var updated = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE Movements SET Command = @Command
            WHERE KeyCode = @KeyCode
              AND ((@MudId IS NOT NULL AND MudId = @MudId) OR (@CharacterId IS NOT NULL AND CharacterId = @CharacterId))
            """, args, tx, cancellationToken: ct));
        if (updated == 0)
            await conn.ExecuteScalarAsync<int>(new CommandDefinition(InsertSql, args, tx, cancellationToken: ct));
        tx.Commit();
    }

    public async Task ReplaceAllAsync(int? mudId, int? characterId, IReadOnlyDictionary<int, string> commandsByKey, CancellationToken ct = default)
    {
        ValidateOwner(mudId, characterId);
        foreach (var key in commandsByKey.Keys) ValidateKey(key);

        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM Movements WHERE (@MudId IS NOT NULL AND MudId = @MudId) OR (@CharacterId IS NOT NULL AND CharacterId = @CharacterId)",
            new { MudId = mudId, CharacterId = characterId }, tx, cancellationToken: ct));
        foreach (var (key, command) in commandsByKey.OrderBy(p => p.Key))
        {
            await conn.ExecuteScalarAsync<int>(new CommandDefinition(InsertSql,
                new { MudId = mudId, CharacterId = characterId, KeyCode = key, Command = command }, tx, cancellationToken: ct));
        }
        tx.Commit();
    }

    private static void Validate(int? mudId, int? characterId, int keyCode)
    {
        ValidateOwner(mudId, characterId);
        ValidateKey(keyCode);
    }

    private static void ValidateOwner(int? mudId, int? characterId)
    {
        if ((mudId is null) == (characterId is null))
            throw new ArgumentException("A movement belongs to a MUD or to a character: exactly one of them must be given.");
    }

    private static void ValidateKey(int keyCode)
    {
        if (!Omnimud.Core.Session.MovementKeys.IsValid(keyCode))
            throw new ArgumentException("The movement key must be a code between 0 and 17 (see Omnimud.Core.Session.MovementKey).", nameof(keyCode));
    }
}
