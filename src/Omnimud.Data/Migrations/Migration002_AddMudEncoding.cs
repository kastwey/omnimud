namespace Omnimud.Data.Migrations;

internal static class Migration002_AddMudEncoding
{
    public const string Sql = """
        ALTER TABLE Muds ADD COLUMN Encoding TEXT NOT NULL DEFAULT 'utf-8';
        """;
}
