using System.Data.Common;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Omnimud.Data.Migrations;

public static class MigrationRunner
{
    /// <summary>Schema version produced by running every migration.</summary>
    public const int LatestVersion = 6;

    public static Task RunAsync(SqliteConnection connection, CancellationToken ct = default) =>
        RunAsync(connection, LatestVersion, ct);

    /// <summary>Applies pending migrations up to and including <paramref name="upToVersion"/>. Used by tests to build old schemas.</summary>
    internal static async Task RunAsync(SqliteConnection connection, int upToVersion, CancellationToken ct = default)
    {
        await EnsureSchemaVersionTableAsync(connection, ct).ConfigureAwait(false);
        var currentVersion = await GetCurrentVersionAsync(connection, ct).ConfigureAwait(false);

        foreach (var migration in GetMigrations()
                     .Where(m => m.Version > currentVersion && m.Version <= upToVersion)
                     .OrderBy(m => m.Version))
        {
            await ApplyAsync(connection, migration, ct).ConfigureAwait(false);
        }
    }

    private static async Task ApplyAsync(SqliteConnection connection, Migration migration, CancellationToken ct)
    {
        // Rebuilding a parent table (DROP + RENAME) with foreign keys enforced would cascade-delete
        // its children. The pragma is a no-op inside a transaction, so it is switched around it.
        var restoreForeignKeys = false;
        if (migration.RebuildsTables)
        {
            restoreForeignKeys = await connection.ExecuteScalarAsync<long>(
                new CommandDefinition("PRAGMA foreign_keys", cancellationToken: ct)).ConfigureAwait(false) != 0;
            if (restoreForeignKeys)
                await connection.ExecuteAsync(new CommandDefinition("PRAGMA foreign_keys=OFF", cancellationToken: ct)).ConfigureAwait(false);
        }

        try
        {
            await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await migration.Apply(connection, transaction, ct).ConfigureAwait(false);

            if (migration.RebuildsTables)
            {
                var violations = (await connection.QueryAsync(
                    new CommandDefinition("PRAGMA foreign_key_check", transaction: transaction, cancellationToken: ct)).ConfigureAwait(false)).Count();
                if (violations > 0)
                    throw new InvalidOperationException(
                        $"Migration {migration.Version} left {violations} foreign key violation(s); it has been rolled back.");
            }

            await connection.ExecuteAsync(
                new CommandDefinition(
                    "INSERT INTO SchemaVersion (Version, AppliedAt) VALUES (@Version, @AppliedAt)",
                    new { migration.Version, AppliedAt = DateTime.UtcNow.ToString("o") },
                    transaction: transaction,
                    cancellationToken: ct)).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            if (restoreForeignKeys)
                await connection.ExecuteAsync(new CommandDefinition("PRAGMA foreign_keys=ON", cancellationToken: CancellationToken.None)).ConfigureAwait(false);
        }
    }

    private static async Task EnsureSchemaVersionTableAsync(SqliteConnection connection, CancellationToken ct)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS SchemaVersion (
                Version INTEGER PRIMARY KEY,
                AppliedAt TEXT NOT NULL
            );
            """;
        await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false);
    }

    private static async Task<int> GetCurrentVersionAsync(SqliteConnection connection, CancellationToken ct)
    {
        const string sql = "SELECT COALESCE(MAX(Version), 0) FROM SchemaVersion";
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false);
    }

    private static IEnumerable<Migration> GetMigrations()
    {
        yield return Migration.FromSql(1, Migration001_InitialSchema.Sql);
        yield return Migration.FromSql(2, Migration002_AddMudEncoding.Sql);
        yield return Migration.FromSql(3, Migration003_AddMudLoginScript.Sql);
        yield return new Migration(4, Migration004_SessionData.ApplyAsync, RebuildsTables: true);
        yield return Migration.FromSql(5, Migration005_MovementKeys.Sql) with { RebuildsTables = true };
        yield return new Migration(6, Migration006_MessageRuleScripts.ApplyAsync);
    }

    private sealed record Migration(
        int Version,
        Func<SqliteConnection, DbTransaction, CancellationToken, Task> Apply,
        bool RebuildsTables = false)
    {
        public static Migration FromSql(int version, string sql) =>
            new(version, async (connection, transaction, ct) =>
                await connection.ExecuteAsync(new CommandDefinition(sql, transaction: transaction, cancellationToken: ct)).ConfigureAwait(false));
    }
}
