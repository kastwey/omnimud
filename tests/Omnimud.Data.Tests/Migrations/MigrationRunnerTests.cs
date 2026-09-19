using Microsoft.Data.Sqlite;
using Omnimud.Data.Migrations;

namespace Omnimud.Data.Tests;

public sealed class MigrationRunnerTests
{
    [Fact]
    public async Task RunAsync_CreatesAllTables()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await MigrationRunner.RunAsync(connection);

        var tables = await GetTableNames(connection);
        tables.Should().Contain([
            "Muds", "Characters", "Aliases", "Triggers", "Paths", "Directions", "Movements", "Options",
            "MessageRuleSets", "MessageRules", "SchemaVersion"]);
        tables.Should().NotContain(t => t.EndsWith("_new"));
    }

    [Fact]
    public async Task RunAsync_SetsSchemaVersion()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await MigrationRunner.RunAsync(connection);

        (await ScalarAsync(connection, "SELECT MAX(Version) FROM SchemaVersion")).Should().Be(MigrationRunner.LatestVersion);
        MigrationRunner.LatestVersion.Should().Be(6);
    }

    [Fact]
    public async Task RunAsync_IsIdempotent()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await MigrationRunner.RunAsync(connection);
        await MigrationRunner.RunAsync(connection); // should not throw
        await MigrationRunner.RunAsync(connection);

        (await ScalarAsync(connection, "SELECT COUNT(*) FROM SchemaVersion")).Should().Be(6);
        (await ScalarAsync(connection, "SELECT COUNT(*) FROM MessageRuleSets")).Should().Be(4, "the seed must not be repeated");
    }

    [Fact]
    public async Task RunAsync_UpToVersion_StopsThere_AndCanContinueLater()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await MigrationRunner.RunAsync(connection, upToVersion: 3);
        (await ScalarAsync(connection, "SELECT MAX(Version) FROM SchemaVersion")).Should().Be(3);
        (await GetTableNames(connection)).Should().NotContain("MessageRuleSets");

        await MigrationRunner.RunAsync(connection);
        (await ScalarAsync(connection, "SELECT MAX(Version) FROM SchemaVersion")).Should().Be(6);
    }

    [Fact]
    public async Task RunAsync_RestoresForeignKeyEnforcement()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await ExecuteAsync(connection, "PRAGMA foreign_keys=ON");

        await MigrationRunner.RunAsync(connection);

        (await ScalarAsync(connection, "PRAGMA foreign_keys")).Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_LeavesForeignKeysOff_WhenTheyWereOff()
    {
        using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        await connection.OpenAsync();

        await MigrationRunner.RunAsync(connection);

        (await ScalarAsync(connection, "PRAGMA foreign_keys")).Should().Be(0);
    }

    internal static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    internal static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    internal static async Task<List<string>> GetTableNames(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
        using var reader = await cmd.ExecuteReaderAsync();
        var tables = new List<string>();
        while (await reader.ReadAsync())
            tables.Add(reader.GetString(0));
        return tables;
    }
}
