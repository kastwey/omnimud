using System.Data.Common;
using Dapper;
using Microsoft.Data.Sqlite;
using Omnimud.Data.Seed;

namespace Omnimud.Data.Migrations;

/// <summary>
/// Everything the session engine needs: message rule sets, per-MUD directions, MUD/character
/// movements, new MUD, character and trigger columns, and a NULL-safe unique key for options.
/// Runs with foreign keys switched off by the runner because Muds, Movements and Options are rebuilt.
/// </summary>
internal static class Migration004_SessionData
{
    private const string CreateRuleTablesSql = """
        CREATE TABLE MessageRuleSets (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL UNIQUE COLLATE NOCASE,
            IsBuiltIn INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE MessageRules (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            RuleSetId INTEGER NOT NULL REFERENCES MessageRuleSets(Id) ON DELETE CASCADE,
            SortOrder INTEGER NOT NULL DEFAULT 0,
            Pattern TEXT NOT NULL,
            Template TEXT NOT NULL DEFAULT '$0',
            CaseSensitive INTEGER NOT NULL DEFAULT 0,
            Channel TEXT,
            Enabled INTEGER NOT NULL DEFAULT 1
        );

        CREATE INDEX IX_MessageRules_RuleSetId ON MessageRules(RuleSetId, SortOrder);
        """;

    private const string SchemaSql = """
        -- ── Characters ────────────────────────────────────────────────────────
        ALTER TABLE Characters ADD COLUMN MovementMode INTEGER NOT NULL DEFAULT 0;

        -- ── Muds: rebuilt to get real foreign keys ────────────────────────────
        CREATE TABLE Muds_new (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL UNIQUE,
            Host TEXT NOT NULL,
            Port INTEGER NOT NULL,
            UseTls INTEGER NOT NULL DEFAULT 0,
            ValidateCertificate INTEGER NOT NULL DEFAULT 1,
            SaveCommand TEXT,
            QuitCommand TEXT,
            LoginScript TEXT,
            ProcessRule TEXT,
            MessageRuleSetId INTEGER NULL REFERENCES MessageRuleSets(Id) ON DELETE SET NULL,
            SoundDirectory TEXT,
            Encoding TEXT NOT NULL DEFAULT 'utf-8',
            MovementMode INTEGER NOT NULL DEFAULT 0,
            DefaultCharacterId INTEGER NULL REFERENCES Characters(Id) ON DELETE SET NULL,
            CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
            UpdatedAt TEXT NOT NULL DEFAULT (datetime('now'))
        );

        -- Until now only Characters.IsDefault was maintained, so it wins over a stale DefaultCharacterId.
        INSERT INTO Muds_new (Id, Name, Host, Port, UseTls, ValidateCertificate, SaveCommand, QuitCommand, LoginScript,
                              ProcessRule, MessageRuleSetId, SoundDirectory, Encoding, MovementMode, DefaultCharacterId,
                              CreatedAt, UpdatedAt)
        SELECT m.Id, m.Name, m.Host, m.Port, m.UseTls, 1, m.SaveCommand, m.QuitCommand, m.LoginScript,
               m.ProcessRule,
               (SELECT s.Id FROM MessageRuleSets s WHERE s.Name = TRIM(m.ProcessRule) COLLATE NOCASE),
               m.SoundDirectory, m.Encoding, 0,
               COALESCE(
                   (SELECT MIN(c.Id) FROM Characters c WHERE c.MudId = m.Id AND c.IsDefault = 1),
                   (SELECT c.Id FROM Characters c WHERE c.Id = m.DefaultCharacterId AND c.MudId = m.Id)),
               m.CreatedAt, m.UpdatedAt
        FROM Muds m;

        DROP TABLE Muds;
        ALTER TABLE Muds_new RENAME TO Muds;

        UPDATE Characters
        SET IsDefault = CASE
            WHEN Id = (SELECT DefaultCharacterId FROM Muds WHERE Muds.Id = Characters.MudId) THEN 1
            ELSE 0 END;

        -- ── Triggers ──────────────────────────────────────────────────────────
        -- ActionType has no CHECK, so value 3 (command + sound) needs nothing; Action is NOT NULL
        -- but may be the empty string (sound-only triggers).
        ALTER TABLE Triggers ADD COLUMN Multiline INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE Triggers ADD COLUMN GagLine INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE Triggers ADD COLUMN SortOrder INTEGER NOT NULL DEFAULT 0;

        UPDATE Triggers
        SET SortOrder = (
            SELECT COUNT(*) FROM Triggers t2
            WHERE t2.CharacterId = Triggers.CharacterId
              AND (t2.CreatedAt < Triggers.CreatedAt
                   OR (t2.CreatedAt = Triggers.CreatedAt AND t2.rowid <= Triggers.rowid)));

        CREATE INDEX IX_Triggers_Character_SortOrder ON Triggers(CharacterId, SortOrder);

        -- ── Directions: per MUD. The global table never had data nor UI. ──────
        DROP TABLE Directions;
        CREATE TABLE Directions (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            MudId INTEGER NOT NULL REFERENCES Muds(Id) ON DELETE CASCADE,
            Direction TEXT NOT NULL,
            Abbreviation TEXT NOT NULL CHECK (length(Abbreviation) = 1),
            OppositeDirection TEXT,
            UNIQUE (MudId, Abbreviation),
            UNIQUE (MudId, Direction)
        );

        -- ── Movements: owned by a MUD or by a character ───────────────────────
        CREATE TABLE Movements_new (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            MudId INTEGER NULL REFERENCES Muds(Id) ON DELETE CASCADE,
            CharacterId INTEGER NULL REFERENCES Characters(Id) ON DELETE CASCADE,
            KeyCode INTEGER NOT NULL CHECK (KeyCode BETWEEN 0 AND 9),
            Command TEXT NOT NULL,
            CHECK ((MudId IS NULL) <> (CharacterId IS NULL)),
            UNIQUE (MudId, KeyCode),
            UNIQUE (CharacterId, KeyCode)
        );

        -- KeyCode becomes the numpad digit. Old rows may hold the digit itself or the virtual key
        -- (NumPad0-9 = 96-105, D0-D9 = 48-57); anything else has no meaning any more and is dropped.
        INSERT OR IGNORE INTO Movements_new (MudId, CharacterId, KeyCode, Command)
        SELECT NULL, CharacterId,
               CASE WHEN KeyCode BETWEEN 0 AND 9 THEN KeyCode
                    WHEN KeyCode BETWEEN 96 AND 105 THEN KeyCode - 96
                    ELSE KeyCode - 48 END,
               Command
        FROM Movements
        WHERE KeyCode BETWEEN 0 AND 9 OR KeyCode BETWEEN 96 AND 105 OR KeyCode BETWEEN 48 AND 57
        ORDER BY Id;

        DROP TABLE Movements;
        ALTER TABLE Movements_new RENAME TO Movements;

        -- ── Options: UNIQUE(Scope, ScopeId, Key) did not protect global rows (ScopeId NULL) ──
        CREATE TABLE Options_new (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Scope INTEGER NOT NULL,
            ScopeId INTEGER,
            Key TEXT NOT NULL,
            Value TEXT NOT NULL
        );

        INSERT INTO Options_new (Scope, ScopeId, Key, Value)
        SELECT o.Scope, o.ScopeId, o.Key, o.Value
        FROM Options o
        WHERE o.Id = (SELECT MAX(o2.Id) FROM Options o2
                      WHERE o2.Scope = o.Scope AND COALESCE(o2.ScopeId, 0) = COALESCE(o.ScopeId, 0) AND o2.Key = o.Key)
        ORDER BY o.Id;

        DROP TABLE Options;
        ALTER TABLE Options_new RENAME TO Options;
        CREATE UNIQUE INDEX UX_Options_Scope_Key ON Options(Scope, COALESCE(ScopeId, 0), Key);

        -- Options have no foreign key (ScopeId points to a different table per scope).
        CREATE TRIGGER TR_Muds_Delete_Options AFTER DELETE ON Muds
        BEGIN
            DELETE FROM Options WHERE Scope = 1 AND ScopeId = OLD.Id;
        END;

        CREATE TRIGGER TR_Characters_Delete_Options AFTER DELETE ON Characters
        BEGIN
            DELETE FROM Options WHERE Scope = 2 AND ScopeId = OLD.Id;
        END;

        DELETE FROM Options WHERE Scope = 1 AND ScopeId NOT IN (SELECT Id FROM Muds);
        DELETE FROM Options WHERE Scope = 2 AND ScopeId NOT IN (SELECT Id FROM Characters);
        """;

