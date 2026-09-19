using Omnimud.Core.Actions;

namespace Omnimud.UI.Services;

/// <summary>
/// Turns the tree of <see cref="ActionMenuNode"/> into WinForms menu items. No state: the main
/// "Actions" menu and the context menu of the Received box share it, so both always show the same.
///
/// Labels come from the MUD: "&amp;" is escaped so that it never becomes a mnemonic (letters chosen
/// by a server would collide at random; menus already jump to an entry by its first letter).
/// </summary>
public static class ActionMenuRenderer
{
    /// <summary>The text of a menu item that shows <paramref name="label"/> literally.</summary>
    public static string EscapeLabel(string label) => label.Replace("&", "&&");

    /// <summary>One item per node, submenus included, already complete. <paramref name="onChosen"/> gets the action's node.</summary>
    public static ToolStripItem[] Render(IReadOnlyList<ActionMenuNode> nodes, Action<ActionMenuNode> onChosen)
    {
        var items = new ToolStripItem[nodes.Count];
        for (var i = 0; i < nodes.Count; i++)
            items[i] = Render(nodes[i], onChosen);
        return items;
    }

    private static ToolStripMenuItem Render(ActionMenuNode node, Action<ActionMenuNode> onChosen)
    {
        var item = new ToolStripMenuItem(EscapeLabel(node.Label)) { Tag = node };

        if (node.IsSubmenu)
            item.DropDownItems.AddRange(Render(node.Children, onChosen));
        else if (node.IsAction)
            item.Click += (_, _) => onChosen(node);
        else
            item.Enabled = false; // information: it can be read, not chosen

        return item;
    }

    /// <summary>
    /// Replaces the items of <paramref name="target"/> from <paramref name="keep"/> on (the first
    /// ones are fixed entries of the menu) and disposes the ones that leave.
    /// </summary>
    public static void Replace(ToolStripItemCollection target, int keep, ToolStripItem[] items)
    {
        for (var i = target.Count - 1; i >= keep; i--)
        {
            var old = target[i];
            target.RemoveAt(i);
            old.Dispose();
        }
        target.AddRange(items);
    }
}
