using Omnimud.Core.Session;
using Omnimud.Data;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;
using Omnimud.UI.Services.Accessibility;

namespace Omnimud.UI.Presenters;

/// <summary>A column the list can be sorted by. <paramref name="Key"/> is what gets stored.</summary>
public sealed record SortColumn(string Key, string DisplayName);

/// <summary>
/// Shows the add/edit dialog. Receives the element to edit (null = new; after a rejected save, what
/// the user had typed) and every element of the list, and returns the edited copy or null if cancelled.
/// </summary>
public delegate T? EntityEditor<T>(T? current, bool isNew, IReadOnlyList<T> all) where T : class;

/// <summary>
/// Everything a management list (aliases, triggers, paths) does, without a window: loading,
/// keyboard-driven sorting remembered per character, a selection that survives reloads (after a
/// removal, the neighbour), add/edit/remove with duplicates turned into a message, and
/// announcements for changes the focus does not reflect.
/// </summary>
public abstract class EntityListPresenter<T> where T : class
{
    private readonly IListSortStore _sortStore;
    private readonly string _listKey;
    private readonly IAnnouncer? _announcer;
    private List<T> _items = [];

    protected EntityListPresenter(int characterId, string listKey, IUserPrompts prompts, IListSortStore? sortStore, IAnnouncer? announcer)
    {
        CharacterId = characterId;
        Prompts = prompts;
        _listKey = listKey;
        _sortStore = sortStore ?? new MemoryListSortStore();
        _announcer = announcer;
        Sort = new ListSort(string.Empty);
    }

    protected int CharacterId { get; }
    protected IUserPrompts Prompts { get; }

    public IReadOnlyList<T> Items => _items;
    public int SelectedIndex { get; private set; } = -1;
    public T? Selected => SelectedIndex >= 0 && SelectedIndex < _items.Count ? _items[SelectedIndex] : null;
    public ListSort Sort { get; private set; }

    /// <summary>Last thing announced; the form mirrors it in a status label.</summary>
    public string Status { get; private set; } = string.Empty;

    /// <summary>True once anything has been written, so the caller knows the session must reload.</summary>
    public bool DataChanged { get; protected set; }

    /// <summary>Items, selection or status changed: the view must repaint.</summary>
    public event EventHandler? Changed;

    public abstract IReadOnlyList<SortColumn> SortColumns { get; }
    protected abstract ListSort DefaultSort { get; }

    protected abstract Task<IReadOnlyList<T>> FetchAsync(CancellationToken ct);
    protected abstract object KeyOf(T item);
    protected abstract string NameOf(T item);
    protected abstract IComparable? SortValue(T item, string column);
    protected abstract Task InsertAsync(T item, CancellationToken ct);
    protected abstract Task UpdateAsync(T item, CancellationToken ct);
    protected abstract Task DeleteAsync(T item, CancellationToken ct);
    protected abstract string ConfirmRemoveMessage(T item);
    protected abstract string DuplicateMessage(T item);

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var stored = await _sortStore.LoadAsync(CharacterId, _listKey, ct);
        Sort = stored is not null && SortColumns.Any(c => c.Key == stored.Column) ? stored : DefaultSort;
        await ReloadAsync(Selected is { } s ? KeyOf(s) : null, 0, ct);
    }

    /// <summary>Re-reads the list keeping the selected element (after an import, for instance).</summary>
    public Task RefreshAsync(CancellationToken ct = default) =>
        ReloadAsync(Selected is { } s ? KeyOf(s) : null, Math.Max(SelectedIndex, 0), ct);

    protected async Task ReloadAsync(object? selectKey, int fallbackIndex, CancellationToken ct = default)
    {
        var fetched = await FetchAsync(ct);
        _items = [.. fetched.Order(Comparer<T>.Create(Compare))]; // Order is stable: ties keep the repository order

        // A list is never left without a selected element: keyboard users would lose their place.
        var index = selectKey is null ? -1 : _items.FindIndex(i => KeyOf(i).Equals(selectKey));
        SelectedIndex = _items.Count == 0 ? -1 : index >= 0 ? index : Math.Clamp(fallbackIndex, 0, _items.Count - 1);
        OnChanged();
    }

    private int Compare(T a, T b)
    {
        var x = SortValue(a, Sort.Column);
        var y = SortValue(b, Sort.Column);
        var result = x is string sx && y is string sy
            ? string.Compare(sx, sy, StringComparison.CurrentCultureIgnoreCase)
            : Comparer<IComparable?>.Default.Compare(x, y);
        return Sort.Descending ? -result : result;
    }

    /// <summary>The view reports what the user selected. Does not raise <see cref="Changed"/>.</summary>
    public void Select(int index)
    {
        if (index >= 0 && index < _items.Count) SelectedIndex = index;
    }

    /// <summary>Sorts by a column; choosing the current one again reverses the direction.</summary>
    public async Task SortByAsync(string column, CancellationToken ct = default)
    {
        var definition = SortColumns.FirstOrDefault(c => c.Key == column);
        if (definition is null) return;

        Sort = Sort.Column == column ? Sort with { Descending = !Sort.Descending } : new ListSort(column);
        await _sortStore.SaveAsync(CharacterId, _listKey, Sort, ct);
        Status = string.Format(Sort.Descending ? Strings.Lst_SortedDescending : Strings.Lst_SortedAscending, definition.DisplayName);
        await RefreshAsync(ct);
        Announce(Status);
    }

    public Task<bool> AddAsync(EntityEditor<T> editor, CancellationToken ct = default) => EditLoopAsync(null, true, editor, ct);

    public Task<bool> EditSelectedAsync(EntityEditor<T> editor, CancellationToken ct = default) =>
        Selected is { } current ? EditLoopAsync(current, false, editor, ct) : Task.FromResult(false);

    private async Task<bool> EditLoopAsync(T? current, bool isNew, EntityEditor<T> editor, CancellationToken ct)
    {
        while (editor(current, isNew, _items) is { } edited)
        {
            try
            {
                if (isNew) await InsertAsync(edited, ct);
                else await UpdateAsync(edited, ct);
                DataChanged = true;
                await ReloadAsync(KeyOf(edited), SelectedIndex, ct);
                return true;
            }
            catch (DuplicateEntityException)
            {
                // Never an exception to the user: say it and reopen the dialog with what was typed.
                Prompts.Warn(DuplicateMessage(edited));
                current = edited;
            }
        }
        return false;
    }

    public async Task<bool> RemoveSelectedAsync(CancellationToken ct = default)
    {
        if (Selected is not { } current) return false;
        if (!Prompts.Confirm(ConfirmRemoveMessage(current))) return false;

        var index = SelectedIndex;
        await DeleteAsync(current, ct);
        DataChanged = true;
        Status = string.Format(Strings.Lst_Removed, NameOf(current));
        await ReloadAsync(null, index, ct); // same position = the neighbour
        Announce(Status);
        return true;
    }

    /// <summary>For imports and other writes made outside the presenter.</summary>
    public void MarkDataChanged() => DataChanged = true;

    protected void SetStatus(string text, bool announce = true)
    {
        Status = text;
        if (announce) Announce(text);
    }

    protected void Announce(string text) => _announcer?.Announce(text, AnnouncePriority.MostRecent);

    protected void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
