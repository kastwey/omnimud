using Dapper;
using Omnimud.Data.Entities;

namespace Omnimud.Data.Repositories;

public sealed class SqliteTriggerRepository : ITriggerRepository
{
    private readonly IDbConnectionFactory _factory;

    public SqliteTriggerRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<TriggerEntity>> GetByCharacterAsync(int characterId, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        var result = await conn.QueryAsync<TriggerEntity>(
            new CommandDefinition("SELECT * FROM Triggers WHERE CharacterId = @CharacterId ORDER BY SortOrder, CreatedAt, rowid",
                new { CharacterId = characterId }, cancellationToken: ct));
        return result.ToList();
    }

    public async Task<TriggerEntity?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        return await conn.QuerySingleOrDefaultAsync<TriggerEntity>(
            new CommandDefinition("SELECT * FROM Triggers WHERE Id = @Id", new { Id = id }, cancellationToken: ct));
    }

    public Task AddAsync(TriggerEntity trigger, CancellationToken ct = default) =>
        SqliteErrors.GuardAsync("Trigger", trigger.Id, async () =>
        {
            using var conn = _factory.Create();
            using var tx = conn.BeginTransaction();
            if (trigger.SortOrder <= 0)
            {
                trigger.SortOrder = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT COALESCE(MAX(SortOrder), 0) + 1 FROM Triggers WHERE CharacterId = @CharacterId",
                    new { trigger.CharacterId }, tx, cancellationToken: ct));
            }

            const string sql = """
                INSERT INTO Triggers (Id, CharacterId, Name, Pattern, PatternType, Action, ActionType, Sound, Enabled,
                                      CaseSensitive, Priority, Multiline, GagLine, SortOrder, CreatedAt, UpdatedAt)
                VALUES (@Id, @CharacterId, @Name, @Pattern, @PatternType, @Action, @ActionType, @Sound, @Enabled,
                        @CaseSensitive, @Priority, @Multiline, @GagLine, @SortOrder, datetime('now'), datetime('now'))
                """;
            await conn.ExecuteAsync(new CommandDefinition(sql, trigger, tx, cancellationToken: ct));
            tx.Commit();
        });

    public async Task UpdateAsync(TriggerEntity trigger, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        // SortOrder 0 means "not set by the caller" (forms built before the column existed): keep the stored one.
        const string sql = """
            UPDATE Triggers SET Name = @Name, Pattern = @Pattern, PatternType = @PatternType,
                Action = @Action, ActionType = @ActionType, Sound = @Sound, Enabled = @Enabled,
                CaseSensitive = @CaseSensitive, Priority = @Priority, Multiline = @Multiline, GagLine = @GagLine,
                SortOrder = CASE WHEN @SortOrder > 0 THEN @SortOrder ELSE SortOrder END,
                UpdatedAt = datetime('now')
            WHERE Id = @Id
            """;
        await conn.ExecuteAsync(new CommandDefinition(sql, trigger, cancellationToken: ct));
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        await conn.ExecuteAsync(new CommandDefinition("DELETE FROM Triggers WHERE Id = @Id", new { Id = id }, cancellationToken: ct));
    }

    public async Task SetEnabledAsync(string id, bool enabled, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE Triggers SET Enabled = @Enabled, UpdatedAt = datetime('now') WHERE Id = @Id",
            new { Id = id, Enabled = enabled }, cancellationToken: ct));
    }

    public async Task ReorderAsync(int characterId, IReadOnlyList<string> triggerIdsInOrder, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        var current = (await conn.QueryAsync<string>(new CommandDefinition(
            "SELECT Id FROM Triggers WHERE CharacterId = @CharacterId ORDER BY SortOrder, CreatedAt, rowid",
            new { CharacterId = characterId }, tx, cancellationToken: ct))).ToList();

        var known = current.ToHashSet(StringComparer.Ordinal);
        var ordered = triggerIdsInOrder.Where(known.Contains).Distinct(StringComparer.Ordinal).ToList();
        var listed = ordered.ToHashSet(StringComparer.Ordinal);
        ordered.AddRange(current.Where(id => !listed.Contains(id)));

        for (var i = 0; i < ordered.Count; i++)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE Triggers SET SortOrder = @SortOrder WHERE Id = @Id",
                new { Id = ordered[i], SortOrder = i + 1 }, tx, cancellationToken: ct));
        }
        tx.Commit();
    }
}