    public static async Task ApplyAsync(SqliteConnection connection, DbTransaction transaction, CancellationToken ct)
    {
        await connection.ExecuteAsync(new CommandDefinition(CreateRuleTablesSql, transaction: transaction, cancellationToken: ct)).ConfigureAwait(false);
        // Seeded before the Muds rebuild so Muds.ProcessRule can be resolved to a rule set.
        await SeedMessageRuleSetsAsync(connection, transaction, ct).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(SchemaSql, transaction: transaction, cancellationToken: ct)).ConfigureAwait(false);
    }

    private static async Task SeedMessageRuleSetsAsync(SqliteConnection connection, DbTransaction transaction, CancellationToken ct)
    {
        foreach (var set in BuiltInMessageRuleSets.All)
        {
            var setId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "INSERT INTO MessageRuleSets (Name, IsBuiltIn) VALUES (@Name, 1); SELECT last_insert_rowid();",
                new { set.Name }, transaction, cancellationToken: ct)).ConfigureAwait(false);

            var order = 0;
            foreach (var rule in set.Rules)
            {
                order++;
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO MessageRules (RuleSetId, SortOrder, Pattern, Template, CaseSensitive, Channel, Enabled)
                    VALUES (@RuleSetId, @SortOrder, @Pattern, @Template, @CaseSensitive, @Channel, 1)
                    """,
                    new { RuleSetId = setId, SortOrder = order, rule.Pattern, rule.Template, rule.CaseSensitive, rule.Channel },
                    transaction, cancellationToken: ct)).ConfigureAwait(false);
            }
        }
    }
}
