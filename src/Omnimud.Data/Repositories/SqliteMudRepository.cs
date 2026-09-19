using Dapper;
using Omnimud.Data.Entities;

namespace Omnimud.Data.Repositories;

public sealed class SqliteMudRepository : IMudRepository
{
    private readonly IDbConnectionFactory _factory;

    public SqliteMudRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<MudEntity>> GetAllAsync(CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        var result = await conn.QueryAsync<MudEntity>(
            new CommandDefinition("SELECT * FROM Muds ORDER BY Name COLLATE NOCASE", cancellationToken: ct));
        return result.ToList();
    }

    public async Task<MudEntity?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        return await conn.QuerySingleOrDefaultAsync<MudEntity>(
            new CommandDefinition("SELECT * FROM Muds WHERE Id = @Id", new { Id = id }, cancellationToken: ct));
    }

    public async Task<MudEntity?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        return await conn.QueryFirstOrDefaultAsync<MudEntity>(
            new CommandDefinition("SELECT * FROM Muds WHERE Name = @Name COLLATE NOCASE ORDER BY Id", new { Name = name }, cancellationToken: ct));
    }

    public Task<int> AddAsync(MudEntity mud, CancellationToken ct = default) =>
        SqliteErrors.GuardAsync("Mud", mud.Name, async () =>
        {
            using var conn = _factory.Create();
            const string sql = """
                INSERT INTO Muds (Name, Host, Port, UseTls, ValidateCertificate, SaveCommand, QuitCommand, LoginScript,
                                  ProcessRule, MessageRuleSetId, SoundDirectory, Encoding, MovementMode, DefaultCharacterId,
                                  CreatedAt, UpdatedAt)
                VALUES (@Name, @Host, @Port, @UseTls, @ValidateCertificate, @SaveCommand, @QuitCommand, @LoginScript,
                        @ProcessRule, @MessageRuleSetId, @SoundDirectory, @Encoding, @MovementMode, @DefaultCharacterId,
                        datetime('now'), datetime('now'));
                SELECT last_insert_rowid();
                """;
            return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, mud, cancellationToken: ct));
        });

    public Task UpdateAsync(MudEntity mud, CancellationToken ct = default) =>
        SqliteErrors.GuardAsync("Mud", mud.Name, async () =>
        {
            using var conn = _factory.Create();
            using var tx = conn.BeginTransaction();
            const string sql = """
                UPDATE Muds SET Name = @Name, Host = @Host, Port = @Port, UseTls = @UseTls,
                    ValidateCertificate = @ValidateCertificate,
                    SaveCommand = @SaveCommand, QuitCommand = @QuitCommand, LoginScript = @LoginScript,
                    ProcessRule = @ProcessRule, MessageRuleSetId = @MessageRuleSetId,
                    SoundDirectory = @SoundDirectory, Encoding = @Encoding, MovementMode = @MovementMode,
                    DefaultCharacterId = (SELECT c.Id FROM Characters c WHERE c.Id = @DefaultCharacterId AND c.MudId = @Id),
                    UpdatedAt = datetime('now')
                WHERE Id = @Id;

                UPDATE Characters
                SET IsDefault = CASE WHEN Id = (SELECT DefaultCharacterId FROM Muds WHERE Id = @Id) THEN 1 ELSE 0 END
                WHERE MudId = @Id;
                """;
            await conn.ExecuteAsync(new CommandDefinition(sql, mud, tx, cancellationToken: ct));
            tx.Commit();
        });

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        await conn.ExecuteAsync(new CommandDefinition("DELETE FROM Muds WHERE Id = @Id", new { Id = id }, cancellationToken: ct));
    }

    public async Task SetMovementModeAsync(int id, bool enabled, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE Muds SET MovementMode = @Enabled WHERE Id = @Id", new { Id = id, Enabled = enabled }, cancellationToken: ct));
    }

    public async Task SetMessageRuleSetAsync(int id, int? ruleSetId, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE Muds SET MessageRuleSetId = @RuleSetId, UpdatedAt = datetime('now') WHERE Id = @Id",
            new { Id = id, RuleSetId = ruleSetId }, cancellationToken: ct));
    }
}
