namespace Omnimud.Core.Actions;

/// <summary>
/// What the player can do right now according to the MUD, as a tree of menus: one submenu per
/// section (Inventory, Exits...). Immutable snapshot: the session replaces it as a whole, so the
/// window can read it from its own thread at any time.
/// </summary>
public sealed class ActionMenu
{
    public static ActionMenu Empty { get; } = new([]);

    public ActionMenu(IReadOnlyList<ActionMenuNode> sections)
    {
        Sections = sections;
    }

    /// <summary>First level of the menu. Sections without content are not here.</summary>
    public IReadOnlyList<ActionMenuNode> Sections { get; }

    public bool IsEmpty => Sections.Count == 0;
}
