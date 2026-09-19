using System.Data.Common;
using Dapper;
using Microsoft.Data.Sqlite;
using Omnimud.Data.Seed;

namespace Omnimud.Data.Migrations;

/// <summary>
/// A message rule set can be a Lua script instead of a list of patterns: MessageRuleSets.Script
/// (NULL or blank = patterns). The four built-in sets become scripts, faithful ports of the original
/// client's processing rules; they are found by name among the sets marked IsBuiltIn. Their pattern
/// rules stay in the table (a script set does not evaluate them) so that a duplicate can be switched
/// back to patterns. Sets created by the user are not touched.
/// </summary>
internal static class Migration006_MessageRuleScripts
{
    public static async Task ApplyAsync(SqliteConnection connection, DbTransaction transaction, CancellationToken ct)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            "ALTER TABLE MessageRuleSets ADD COLUMN Script TEXT NULL", transaction: transaction, cancellationToken: ct)).ConfigureAwait(false);

        foreach (var set in BuiltInMessageRuleSets.All)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE MessageRuleSets SET Script = @Script WHERE IsBuiltIn = 1 AND Name = @Name COLLATE NOCASE",
                new { set.Name, set.Script }, transaction, cancellationToken: ct)).ConfigureAwait(false);
        }
    }
}
