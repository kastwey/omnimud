using Dapper;
using Omnimud.Data.Entities;

namespace Omnimud.Data.Repositories;

public sealed class SqliteMessageRuleRepository : IMessageRuleRepository
{
    private const string InsertRuleSql = """
        INSERT INTO MessageRules (RuleSetId, SortOrder, Pattern, Template, CaseSensitive, Channel, Enabled)
        VALUES (@RuleSetId, @SortOrder, @Pattern, @Template, @CaseSensitive, @Channel, @Enabled);
        SELECT last_insert_rowid();
        """;

    private readonly IDbConnectionFactory _factory;

    public SqliteMessageRuleRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <summary>A blank script is stored as NULL: the set then works with patterns.</summary>
    private static string? NormalizeScript(string? script) => string.IsNullOrWhiteSpace(script) ? null : script;

    public async Task<IReadOnlyList<MessageRuleSetEntity>> GetRuleSetsAsync(CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        var result = await conn.QueryAsync<MessageRuleSetEntity>(
            new CommandDefinition("SELECT * FROM MessageRuleSets ORDER BY Name COLLATE NOCASE", cancellationToken: ct));
        return result.ToList();
    }

    public async Task<MessageRuleSetEntity?> GetRuleSetByIdAsync(int id, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        return await conn.QuerySingleOrDefaultAsync<MessageRuleSetEntity>(
            new CommandDefinition("SELECT * FROM MessageRuleSets WHERE Id = @Id", new { Id = id }, cancellationToken: ct));
    }

    public async Task<MessageRuleSetEntity?> GetRuleSetByNameAsync(string name, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        return await conn.QuerySingleOrDefaultAsync<MessageRuleSetEntity>(
            new CommandDefinition("SELECT * FROM MessageRuleSets WHERE Name = @Name COLLATE NOCASE", new { Name = name }, cancellationToken: ct));
    }

    public Task<int> AddRuleSetAsync(MessageRuleSetEntity ruleSet, CancellationToken ct = default) =>
        SqliteErrors.GuardAsync("MessageRuleSet", ruleSet.Name, async () =>
        {
            using var conn = _factory.Create();
            return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                "INSERT INTO MessageRuleSets (Name, IsBuiltIn, Script) VALUES (@Name, @IsBuiltIn, @Script); SELECT last_insert_rowid();",
                new { ruleSet.Name, ruleSet.IsBuiltIn, Script = NormalizeScript(ruleSet.Script) }, cancellationToken: ct));
        });

    public Task UpdateRuleSetAsync(MessageRuleSetEntity ruleSet, CancellationToken ct = default) =>
        SqliteErrors.GuardAsync("MessageRuleSet", ruleSet.Name, async () =>
        {
            using var conn = _factory.Create();
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE MessageRuleSets SET Name = @Name, IsBuiltIn = @IsBuiltIn, Script = @Script WHERE Id = @Id",
                new { ruleSet.Id, ruleSet.Name, ruleSet.IsBuiltIn, Script = NormalizeScript(ruleSet.Script) }, cancellationToken: ct));
        });

    public async Task DeleteRuleSetAsync(int id, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        await conn.ExecuteAsync(new CommandDefinition("DELETE FROM MessageRuleSets WHERE Id = @Id", new { Id = id }, cancellationToken: ct));
    }

    public Task<int> DuplicateRuleSetAsync(int sourceRuleSetId, string newName, CancellationToken ct = default) =>
        SqliteErrors.GuardAsync("MessageRuleSet", newName, async () =>
        {
            using var conn = _factory.Create();
            using var tx = conn.BeginTransaction();
            var newId = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                INSERT INTO MessageRuleSets (Name, IsBuiltIn, Script)
                SELECT @Name, 0, Script FROM MessageRuleSets WHERE Id = @SourceId;
                SELECT last_insert_rowid();
                """,
                new { Name = newName, SourceId = sourceRuleSetId }, tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO MessageRules (RuleSetId, SortOrder, Pattern, Template, CaseSensitive, Channel, Enabled)
                SELECT @NewId, SortOrder, Pattern, Template, CaseSensitive, Channel, Enabled
                FROM MessageRules WHERE RuleSetId = @SourceId ORDER BY SortOrder, Id
                """,
                new { NewId = newId, SourceId = sourceRuleSetId }, tx, cancellationToken: ct));
            tx.Commit();
            return newId;
        });

    public async Task<IReadOnlyList<MessageRuleEntity>> GetRulesAsync(int ruleSetId, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        var result = await conn.QueryAsync<MessageRuleEntity>(
            new CommandDefinition("SELECT * FROM MessageRules WHERE RuleSetId = @RuleSetId ORDER BY SortOrder, Id",
                new { RuleSetId = ruleSetId }, cancellationToken: ct));
        return result.ToList();
    }

    public async Task<int> AddRuleAsync(MessageRuleEntity rule, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        if (rule.SortOrder <= 0)
        {
            rule.SortOrder = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COALESCE(MAX(SortOrder), 0) + 1 FROM MessageRules WHERE RuleSetId = @RuleSetId",
                new { rule.RuleSetId }, tx, cancellationToken: ct));
        }
        var id = await conn.ExecuteScalarAsync<int>(new CommandDefinition(InsertRuleSql, rule, tx, cancellationToken: ct));
        tx.Commit();
        return id;
    }

    public async Task UpdateRuleAsync(MessageRuleEntity rule, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        const string sql = """
            UPDATE MessageRules SET SortOrder = @SortOrder, Pattern = @Pattern, Template = @Template,
                CaseSensitive = @CaseSensitive, Channel = @Channel, Enabled = @Enabled
            WHERE Id = @Id
            """;
        await conn.ExecuteAsync(new CommandDefinition(sql, rule, cancellationToken: ct));
    }

    public async Task DeleteRuleAsync(int id, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        await conn.ExecuteAsync(new CommandDefinition("DELETE FROM MessageRules WHERE Id = @Id", new { Id = id }, cancellationToken: ct));
    }

    public async Task ReplaceRulesAsync(int ruleSetId, IReadOnlyList<MessageRuleEntity> rules, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(new CommandDefinition("DELETE FROM MessageRules WHERE RuleSetId = @RuleSetId",
            new { RuleSetId = ruleSetId }, tx, cancellationToken: ct));
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            rule.RuleSetId = ruleSetId;
            rule.SortOrder = i + 1;
            rule.Id = await conn.ExecuteScalarAsync<int>(new CommandDefinition(InsertRuleSql, rule, tx, cancellationToken: ct));
        }
        tx.Commit();
    }
}
