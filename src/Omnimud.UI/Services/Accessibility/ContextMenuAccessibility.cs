namespace Omnimud.UI.Services.Accessibility;

/// <summary>
/// Makes a <see cref="ContextMenuStrip"/> speak when it opens. Two things of WinForms (checked on .NET 10 by listening
/// to MSAA and UI Automation from another process) leave a screen reader silent:
/// <list type="number">
/// <item>The "menu opened" events are only raised when the accessible object of the menu already exists, and nobody
/// has asked for it the first time the menu opens.</item>
/// <item>No item is selected when the menu opens, so nothing gets the focus: NVDA says nothing until an arrow is
/// pressed, and JAWS says "menu" with no item. (The menu bar does not have this problem: it selects the first item
/// of a submenu itself.)</item>
/// </list>
/// So, before it opens, the accessible objects of the menu and of its items are created; and once open, the first
/// item that can be chosen is selected, which raises a real focus event on it.
/// </summary>
internal static class ContextMenuAccessibility
{
    /// <summary>
    /// Call once, after any handler that fills the menu on <see cref="ToolStripDropDown.Opening"/> (handlers run in
    /// subscription order). <paramref name="openedWithMouse"/> lets a caller that knows it keep the usual look of a
    /// menu opened with a right click, with nothing highlighted; by default the first item is always selected.
    /// </summary>
    public static void Attach(ContextMenuStrip menu, Func<bool>? openedWithMouse = null)
    {
        ArgumentNullException.ThrowIfNull(menu);
        menu.Opening += (_, e) =>
        {
            if (!e.Cancel) CreateAccessibleObjects(menu);
        };
        menu.Opened += (_, _) =>
        {
            if (openedWithMouse?.Invoke() != true) SelectFirst(menu);
        };
    }

    internal static void CreateAccessibleObjects(ToolStrip strip)
    {
        _ = strip.AccessibilityObject;
        foreach (ToolStripItem item in strip.Items)
            _ = item.AccessibilityObject;
    }

    /// <summary>Selects the first item that can be chosen. Returns it, or null when there is none.</summary>
    internal static ToolStripItem? SelectFirst(ToolStrip strip)
    {
        foreach (ToolStripItem item in strip.Items)
        {
            // A disabled item never raises the focus event (WinForms checks Enabled), and a separator cannot be selected.
            if (item is ToolStripSeparator || !item.Available || !item.Enabled || !item.CanSelect) continue;
            item.Select();
            return item;
        }
        return null;
    }
}
