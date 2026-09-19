using Omnimud.Data.Exchange;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Presenters;

/// <summary>Asks the user what to do with the elements of an import that already exist.</summary>
public interface IImportConflictResolver
{
    /// <summary>Receives every item of the analysis. Returns the decisions for the conflicting ones, or null to cancel the import.</summary>
    IReadOnlyDictionary<string, ImportDecision>? Resolve(IReadOnlyList<ImportItem> items);
}

/// <summary>One conflicting element and what the user wants to do with it.</summary>
public sealed class ImportConflictRow(ImportItem item, string text)
{
    public ImportItem Item { get; } = item;
    public string Key => Item.Key;
    /// <summary>"Alias: look", "Character: Gandalf (MUD Reinos)"...</summary>
    public string Text { get; } = text;
    /// <summary>False (the default) = skip: nothing is ever overwritten without the user saying so.</summary>
    public bool Overwrite { get; set; }

    public override string ToString() => Text;
}

/// <summary>
/// Logic of the import conflicts dialog, generic for any .omnimud import or copy between
/// characters: takes the analyzed items, keeps the conflicting ones and produces the decisions.
/// </summary>
public sealed class ImportConflictsPresenter
{
    public ImportConflictsPresenter(IEnumerable<ImportItem> items)
    {
        var all = items.ToList();
        var byKey = all.GroupBy(i => i.Key).ToDictionary(g => g.Key, g => g.First());
        Rows = all.Where(i => i.Status == ImportItemStatus.Conflict)
            .Select(i => new ImportConflictRow(i, Describe(i, i.ParentKey is { } p && byKey.TryGetValue(p, out var parent) ? parent : null)))
            .ToList();
    }

    public IReadOnlyList<ImportConflictRow> Rows { get; }
    public bool HasConflicts => Rows.Count > 0;

    public int OverwriteCount => Rows.Count(r => r.Overwrite);
    public int SkipCount => Rows.Count - OverwriteCount;

    public void SetOverwrite(int index, bool overwrite)
    {
        if (index >= 0 && index < Rows.Count)
            Rows[index].Overwrite = overwrite;
    }

    public void SetAll(bool overwrite)
    {
        foreach (var row in Rows)
            row.Overwrite = overwrite;
    }

    /// <summary>One explicit decision per conflicting element.</summary>
    public IReadOnlyDictionary<string, ImportDecision> Decisions =>
        Rows.ToDictionary(r => r.Key, r => r.Overwrite ? ImportDecision.Overwrite : ImportDecision.Skip);

    /// <summary>"3 elements: 1 will be overwritten, 2 skipped."</summary>
    public string Status => string.Format(Strings.ImportConflicts_Status, Rows.Count, OverwriteCount, SkipCount);

    public static string KindName(ImportItemKind kind) => kind switch
    {
        ImportItemKind.Mud => Strings.Exchange_KindMud,
        ImportItemKind.MessageRuleSet => Strings.Exchange_KindRuleSet,
        ImportItemKind.Character => Strings.Exchange_KindCharacter,
        ImportItemKind.Alias => Strings.Exchange_KindAlias,
        ImportItemKind.Trigger => Strings.Exchange_KindTrigger,
        ImportItemKind.Path => Strings.Exchange_KindPath,
        ImportItemKind.Movements => Strings.Exchange_KindMovements,
        ImportItemKind.Options => Strings.Exchange_KindOptions,
        _ => kind.ToString(),
    };

    private static string Describe(ImportItem item, ImportItem? parent)
    {
        var text = string.IsNullOrWhiteSpace(item.Name)
            ? KindName(item.Kind)
            : string.Format(Strings.Exchange_ItemFormat, KindName(item.Kind), item.Name);
        return parent is null ? text : string.Format(Strings.Exchange_ItemInParent, text, KindName(parent.Kind), parent.Name);
    }
}
