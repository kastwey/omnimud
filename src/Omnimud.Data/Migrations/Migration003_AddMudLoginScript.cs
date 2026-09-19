namespace Omnimud.Data.Migrations;

internal static class Migration003_AddMudLoginScript
{
    public const string Sql = """
        ALTER TABLE Muds ADD COLUMN LoginScript TEXT;
        """;
}
