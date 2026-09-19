using Omnimud.Data.Repositories;

namespace Omnimud.UI.Presenters;

/// <summary>Column and direction a list is sorted by. Stored as "column;asc" / "column;desc", like the original client did.</summary>
public sealed record ListSort(string Column, bool Descending = false)
{
    public string Serialize() => $"{Column};{(Descending ? "desc" : "asc")}";

    public static ListSort? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split(';');
        if (parts.Length != 2 || parts[0].Length == 0) return null;
        return parts[1] switch
        {
            "asc" => new ListSort(parts[0]),
            "desc" => new ListSort(parts[0], true),
            _ => null,
        };
    }
}

/// <summary>Remembers, per character, how each management list is sorted.</summary>
public interface IListSortStore
{
    Task<ListSort?> LoadAsync(int characterId, string listKey, CancellationToken ct = default);
    Task SaveAsync(int characterId, string listKey, ListSort sort, CancellationToken ct = default);
}

/// <summary>Used when no database store is given: the order lasts as long as the process.</summary>
public sealed class MemoryListSortStore : IListSortStore
{
    private readonly Dictionary<(int, string), ListSort> _values = [];

    public Task<ListSort?> LoadAsync(int characterId, string listKey, CancellationToken ct = default)
    {
        lock (_values) return Task.FromResult(_values.GetValueOrDefault((characterId, listKey)));
    }

    public Task SaveAsync(int characterId, string listKey, ListSort sort, CancellationToken ct = default)
    {
        lock (_values) _values[(characterId, listKey)] = sort;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Stores the order in the Options table (everything lives in the database, nothing in the Registry).
/// It uses a scope number of its own: scopes 0-2 belong to <c>OptionsService</c>, which treats "has any
/// row" as "has its own options" and rewrites the whole scope when saving, so interface state must
/// not be mixed with them.
/// </summary>
public sealed class OptionListSortStore(IOptionRepository options) : IListSortStore
{
    /// <summary>Interface state of a character (ScopeId = character id).</summary>
    public const int UiStateScope = 102;

    private static string Key(string listKey) => "ui.sort." + listKey;

    public async Task<ListSort?> LoadAsync(int characterId, string listKey, CancellationToken ct = default) =>
        ListSort.TryParse(await options.GetValueAsync(UiStateScope, characterId, Key(listKey), ct).ConfigureAwait(false));

    public Task SaveAsync(int characterId, string listKey, ListSort sort, CancellationToken ct = default) =>
        options.SetValueAsync(UiStateScope, characterId, Key(listKey), sort.Serialize(), ct);
}
