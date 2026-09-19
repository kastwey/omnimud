using Dapper;
using Microsoft.Data.Sqlite;
using Omnimud.Data.Entities;
using Omnimud.Data.Migrations;
using Omnimud.Data.Repositories;

namespace Omnimud.Data.Tests;

/// <summary>The production path: a database FILE in WAL mode opened through SqliteConnectionFactory, as Program.cs does.</summary>
public sealed class FileDatabaseMigrationTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("omnimud-db-");
    private readonly SqliteConnectionFactory _factory;

    public FileDatabaseMigrationTests()
    {
        _factory = new SqliteConnectionFactory(Path.Combine(_directory.FullName, "omnimud.db"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { _directory.Delete(recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public async Task Version3FileWithData_IsMigrated_AndUsableThroughTheRepositories()
    {
        using (var conn = _factory.Create())
        {
            await MigrationRunner.RunAsync(conn, upToVersion: 3);
            await conn.ExecuteAsync("""
                INSERT INTO Muds (Id, Name, Host, Port, ProcessRule, Encoding) VALUES (1, 'Simauria', 'mud.simauria.org', 23, 'Simauria', 'iso-8859-1');
                INSERT INTO Characters (Id, MudId, Name, IsDefault) VALUES (1, 1, 'Pj', 1);
                INSERT INTO Aliases (CharacterId, Command, Action) VALUES (1, 'k', 'matar');
                INSERT INTO Triggers (Id, CharacterId, Name, Pattern, Action) VALUES ('t', 1, 'T', 'p', 'a');
                INSERT INTO Movements (CharacterId, KeyCode, Command) VALUES (1, 104, 'norte');
                INSERT INTO Options (Scope, ScopeId, Key, Value) VALUES (0, NULL, 'Volume', '50'), (0, NULL, 'Volume', '60');
                """);
        }

        // A new start of the application: new connection, pending migrations applied.
        using (var conn = _factory.Create())
        {
            await MigrationRunner.RunAsync(conn);
            await MigrationRunner.RunAsync(conn);
            (await conn.ExecuteScalarAsync<long>("PRAGMA foreign_keys")).Should().Be(1);
            (await conn.ExecuteScalarAsync<string>("PRAGMA integrity_check")).Should().Be("ok");
            (await conn.QueryAsync("PRAGMA foreign_key_check")).Should().BeEmpty();
        }

        var muds = new SqliteMudRepository(_factory);
        var mud = (await muds.GetByNameAsync("simauria"))!;
        mud.Encoding.Should().Be("iso-8859-1");
        mud.DefaultCharacterId.Should().Be(1);
        var ruleSet = await new SqliteMessageRuleRepository(_factory).GetRuleSetByIdAsync(mud.MessageRuleSetId!.Value);
        ruleSet!.Name.Should().Be("Simauria");

        (await new SqliteMovementRepository(_factory).GetByCharacterAsync(1)).Should().ContainSingle().Which.KeyCode.Should().Be(8);
        (await new SqliteOptionRepository(_factory).GetValueAsync(0, null, "Volume")).Should().Be("60");
        await new SqliteDirectionRepository(_factory).AddAsync(new DirectionEntity { MudId = mud.Id, Direction = "norte", Abbreviation = "n" });

        await muds.DeleteAsync(mud.Id);
        using (var conn = _factory.Create())
        {
            foreach (var table in new[] { "Characters", "Aliases", "Triggers", "Movements", "Directions" })
                (await conn.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM {table}")).Should().Be(0, table);
        }
    }

    [Fact]
    public async Task FailedMigration_IsRolledBack_AndForeignKeysAreRestored()
    {
        using var conn = _factory.Create();
        await MigrationRunner.RunAsync(conn, upToVersion: 3);
        // A table that migration 004 wants to create makes it fail half way.
        await conn.ExecuteAsync("CREATE TABLE MessageRuleSets (Id INTEGER PRIMARY KEY)");
        await conn.ExecuteAsync("INSERT INTO Muds (Name, Host, Port) VALUES ('M', 'h', 1)");

        var run = () => MigrationRunner.RunAsync(conn);

        await run.Should().ThrowAsync<SqliteException>();
        (await conn.ExecuteScalarAsync<long>("SELECT MAX(Version) FROM SchemaVersion")).Should().Be(3);
        (await conn.ExecuteScalarAsync<long>("PRAGMA foreign_keys")).Should().Be(1);
        (await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Muds")).Should().Be(1);
        (await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM pragma_table_info('Characters') WHERE name = 'MovementMode'")).Should().Be(0);
    }
}
