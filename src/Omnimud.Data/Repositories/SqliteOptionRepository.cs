using Dapper;
using Omnimud.Data.Entities;

namespace Omnimud.Data.Repositories;

public sealed class SqliteOptionRepository : IOptionRepository
{
    // Same expression as the unique index UX_Options_Scope_Key, so global rows (ScopeId NULL) are
    // protected against duplicates and the lookups use the index.
    private const string ScopeFilter = "Scope = @Scope AND COALESCE(ScopeId, 0) = COALESCE(@ScopeId, 0)";

    private const string UpsertSql = """
        INSERT INTO Options (Scope, ScopeId, Key, Value) VALUES (@Scope, @ScopeId, @Key, @Value)
        ON CONFLICT (Scope, COALESCE(ScopeId, 0), Key) DO UPDATE SET Value = excluded.Value
        """;

    private readonly IDbConnectionFactory _factory;

    public SqliteOptionRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<OptionEntity>> GetByScope(int scope, int? scopeId, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        var result = await conn.QueryAsync<OptionEntity>(
            new CommandDefinition($"SELECT * FROM Options WHERE {ScopeFilter} ORDER BY Id", new { Scope = scope, ScopeId = scopeId }, cancellationToken: ct));
        return result.ToList();
    }

    public async Task<string?> GetValueAsync(int scope, int? scopeId, string key, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        return await conn.ExecuteScalarAsync<string?>(
            new CommandDefinition($"SELECT Value FROM Options WHERE {ScopeFilter} AND Key = @Key",
                new { Scope = scope, ScopeId = scopeId, Key = key }, cancellationToken: ct));
    }

    public async Task SetValueAsync(int scope, int? scopeId, string key, string value, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        await conn.ExecuteAsync(new CommandDefinition(UpsertSql,
            new { Scope = scope, ScopeId = Normalize(scope, scopeId), Key = key, Value = value }, cancellationToken: ct));
    }

    public async Task DeleteAsync(int scope, int? scopeId, string key, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        await conn.ExecuteAsync(new CommandDefinition($"DELETE FROM Options WHERE {ScopeFilter} AND Key = @Key",
            new { Scope = scope, ScopeId = scopeId, Key = key }, cancellationToken: ct));
    }

    public async Task DeleteAllByScopeAsync(int scope, int? scopeId, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        await conn.ExecuteAsync(new CommandDefinition($"DELETE FROM Options WHERE {ScopeFilter}",
            new { Scope = scope, ScopeId = scopeId }, cancellationToken: ct));
    }

    public async Task<bool> HasAnyAsync(int scope, int? scopeId, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            $"SELECT EXISTS (SELECT 1 FROM Options WHERE {ScopeFilter})",
            new { Scope = scope, ScopeId = scopeId }, cancellationToken: ct)) != 0;
    }

    public async Task ReplaceAllAsync(int scope, int? scopeId, IReadOnlyDictionary<string, string> values, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(new CommandDefinition($"DELETE FROM Options WHERE {ScopeFilter}",
            new { Scope = scope, ScopeId = scopeId }, tx, cancellationToken: ct));
        foreach (var (key, value) in values)
        {
            await conn.ExecuteAsync(new CommandDefinition(UpsertSql,
                new { Scope = scope, ScopeId = Normalize(scope, scopeId), Key = key, Value = value }, tx, cancellationToken: ct));
        }
        tx.Commit();
    }

    /// <summary>Global rows are always stored with a NULL id, whatever the caller passed.</summary>
    private static int? Normalize(int scope, int? scopeId) => scope == 0 ? null : scopeId;
}
