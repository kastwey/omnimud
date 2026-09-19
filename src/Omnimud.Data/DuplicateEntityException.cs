using Microsoft.Data.Sqlite;

namespace Omnimud.Data;

/// <summary>Thrown instead of a raw <see cref="SqliteException"/> when a UNIQUE or PRIMARY KEY constraint is violated.</summary>
public sealed class DuplicateEntityException : Exception
{
    public DuplicateEntityException(string entityName, string? key, Exception? innerException = null)
        : base(key is null
            ? $"A {entityName} with the same key already exists."
            : $"A {entityName} with the key '{key}' already exists.", innerException)
    {
        EntityName = entityName;
        Key = key;
    }

    /// <summary>Entity type, e.g. "Mud", "Alias".</summary>
    public string EntityName { get; }

    /// <summary>The duplicated value, when known.</summary>
    public string? Key { get; }
}

internal static class SqliteErrors
{
    private const int SqliteConstraint = 19;
    private const int SqliteConstraintPrimaryKey = 1555;
    private const int SqliteConstraintUnique = 2067;

    public static bool IsUniqueViolation(SqliteException ex) =>
        ex.SqliteErrorCode == SqliteConstraint
        && (ex.SqliteExtendedErrorCode is SqliteConstraintUnique or SqliteConstraintPrimaryKey
            // Extended codes can be disabled; the message is stable across SQLite versions.
            || ex.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase));

    public static async Task<T> GuardAsync<T>(string entityName, string? key, Func<Task<T>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (SqliteException ex) when (IsUniqueViolation(ex))
        {
            throw new DuplicateEntityException(entityName, key, ex);
        }
    }

    public static async Task GuardAsync(string entityName, string? key, Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (SqliteException ex) when (IsUniqueViolation(ex))
        {
            throw new DuplicateEntityException(entityName, key, ex);
        }
    }
}
