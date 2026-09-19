using Dapper;
using Microsoft.Data.Sqlite;
using Omnimud.Data.Migrations;

namespace Omnimud.Data.Tests;

/// <summary>Migration 004 applied to a version 3 database that already holds data in every table.</summary>
public sealed class Migration004Tests : IAsyncLifetime
{
    private SqliteConnection _conn = null!;

    public async Task InitializeAsync()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        await _conn.OpenAsync();
        await _conn.ExecuteAsync("PRAGMA foreign_keys=ON");
        await MigrationRunner.RunAsync(_conn, upToVersion: 3);

        await _conn.ExecuteAsync("""
            INSERT INTO Muds (Id, Name, Host, Port, UseTls, SaveCommand, QuitCommand, ProcessRule, SoundDirectory, DefaultCharacterId, Encoding, LoginScript, CreatedAt, UpdatedAt)
            VALUES (1, 'Reinos', 'rl.example.org', 5001, 1, 'guardar', 'salir', 'balzhur', 'C:\snd', 999, 'iso-8859-1', 'connect %n %p', '2024-01-02 03:04:05', '2024-02-03 04:05:06'),
                   (2, 'Otro', 'otro.example.org', 23, 0, NULL, NULL, 'NoExiste', NULL, 3, 'utf-8', NULL, '2024-01-01 00:00:00', '2024-01-01 00:00:00'),
                   (3, 'Vacio', 'vacio.example.org', 4000, 0, NULL, NULL, NULL, NULL, NULL, 'utf-8', NULL, '2024-01-01 00:00:00', '2024-01-01 00:00:00');

            INSERT INTO Characters (Id, MudId, Name, EncryptedPassword, IsDefault) VALUES
                (1, 1, 'Ana', NULL, 0),
                (2, 1, 'Berto', x'0102030405', 1),
                (3, 2, 'Carla', NULL, 0);

            INSERT INTO Aliases (CharacterId, Command, Action, Enabled) VALUES (2, 'k', 'matar', 1), (2, 'h', 'curar', 0), (3, 'k', 'kill', 1);

            INSERT INTO Triggers (Id, CharacterId, Name, Pattern, PatternType, Action, ActionType, Sound, Enabled, CaseSensitive, Priority, CreatedAt, UpdatedAt) VALUES
                ('t-late',  2, 'Tarde',   'zzz', 1, 'dormir',   0, NULL,       1, 0, 90, '2024-03-03 00:00:00', '2024-03-03 00:00:00'),
                ('t-early', 2, 'Pronto',  'aaa', 0, 'print(1)', 2, NULL,       0, 1, 10, '2024-03-01 00:00:00', '2024-03-01 00:00:00'),
                ('t-mid',   2, 'Medio',   'mmm', 2, 'x',        1, 'ding.wav', 1, 0, 50, '2024-03-02 00:00:00', '2024-03-02 00:00:00'),
                ('t-other', 3, 'DeCarla', 'ccc', 0, 'y',        0, NULL,       1, 0, 50, '2024-03-05 00:00:00', '2024-03-05 00:00:00');

            INSERT INTO Paths (CharacterId, Name, Path) VALUES (2, 'plaza', '3n2e'), (3, 'casa', 's');

            INSERT INTO Directions (Direction, Abbreviation, OppositeDirection) VALUES ('norte', 'n', 'sur');

            INSERT INTO Movements (Id, CharacterId, KeyCode, Command) VALUES
                (1, 2, 104, 'norte'),   -- Keys.NumPad8
                (2, 2, 50, 'sur'),      -- Keys.D2
                (3, 2, 38, 'arriba'),   -- Keys.Up: meaningless now
                (4, 2, 8, 'duplicada'), -- collides with NumPad8 once translated: the oldest row wins
                (5, 3, 5, 'mirar');

            INSERT INTO Options (Scope, ScopeId, Key, Value) VALUES
                (0, NULL, 'Language', 'en'),
                (0, NULL, 'Language', 'es'),   -- the old UNIQUE allowed this duplicate
                (0, NULL, 'Volume', '70'),
                (1, 1, 'Volume', '30'),
                (2, 2, 'FontSize', '14'),
                (1, 77, 'Volume', '1'),        -- orphans
                (2, 88, 'Volume', '2');
            """);

