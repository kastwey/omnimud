namespace Omnimud.Data.Migrations;

/// <summary>
/// Movement keys are no longer only the numeric keypad: codes 10-17 are the arrows, Page Up,
/// Page Down, Home and End (see Omnimud.Core.Session.MovementKey). SQLite cannot alter a CHECK,
/// so the table is rebuilt with every row (and its Id) copied. Nothing references Movements.
/// </summary>
internal static class Migration005_MovementKeys
{
    public const string Sql = """
        CREATE TABLE Movements_new (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            MudId INTEGER NULL REFERENCES Muds(Id) ON DELETE CASCADE,
            CharacterId INTEGER NULL REFERENCES Characters(Id) ON DELETE CASCADE,
            KeyCode INTEGER NOT NULL CHECK (KeyCode BETWEEN 0 AND 17),
            Command TEXT NOT NULL,
            CHECK ((MudId IS NULL) <> (CharacterId IS NULL)),
            UNIQUE (MudId, KeyCode),
            UNIQUE (CharacterId, KeyCode)
        );

        INSERT INTO Movements_new (Id, MudId, CharacterId, KeyCode, Command)
        SELECT Id, MudId, CharacterId, KeyCode, Command
        FROM Movements
        ORDER BY Id;

        DROP TABLE Movements;
        ALTER TABLE Movements_new RENAME TO Movements;
        """;
}
