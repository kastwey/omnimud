using Dapper;
using Microsoft.Data.Sqlite;
using Omnimud.Data.Migrations;

namespace Omnimud.Data.Tests;

/// <summary>Migration 005 (movement key codes 0-17) applied to a version 4 database that already holds movements.</summary>
public sealed class Migration005Tests : IAsyncLifetime
{
    private SqliteConnection _conn = null!;

    public async Task InitializeAsync()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        await _conn.OpenAsync();
        await _conn.ExecuteAsync("PRAGMA foreign_keys=ON");
        await MigrationRunner.RunAsync(_conn, upToVersion: 4);

        await _conn.ExecuteAsync("""
            INSERT INTO Muds (Id, Name, Host, Port) VALUES (1, 'Reinos', 'rl.example.org', 5001), (2, 'Otro', 'otro.example.org', 23);
            INSERT INTO Characters (Id, MudId, Name) VALUES (1, 1, 'Ana'), (2, 1, 'Berto');

            INSERT INTO Movements (Id, MudId, CharacterId, KeyCode, Command) VALUES
                (3, 1, NULL, 8, 'norte'),
                (5, 1, NULL, 2, 'sur'),
                (9, NULL, 2, 0, 'mirar'),
                (12, NULL, 2, 9, '');
            """);
    }

    public Task DisposeAsync()
    {
        _conn.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Version4_RejectsTheNewKeys_SoTheMigrationIsNeeded()
    {
        var arrow = () => _conn.ExecuteAsync("INSERT INTO Movements (MudId, CharacterId, KeyCode, Command) VALUES (1, NULL, 10, 'norte')");
        await arrow.Should().ThrowAsync<SqliteException>();
    }

    [Fact]
    public async Task ExistingRows_AreKept_WithTheirIds()
    {
        await MigrationRunner.RunAsync(_conn);

        var rows = (await _conn.QueryAsync<string>(
            "SELECT Id || ':' || COALESCE(MudId, 'null') || '/' || COALESCE(CharacterId, 'null') || '/' || KeyCode || '=' || Command FROM Movements ORDER BY Id")).ToList();

        rows.Should().Equal("3:1/null/8=norte", "5:1/null/2=sur", "9:null/2/0=mirar", "12:null/2/9=");
        (await _conn.ExecuteScalarAsync<long>("SELECT MAX(Version) FROM SchemaVersion")).Should().Be(6);
    }

    [Fact]
    public async Task Codes10To17_AreAccepted_And18AndNegative_AreRejected()
    {
        await MigrationRunner.RunAsync(_conn);

        for (var key = 10; key <= 17; key++)
            await _conn.ExecuteAsync("INSERT INTO Movements (MudId, CharacterId, KeyCode, Command) VALUES (2, NULL, @key, 'x')", new { key });

        (await _conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Movements WHERE MudId = 2")).Should().Be(8);

        var tooHigh = () => _conn.ExecuteAsync("INSERT INTO Movements (MudId, CharacterId, KeyCode, Command) VALUES (2, NULL, 18, 'x')");
        var negative = () => _conn.ExecuteAsync("INSERT INTO Movements (MudId, CharacterId, KeyCode, Command) VALUES (2, NULL, -1, 'x')");
        await tooHigh.Should().ThrowAsync<SqliteException>();
        await negative.Should().ThrowAsync<SqliteException>();
    }

    [Fact]
    public async Task OwnerAndUniquenessRules_Survive()
    {
        await MigrationRunner.RunAsync(_conn);

        var both = () => _conn.ExecuteAsync("INSERT INTO Movements (MudId, CharacterId, KeyCode, Command) VALUES (1, 1, 1, 'x')");
        var none = () => _conn.ExecuteAsync("INSERT INTO Movements (MudId, CharacterId, KeyCode, Command) VALUES (NULL, NULL, 1, 'x')");
        var duplicatedForMud = () => _conn.ExecuteAsync("INSERT INTO Movements (MudId, CharacterId, KeyCode, Command) VALUES (1, NULL, 8, 'x')");
        var duplicatedForCharacter = () => _conn.ExecuteAsync("INSERT INTO Movements (MudId, CharacterId, KeyCode, Command) VALUES (NULL, 2, 0, 'x')");
        var nullCommand = () => _conn.ExecuteAsync("INSERT INTO Movements (MudId, CharacterId, KeyCode, Command) VALUES (1, NULL, 11, NULL)");

        await both.Should().ThrowAsync<SqliteException>();
        await none.Should().ThrowAsync<SqliteException>();
        await duplicatedForMud.Should().ThrowAsync<SqliteException>();
        await duplicatedForCharacter.Should().ThrowAsync<SqliteException>();
        await nullCommand.Should().ThrowAsync<SqliteException>();
    }

    [Fact]
    public async Task NewIds_ContinueAfterTheHighestMigratedId()
    {
        await MigrationRunner.RunAsync(_conn);

        await _conn.ExecuteAsync("INSERT INTO Movements (MudId, CharacterId, KeyCode, Command) VALUES (1, NULL, 10, 'norte')");

        (await _conn.ExecuteScalarAsync<long>("SELECT Id FROM Movements WHERE KeyCode = 10")).Should().Be(13);
    }

    [Fact]
    public async Task ForeignKeys_StillCascade_AndAreSwitchedBackOn()
    {
        await MigrationRunner.RunAsync(_conn);

        (await _conn.QueryAsync("PRAGMA foreign_key_check")).Should().BeEmpty();
        (await _conn.ExecuteScalarAsync<long>("PRAGMA foreign_keys")).Should().Be(1);

        await _conn.ExecuteAsync("DELETE FROM Characters WHERE Id = 2");
        (await _conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Movements WHERE CharacterId = 2")).Should().Be(0);

        await _conn.ExecuteAsync("DELETE FROM Muds WHERE Id = 1");
        (await _conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Movements")).Should().Be(0);
    }

    [Fact]
    public async Task RunningAgain_DoesNothing()
    {
        await MigrationRunner.RunAsync(_conn);
        await MigrationRunner.RunAsync(_conn);

        (await _conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Movements")).Should().Be(4);
        (await _conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM SchemaVersion WHERE Version = 5")).Should().Be(1);
    }
}