        await MigrationRunner.RunAsync(_conn);
    }

    public Task DisposeAsync()
    {
        _conn.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Muds_KeepEveryColumn_AndGetNewDefaults()
    {
        var mud = await _conn.QuerySingleAsync("SELECT * FROM Muds WHERE Id = 1");

        ((string)mud.Name).Should().Be("Reinos");
        ((string)mud.Host).Should().Be("rl.example.org");
        ((long)mud.Port).Should().Be(5001);
        ((long)mud.UseTls).Should().Be(1);
        ((string)mud.SaveCommand).Should().Be("guardar");
        ((string)mud.QuitCommand).Should().Be("salir");
        ((string)mud.ProcessRule).Should().Be("balzhur");
        ((string)mud.SoundDirectory).Should().Be(@"C:\snd");
        ((string)mud.Encoding).Should().Be("iso-8859-1");
        ((string)mud.LoginScript).Should().Be("connect %n %p");
        ((string)mud.CreatedAt).Should().Be("2024-01-02 03:04:05");
        ((string)mud.UpdatedAt).Should().Be("2024-02-03 04:05:06");
        ((long)mud.ValidateCertificate).Should().Be(1);
        ((long)mud.MovementMode).Should().Be(0);
    }

    [Fact]
    public async Task Muds_ProcessRuleIsLinkedToTheSeededRuleSet_CaseInsensitively()
    {
        var linked = await _conn.ExecuteScalarAsync<string?>(
            "SELECT s.Name FROM Muds m JOIN MessageRuleSets s ON s.Id = m.MessageRuleSetId WHERE m.Id = 1");
        linked.Should().Be("Balzhur");

        (await _conn.ExecuteScalarAsync<long?>("SELECT MessageRuleSetId FROM Muds WHERE Id = 2")).Should().BeNull("'NoExiste' is not a rule set");
        (await _conn.ExecuteScalarAsync<long?>("SELECT MessageRuleSetId FROM Muds WHERE Id = 3")).Should().BeNull();
    }

    [Fact]
    public async Task DefaultCharacter_IsMadeCoherent_IsDefaultWinsOverStaleId()
    {
        (await _conn.ExecuteScalarAsync<long?>("SELECT DefaultCharacterId FROM Muds WHERE Id = 1")).Should().Be(2, "Berto had IsDefault = 1 and 999 did not exist");
        (await _conn.ExecuteScalarAsync<long?>("SELECT DefaultCharacterId FROM Muds WHERE Id = 2")).Should().Be(3, "a valid DefaultCharacterId is kept");
        (await _conn.ExecuteScalarAsync<long?>("SELECT DefaultCharacterId FROM Muds WHERE Id = 3")).Should().BeNull();

        var defaults = (await _conn.QueryAsync<long>("SELECT Id FROM Characters WHERE IsDefault = 1 ORDER BY Id")).ToList();
        defaults.Should().Equal(2, 3);
    }

    [Fact]
    public async Task Characters_KeepPassword_AndGetMovementMode()
    {
        var berto = await _conn.QuerySingleAsync("SELECT * FROM Characters WHERE Id = 2");
        ((byte[])berto.EncryptedPassword).Should().Equal(1, 2, 3, 4, 5);
        ((long)berto.MovementMode).Should().Be(0);
        (await _conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Characters")).Should().Be(3);
    }

    [Fact]
    public async Task AliasesAndPaths_AreUntouched()
    {
        (await _conn.QueryAsync<string>("SELECT Command || '=' || Action || '/' || Enabled FROM Aliases WHERE CharacterId = 2 ORDER BY Command"))
            .Should().Equal("h=curar/0", "k=matar/1");
        (await _conn.QueryAsync<string>("SELECT Name || '=' || Path FROM Paths ORDER BY Name")).Should().Equal("casa=s", "plaza=3n2e");
    }

    [Fact]
    public async Task Triggers_KeepData_AndSortOrderFollowsCreationPerCharacter()
    {
        var rows = (await _conn.QueryAsync("SELECT * FROM Triggers WHERE CharacterId = 2 ORDER BY SortOrder")).ToList();

        rows.Select(r => (string)r.Id).Should().Equal("t-early", "t-mid", "t-late");
        rows.Select(r => (long)r.SortOrder).Should().Equal(1, 2, 3);
        rows.Select(r => (long)r.Multiline + (long)r.GagLine).Should().AllBeEquivalentTo(0L);

        var mid = rows[1];
        ((string)mid.Name).Should().Be("Medio");
        ((long)mid.PatternType).Should().Be(2);
        ((long)mid.ActionType).Should().Be(1);
        ((string)mid.Sound).Should().Be("ding.wav");
        ((long)mid.Priority).Should().Be(50);

        (await _conn.ExecuteScalarAsync<long>("SELECT SortOrder FROM Triggers WHERE Id = 't-other'")).Should().Be(1);
    }

    [Fact]
    public async Task Directions_BecomePerMud_AndStartEmpty()
    {
        (await _conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Directions")).Should().Be(0);

        await _conn.ExecuteAsync("INSERT INTO Directions (MudId, Direction, Abbreviation, OppositeDirection) VALUES (1, 'norte', 'n', 'sur')");
        await _conn.ExecuteAsync("INSERT INTO Directions (MudId, Direction, Abbreviation, OppositeDirection) VALUES (2, 'norte', 'n', 'sur')");

        var sameAbbreviation = () => _conn.ExecuteAsync("INSERT INTO Directions (MudId, Direction, Abbreviation) VALUES (1, 'noreste', 'n')");
        var sameDirection = () => _conn.ExecuteAsync("INSERT INTO Directions (MudId, Direction, Abbreviation) VALUES (1, 'norte', 'x')");
        var longAbbreviation = () => _conn.ExecuteAsync("INSERT INTO Directions (MudId, Direction, Abbreviation) VALUES (1, 'noreste', 'ne')");
        await sameAbbreviation.Should().ThrowAsync<SqliteException>();
        await sameDirection.Should().ThrowAsync<SqliteException>();
        await longAbbreviation.Should().ThrowAsync<SqliteException>();
    }

    [Fact]
    public async Task Movements_AreMigratedToNumpadDigits()
    {
        var rows = (await _conn.QueryAsync<string>(
            "SELECT COALESCE(MudId, 'null') || '/' || CharacterId || '/' || KeyCode || '/' || Command FROM Movements ORDER BY CharacterId, KeyCode")).ToList();

        rows.Should().Equal("null/2/2/sur", "null/2/8/norte", "null/3/5/mirar");
    }

    [Fact]
    public async Task Movements_EnforceOwnerAndKeyRules()
    {
        await _conn.ExecuteAsync("INSERT INTO Movements (MudId, CharacterId, KeyCode, Command) VALUES (1, NULL, 8, 'n')");

        var both = () => _conn.ExecuteAsync("INSERT INTO Movements (MudId, CharacterId, KeyCode, Command) VALUES (1, 1, 1, 'x')");
        var none = () => _conn.ExecuteAsync("INSERT INTO Movements (MudId, CharacterId, KeyCode, Command) VALUES (NULL, NULL, 1, 'x')");
        var badKey = () => _conn.ExecuteAsync("INSERT INTO Movements (MudId, CharacterId, KeyCode, Command) VALUES (1, NULL, 18, 'x')");
        var duplicatedForMud = () => _conn.ExecuteAsync("INSERT INTO Movements (MudId, CharacterId, KeyCode, Command) VALUES (1, NULL, 8, 'x')");
        var duplicatedForCharacter = () => _conn.ExecuteAsync("INSERT INTO Movements (MudId, CharacterId, KeyCode, Command) VALUES (NULL, 2, 8, 'x')");

        await both.Should().ThrowAsync<SqliteException>();
        await none.Should().ThrowAsync<SqliteException>();
        await badKey.Should().ThrowAsync<SqliteException>();
        await duplicatedForMud.Should().ThrowAsync<SqliteException>();
        await duplicatedForCharacter.Should().ThrowAsync<SqliteException>();
    }

    [Fact]
    public async Task Options_GlobalDuplicatesCollapseToTheNewest_AndOrphansGo()
    {
        var rows = (await _conn.QueryAsync<string>(
            "SELECT Scope || '/' || COALESCE(ScopeId, 'null') || '/' || Key || '=' || Value FROM Options ORDER BY Scope, Key")).ToList();

        rows.Should().Equal("0/null/Language=es", "0/null/Volume=70", "1/1/Volume=30", "2/2/FontSize=14");
    }

    [Fact]
    public async Task Options_GlobalRowsAreNowUnique()
    {
        var duplicate = () => _conn.ExecuteAsync("INSERT INTO Options (Scope, ScopeId, Key, Value) VALUES (0, NULL, 'Language', 'fr')");
        await duplicate.Should().ThrowAsync<SqliteException>().Where(e => e.SqliteErrorCode == 19);
    }

    [Fact]
    public async Task RuleSets_AreSeeded()
    {
        (await _conn.QueryAsync<string>("SELECT Name FROM MessageRuleSets WHERE IsBuiltIn = 1 ORDER BY Name"))
            .Should().Equal("Balzhur", "Callandor", "Cyberlife", "Simauria");
        (await _conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM MessageRules r JOIN MessageRuleSets s ON s.Id = r.RuleSetId WHERE s.Name = 'Cyberlife'")).Should().Be(15);
    }

    [Fact]
    public async Task ForeignKeys_AreIntactAfterRebuildingTables()
    {
        (await _conn.QueryAsync("PRAGMA foreign_key_check")).Should().BeEmpty();
        (await _conn.ExecuteScalarAsync<long>("PRAGMA foreign_keys")).Should().Be(1);

        await _conn.ExecuteAsync("INSERT INTO Directions (MudId, Direction, Abbreviation) VALUES (1, 'norte', 'n')");
        await _conn.ExecuteAsync("INSERT INTO Movements (MudId, CharacterId, KeyCode, Command) VALUES (1, NULL, 8, 'n')");

        await _conn.ExecuteAsync("DELETE FROM Muds WHERE Id = 1");

        (await _conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Characters WHERE MudId = 1")).Should().Be(0);
        (await _conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Aliases WHERE CharacterId IN (1, 2)")).Should().Be(0);
        (await _conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Triggers WHERE CharacterId IN (1, 2)")).Should().Be(0);
        (await _conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Paths WHERE CharacterId IN (1, 2)")).Should().Be(0);
        (await _conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Movements WHERE CharacterId IN (1, 2) OR MudId = 1")).Should().Be(0);
        (await _conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Directions WHERE MudId = 1")).Should().Be(0);
        (await _conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Options WHERE (Scope = 1 AND ScopeId = 1) OR (Scope = 2 AND ScopeId = 2)")).Should().Be(0);

        // The other MUD is untouched.
        (await _conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Characters WHERE MudId = 2")).Should().Be(1);
        (await _conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Aliases WHERE CharacterId = 3")).Should().Be(1);
    }

    [Fact]
    public async Task DeletingTheDefaultCharacter_ClearsTheMudReference()
    {
        await _conn.ExecuteAsync("DELETE FROM Characters WHERE Id = 2");
        (await _conn.ExecuteScalarAsync<long?>("SELECT DefaultCharacterId FROM Muds WHERE Id = 1")).Should().BeNull();
    }

    [Fact]
    public async Task DeletingARuleSet_LeavesTheMudWithoutOne()
    {
        await _conn.ExecuteAsync("DELETE FROM MessageRuleSets WHERE Name = 'Balzhur'");

        (await _conn.ExecuteScalarAsync<long?>("SELECT MessageRuleSetId FROM Muds WHERE Id = 1")).Should().BeNull();
        (await _conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Muds")).Should().Be(3);
    }

    [Fact]
    public async Task AutoIncrement_ContinuesAfterTheHighestMigratedId()
    {
        await _conn.ExecuteAsync("INSERT INTO Muds (Name, Host, Port) VALUES ('Nuevo', 'h', 1)");
        (await _conn.ExecuteScalarAsync<long>("SELECT Id FROM Muds WHERE Name = 'Nuevo'")).Should().Be(4);
    }
}
