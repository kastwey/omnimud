namespace Omnimud.Data.Migrations;

internal static class Migration001_InitialSchema
{
    public const string Sql = """
        CREATE TABLE Muds (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL UNIQUE,
            Host TEXT NOT NULL,
            Port INTEGER NOT NULL,
            UseTls INTEGER NOT NULL DEFAULT 0,
            SaveCommand TEXT,
            QuitCommand TEXT,
            ProcessRule TEXT,
            SoundDirectory TEXT,
            DefaultCharacterId INTEGER,
            CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
            UpdatedAt TEXT NOT NULL DEFAULT (datetime('now'))
        );

        CREATE TABLE Characters (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            MudId INTEGER NOT NULL REFERENCES Muds(Id) ON DELETE CASCADE,
            Name TEXT NOT NULL,
            EncryptedPassword BLOB,
            IsDefault INTEGER NOT NULL DEFAULT 0,
            CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
            UpdatedAt TEXT NOT NULL DEFAULT (datetime('now')),
            UNIQUE(MudId, Name)
        );

        CREATE TABLE Aliases (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            CharacterId INTEGER NOT NULL REFERENCES Characters(Id) ON DELETE CASCADE,
            Command TEXT NOT NULL,
            Action TEXT NOT NULL,
            Enabled INTEGER NOT NULL DEFAULT 1,
            UNIQUE(CharacterId, Command)
        );

        CREATE TABLE Triggers (
            Id TEXT PRIMARY KEY,
            CharacterId INTEGER NOT NULL REFERENCES Characters(Id) ON DELETE CASCADE,
            Name TEXT NOT NULL,
            Pattern TEXT NOT NULL,
            PatternType INTEGER NOT NULL DEFAULT 0,
            Action TEXT NOT NULL,
            ActionType INTEGER NOT NULL DEFAULT 0,
            Sound TEXT,
            Enabled INTEGER NOT NULL DEFAULT 1,
            CaseSensitive INTEGER NOT NULL DEFAULT 0,
            Priority INTEGER NOT NULL DEFAULT 50,
            CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
            UpdatedAt TEXT NOT NULL DEFAULT (datetime('now'))
        );

        CREATE TABLE Paths (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            CharacterId INTEGER NOT NULL REFERENCES Characters(Id) ON DELETE CASCADE,
            Name TEXT NOT NULL,
            Path TEXT NOT NULL,
            UNIQUE(CharacterId, Name)
        );

        CREATE TABLE Directions (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Direction TEXT NOT NULL UNIQUE,
            Abbreviation TEXT NOT NULL,
            OppositeDirection TEXT
        );

        CREATE TABLE Movements (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            CharacterId INTEGER NOT NULL REFERENCES Characters(Id) ON DELETE CASCADE,
            KeyCode INTEGER NOT NULL,
            Command TEXT NOT NULL,
            UNIQUE(CharacterId, KeyCode)
        );

        CREATE TABLE Options (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Scope INTEGER NOT NULL,
            ScopeId INTEGER,
            Key TEXT NOT NULL,
            Value TEXT NOT NULL,
            UNIQUE(Scope, ScopeId, Key)
        );

        CREATE INDEX IX_Characters_MudId ON Characters(MudId);
        CREATE INDEX IX_Aliases_CharacterId ON Aliases(CharacterId);
        CREATE INDEX IX_Triggers_CharacterId ON Triggers(CharacterId);
        CREATE INDEX IX_Paths_CharacterId ON Paths(CharacterId);
        CREATE INDEX IX_Movements_CharacterId ON Movements(CharacterId);
        CREATE INDEX IX_Options_Scope ON Options(Scope, ScopeId);
        """;
}
