using System.ComponentModel;
using Omnimud.Core.Actions;
using Omnimud.UI.Controls;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;
using Omnimud.UI.Services.Accessibility;

namespace Omnimud.UI.Forms;

// The "Actions" menu (what the MUD says can be done: inventory, exits...) and the context menu of
// the Received and Messages boxes. The window only decides when to build them and what to do with
// the chosen action; the items themselves come from ActionMenuRenderer.
partial class FrmGame
{
    private ToolStripMenuItem _miActions = null!;
    private ContextMenuStrip _boxMenu = null!;
    private ToolStripMenuItem _cmCopy = null!;
    private ToolStripMenuItem _cmFind = null!;
    private ToolStripMenuItem _cmFindNext = null!;
    private int _boxMenuFixedItems;
    private bool _boxMenuFromMouse;

    // ── Main menu ──────────────────────────────────────────────────────────

    private ToolStripMenuItem BuildActionsMenu()
    {
        _miActions = new ToolStripMenuItem(Strings.Menu_Actions) { Name = "_miActions", Available = false };
        // Built from the session's snapshot every time it opens, and never touched while it is open:
        // nothing moves under the user's cursor however often the MUD sends packages.
        _miActions.DropDownOpening += (_, _) => FillActionsMenu();
        _miActions.DropDownClosed += (_, _) => UpdateActionsMenu();
        return _miActions;
    }

    internal void FillActionsMenu()
        => ActionMenuRenderer.Replace(_miActions.DropDownItems, 0, ActionMenuRenderer.Render(_session.ActionMenu.Sections, RunAction));

    /// <summary>Shows the Actions menu only while there is something in it. Silent on purpose: an
    /// announcement every time the inventory changes would interrupt the reading of the game.</summary>
    private void UpdateActionsMenu()
    {
        // Open right now: it stays as it is until it closes (DropDownClosed comes back here).
        if (_miActions.DropDown.Visible) return;

        var hasContent = !_session.ActionMenu.IsEmpty;
        // A top-level item without children does not open with its mnemonic; the real content replaces this on opening.
        if (hasContent && _miActions.DropDownItems.Count == 0)
            _miActions.DropDownItems.Add(new ToolStripMenuItem("…") { Enabled = false });
        _miActions.Available = hasContent;
    }

    private void OnActionMenuChanged() => Ui(UpdateActionsMenu);

    private void RunAction(ActionMenuNode node)
    {
        Run(() => _session.ExecuteActionAsync(node));
        FocusInputAfterMenu();
    }

    /// <summary>A menu gives the focus back to whoever had it when it closes, so the input box takes it afterwards.</summary>
    private void FocusInputAfterMenu()
    {
        if (IsHandleCreated) BeginInvoke(() => _txtInput.Select());
        else _txtInput.Select();
    }

    // ── Context menu of Received and Messages ──────────────────────────────

    private void BuildBoxMenu()
    {
        _boxMenu = new ContextMenuStrip(components) { Name = "_boxMenu" };
        _cmCopy = ContextItem(Strings.Menu_EditCopy, Keys.Control | Keys.C, () => ActiveText()?.Copy());
        _cmFind = ContextItem(Strings.Menu_EditFind, Keys.Control | Keys.B, Find);
        _cmFindNext = ContextItem(Strings.Menu_EditFindNext, Keys.Control | Keys.S, FindNext);
        _boxMenu.Items.AddRange([
            _cmCopy,
            ContextItem(Strings.Menu_EditSelectAll, Keys.Control | Keys.E, () => ActiveText()?.SelectAll()),
            _cmFind,
            _cmFindNext]);
        _boxMenuFixedItems = _boxMenu.Items.Count;
        // Like the main menu: built when it opens, from the snapshot of that moment, and left alone while open.
        _boxMenu.Opening += (_, _) =>
        {
            if (_boxMenu.SourceControl is AnsiTerminalBox box) PrepareBoxMenu(box);
        };
        // After the handler that fills it: the screen reader hears the menu and its first item as soon as it opens.
        ContextMenuAccessibility.Attach(_boxMenu, () => _boxMenuFromMouse);

        // The boxes ask for the menu themselves (right click, Applications key, Shift+F10), so that from
        // the keyboard it opens next to the caret and not wherever the mouse happens to be.
        _terminal.ContextMenuRequested += location => ShowBoxMenu(_terminal, location);
        _rtbMessages.ContextMenuRequested += location => ShowBoxMenu(_rtbMessages, location);
    }

    /// <summary>The shortcut is only shown: the key itself belongs to the same command of the Edit menu.</summary>
    private static ToolStripMenuItem ContextItem(string text, Keys shortcut, Action onClick)
    {
        var item = new ToolStripMenuItem(text)
        {
            ShortcutKeyDisplayString = TypeDescriptor.GetConverter(typeof(Keys)).ConvertToString(shortcut),
        };
        item.Click += (_, _) => onClick();
        return item;
    }

    /// <summary>Edit commands for the box, enabled like the Edit menu; for Received, also the actions.</summary>
    internal void PrepareBoxMenu(AnsiTerminalBox box)
    {
        // The edit commands act on the active box: a right click does not always move the focus.
        box.Select();
        UpdateEditMenu();

        var sections = box == _terminal ? _session.ActionMenu.Sections : [];
        ToolStripItem[] actions = sections.Count == 0
            ? []
            : [new ToolStripSeparator(), .. ActionMenuRenderer.Render(sections, RunAction)];
        ActionMenuRenderer.Replace(_boxMenu.Items, _boxMenuFixedItems, actions);
    }

    /// <param name="location">Client point of the right click; null when asked from the keyboard
    /// (Applications key, Shift+F10): then it opens under the caret, where the user is reading.</param>
    private void ShowBoxMenu(AnsiTerminalBox box, Point? location)
    {
        _boxMenuFromMouse = location is not null;
        _boxMenu.Show(box, location ?? box.KeyboardMenuLocation());
    }

    internal ContextMenuStrip BoxMenu => _boxMenu;
}
