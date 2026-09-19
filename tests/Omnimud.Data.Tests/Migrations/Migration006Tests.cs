using Dapper;
using Microsoft.Data.Sqlite;
using Omnimud.Data.Migrations;
using Omnimud.Data.Seed;

namespace Omnimud.Data.Tests;

/// <summary>Migration 006 (MessageRuleSets.Script) applied to a version 5 database that already holds rule sets and MUDs.</summary>
public sealed class Migration006Tests : IAsyncLifetime
{
    private SqliteConnection _conn = null!;

    public async Task InitializeAsync()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        await _conn.OpenAsync();
        await _conn.ExecuteAsync("PRAGMA foreign_keys=ON");
        await MigrationRunner.RunAsync(_conn, upToVersion: 5);

        await _conn.ExecuteAsync("""
            INSERT INTO MessageRuleSets (Id, Name, IsBuiltIn) VALUES (50, 'Mis reglas', 0), (51, 'callandor mio', 0);
            INSERT INTO MessageRules (RuleSetId, SortOrder, Pattern, Template) VALUES (50, 1, '^(\w+) te dice', '$0'), (50, 2, 'grita', '$0');
            INSERT INTO Muds (Id, Name, Host, Port, MessageRuleSetId) VALUES
                (1, 'Reinos', 'rl.example.org', 5001, 50),
                (2, 'Callandor', 'callandor.example.org', 23, (SELECT Id FROM MessageRuleSets WHERE Name = 'Callandor'));
            """);
    }

    public Task DisposeAsync()
    {
        _conn.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Version5_HasNoScriptColumn()
    {
        var columns = (await _conn.QueryAsync<string>("SELECT name FROM pragma_table_info('MessageRuleSets')")).ToList();
        columns.Should().NotContain("Script");
    }

    [Fact]
    public async Task BuiltInSets_BecomeScripts_AndKeepTheirIdsRulesAndMuds()
    {
        var before = (await _conn.QueryAsync<(long Id, string Name)>("SELECT Id, Name FROM MessageRuleSets WHERE IsBuiltIn = 1 ORDER BY Id")).ToList();
        var rulesBefore = await _conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM MessageRules");

        await MigrationRunner.RunAsync(_conn);

        (await _conn.ExecuteScalarAsync<long>("SELECT MAX(Version) FROM SchemaVersion")).Should().Be(6);
        var after = (await _conn.QueryAsync<(long Id, string Name, string? Script)>("SELECT Id, Name, Script FROM MessageRuleSets WHERE IsBuiltIn = 1 ORDER BY Id")).ToList();
        after.Select(s => (s.Id, s.Name)).Should().Equal(before);
        after.Should().HaveCount(4);
        foreach (var set in after)
            set.Script.Should().Be(BuiltInMessageRuleSets.ScriptOf(set.Name)).And.NotBeNullOrWhiteSpace();

        (await _conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM MessageRules")).Should().Be(rulesBefore, "the pattern rules stay for reference");
        (await _conn.ExecuteScalarAsync<string>("SELECT s.Name FROM Muds m JOIN MessageRuleSets s ON s.Id = m.MessageRuleSetId WHERE m.Id = 2")).Should().Be("Callandor");
    }

    [Fact]
    public async Task UserSets_AreNotTouched_EvenWithASimilarName()
    {
        await MigrationRunner.RunAsync(_conn);

        var user = (await _conn.QueryAsync<(long Id, string Name, long IsBuiltIn, string? Script)>(
            "SELECT Id, Name, IsBuiltIn, Script FROM MessageRuleSets WHERE IsBuiltIn = 0 ORDER BY Id")).ToList();
        user.Should().Equal((50, "Mis reglas", 0, null), (51, "callandor mio", 0, null));
        (await _conn.QueryAsync<string>("SELECT Pattern FROM MessageRules WHERE RuleSetId = 50 ORDER BY SortOrder")).Should().Equal(@"^(\w+) te dice", "grita");
        (await _conn.ExecuteScalarAsync<long>("SELECT MessageRuleSetId FROM Muds WHERE Id = 1")).Should().Be(50);
    }

    [Fact]
    public async Task AUserSetThatTookTheNameOfABuiltInOne_IsNotConverted()
    {
        // The user removed the built-in flag by hand or a built-in set is missing: only IsBuiltIn = 1 rows change.
        await _conn.ExecuteAsync("UPDATE MessageRuleSets SET IsBuiltIn = 0 WHERE Name = 'Simauria'");

        await MigrationRunner.RunAsync(_conn);

        (await _conn.ExecuteScalarAsync<string?>("SELECT Script FROM MessageRuleSets WHERE Name = 'Simauria'")).Should().BeNull();
        (await _conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM MessageRuleSets WHERE Script IS NOT NULL")).Should().Be(3);
    }

    [Fact]
    public async Task IsIdempotent_AndAFreshDatabaseEndsTheSame()
    {
        await MigrationRunner.RunAsync(_conn);
        await MigrationRunner.RunAsync(_conn);
        (await _conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM SchemaVersion WHERE Version = 6")).Should().Be(1);

        using var fresh = new SqliteConnection("Data Source=:memory:");
        await fresh.OpenAsync();
        await MigrationRunner.RunAsync(fresh);
        var scripts = (await fresh.QueryAsync<(string Name, string Script)>("SELECT Name, Script FROM MessageRuleSets WHERE IsBuiltIn = 1")).ToList();
        scripts.Should().HaveCount(4);
        scripts.Should().OnlyContain(s => s.Script == BuiltInMessageRuleSets.ScriptOf(s.Name));
    }
}
