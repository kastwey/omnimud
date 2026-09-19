using System.Reflection;
using Omnimud.Core.Session;
using Omnimud.UI.Controls;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;

namespace Omnimud.UI.Forms;

// Menus. Every keyboard shortcut of the window is a menu item so it can be discovered with a
// screen reader by walking the menus.
partial class FrmGame
{
    private ToolStripMenuItem _miSave = null!;
    private ToolStripMenuItem _miQuit = null!;
    private ToolStripMenuItem _miReconnect = null!;
    private ToolStripMenuItem _miCopy = null!;
    private ToolStripMenuItem _miCut = null!;
    private ToolStripMenuItem _miPaste = null!;
    private ToolStripMenuItem _miUndo = null!;
    private ToolStripMenuItem _miFind = null!;
    private ToolStripMenuItem _miFindNext = null!;
    private ToolStripMenuItem _miToggleMessages = null!;
    private ToolStripMenuItem _miMovement = null!;
    private ToolStripMenuItem _miSilent = null!;
    private ToolStripMenuItem _miMute = null!;

    private sealed record FindMemory(string Text, bool MatchCase, bool SearchUp);
    private readonly Dictionary<AnsiTerminalBox, FindMemory> _findMemory = [];

    private void BuildMenu()
    {
        // "Actions" is what the MUD offers (FrmGame.Actions.cs); the client's own commands live in "Game".
        var actions = BuildActionsMenu();

        var game = new ToolStripMenuItem(Strings.Menu_Game) { Name = "_miGame" };
        _miSave = Item(Strings.Menu_GameSave, Keys.F3, () => Run(_session.SendSaveCommandAsync));
        _miQuit = Item(Strings.Menu_GameQuit, Keys.F4, () => Run(_session.SendQuitCommandAsync));
        _miReconnect = Item(Strings.Menu_GameReconnect, Keys.None, () => Run(ConnectAsync));
        game.DropDownItems.AddRange([_miSave, _miQuit, new ToolStripSeparator(), _miReconnect,
            Item(Strings.Menu_GameClose, Keys.None, Close)]);

        var edit = new ToolStripMenuItem(Strings.Menu_Edit);
        _miCopy = Item(Strings.Menu_EditCopy, Keys.Control | Keys.C, () => ActiveText()?.Copy());
        _miCut = Item(Strings.Menu_EditCut, Keys.Control | Keys.X, () => ActiveText()?.Cut());
        _miPaste = Item(Strings.Menu_EditPaste, Keys.Control | Keys.V, () => ActiveText()?.Paste());
        _miUndo = Item(Strings.Menu_EditUndo, Keys.Control | Keys.Z, () => ActiveText()?.Undo());
        _miFind = Item(Strings.Menu_EditFind, Keys.Control | Keys.B, Find);
        _miFindNext = Item(Strings.Menu_EditFindNext, Keys.Control | Keys.S, FindNext);
        edit.DropDownItems.AddRange([_miCopy, _miCut, _miPaste,
            Item(Strings.Menu_EditSelectAll, Keys.Control | Keys.E, () => ActiveText()?.SelectAll()),
            _miUndo, new ToolStripSeparator(), _miFind, _miFindNext, new ToolStripSeparator(),
            Item(Strings.Menu_EditClear, Keys.Control | Keys.L, _terminal.Clear)]);
        edit.DropDownOpening += (_, _) => UpdateEditMenu();

        var view = new ToolStripMenuItem(Strings.Menu_View);
        _miToggleMessages = Item(Strings.Menu_ViewHideMessages, Keys.None, ToggleMessagesPanel);
        view.DropDownItems.AddRange([
            Item(Strings.Menu_ViewFocusInput, Keys.Control | Keys.K, () => _txtInput.Focus()),
            new ToolStripSeparator(),
            Item(Strings.Menu_ViewIncrease, Keys.Control | Keys.Oemplus, () => ResizeOutput(+10)),
            Item(Strings.Menu_ViewDecrease, Keys.Control | Keys.OemMinus, () => ResizeOutput(-10)),
            Item(Strings.Menu_ViewReset, Keys.None, ResetSizes),
            _miToggleMessages]);

        var tools = new ToolStripMenuItem(Strings.Menu_Tools);
        _miMovement = Item(Strings.Menu_ToolsMovement, Keys.F2, () => Run(() => _session.SetMovementModeAsync(!_session.MovementMode)));
        _miSilent = Item(Strings.Menu_ToolsSilent, Keys.F8, () => _session.SilentMode = !_session.SilentMode);
        _miMute = Item(Strings.Menu_ToolsMute, Keys.Control | Keys.M, ToggleMute);
        tools.DropDownItems.AddRange([_miMovement,
            Item(Strings.Menu_ToolsAliases, Keys.F5, OpenAliases),
            Item(Strings.Menu_ToolsTriggers, Keys.F6, OpenTriggers),
            Item(Strings.Menu_ToolsPaths, Keys.F7, OpenPaths),
            _miSilent,
            Item(Strings.Menu_ToolsOptions, Keys.F9, () => OpenDialog(needsCharacter: false, () => _dialogs.ShowOptions(this, _session.Profile))),
            new ToolStripSeparator(),
            Item(Strings.Menu_ToolsMovementKeys, Keys.None, () => OpenDialog(needsCharacter: false, () => _dialogs.ShowMovementKeys(this, _session.Profile))),
            Item(Strings.Menu_ToolsDirections, Keys.None, () => OpenDialog(needsCharacter: false, () => _dialogs.ShowDirections(this, _session.Profile))),
            new ToolStripSeparator(),
            _miMute,
            Item(Strings.Menu_ToolsStopSpeech, Keys.Alt | Keys.S, _announcer_StopSpeech)]);

        var help = new ToolStripMenuItem(Strings.Menu_Help);
        help.DropDownItems.AddRange([
            Item(Strings.Menu_HelpManual, Keys.F1, () => OpenHelpFile("manual.html")),
            Item(Strings.Menu_HelpLua, Keys.None, () => OpenHelpFile("API_LUA.md")),
            new ToolStripSeparator(),
            Item(Strings.Menu_HelpAbout, Keys.None, ShowAbout)]);

        _menu.Items.AddRange([actions, game, edit, view, tools, help]);
    }

    private static ToolStripMenuItem Item(string text, Keys shortcut, Action onClick)
    {
        var item = new ToolStripMenuItem(text) { ShortcutKeys = shortcut };
        item.Click += (_, _) => onClick();
        return item;
    }

    private void Run(Func<Task> action) => ErrorReporter.Run(this, action);

    // ── Edit ───────────────────────────────────────────────────────────────

    private TextBoxBase? ActiveText() => ActiveControl as TextBoxBase ?? FocusedTextBox();

    private TextBoxBase? FocusedTextBox() =>
        _txtInput.Focused ? _txtInput : _terminal.Focused ? _terminal : _rtbMessages.Focused ? _rtbMessages : null;

    private AnsiTerminalBox? ActiveReadOnlyBox() => ActiveText() as AnsiTerminalBox;

    private void UpdateEditMenu()
    {
        var box = ActiveText();
        var editable = box is TextBox { ReadOnly: false };
        // The context menu of the boxes offers some of the same commands: one rule for both.
        _miCopy.Enabled = _cmCopy.Enabled = box is { SelectionLength: > 0 };
        _miCut.Enabled = editable && box!.SelectionLength > 0;
        _miPaste.Enabled = editable && Clipboard.ContainsText();
        _miUndo.Enabled = editable && box!.CanUndo;
        _miFind.Enabled = _cmFind.Enabled = box is AnsiTerminalBox;
        _miFindNext.Enabled = _cmFindNext.Enabled = box is AnsiTerminalBox terminal && _findMemory.ContainsKey(terminal);
    }

    private void Find()
    {
        if (ActiveReadOnlyBox() is not { } box) return;

        _findMemory.TryGetValue(box, out var memory);
        var initial = box.SelectionLength is > 0 and <= 255 ? box.SelectedText : memory?.Text;
        using var dialog = new FrmFind(box.AccessibleName ?? string.Empty, initial, memory?.MatchCase ?? false, memory?.SearchUp ?? false);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var search = new FindMemory(dialog.SearchText, dialog.MatchCase, dialog.SearchUp);
        if (Search(box, search))
            _findMemory[box] = search;
        else
            MessageBox.Show(this, Strings.Find_NotFound, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        box.Focus();
    }

    private void FindNext()
    {
        if (ActiveReadOnlyBox() is not { } box || !_findMemory.TryGetValue(box, out var memory)) return;
        if (!Search(box, memory)) System.Media.SystemSounds.Exclamation.Play();
    }

    private static bool Search(AnsiTerminalBox box, FindMemory search)
    {
        var options = RichTextBoxFinds.None;
        if (search.MatchCase) options |= RichTextBoxFinds.MatchCase;

        int found;
        if (search.SearchUp)
        {
            if (box.SelectionStart <= 0) return false;
            found = box.Find(search.Text, 0, box.SelectionStart, options | RichTextBoxFinds.Reverse);
        }
        else
        {
            var from = box.SelectionStart + box.SelectionLength;
            if (from >= box.TextLength) return false;
            found = box.Find(search.Text, from, options);
        }

        if (found < 0) return false;
        box.Select(found, search.Text.Length);
        box.ScrollToCaret();
        return true;
    }

    // ── View ───────────────────────────────────────────────────────────────

    private const int MessagesRow = 1;
    private const int OutputRow = 3;

    private void ResizeOutput(int delta)
    {
        if (_messagesHiddenByUser) return;
        var output = Math.Clamp(_layout.RowStyles[OutputRow].Height + delta, 20F, 90F);
        _layout.RowStyles[OutputRow].Height = output;
        _layout.RowStyles[MessagesRow].Height = 100F - output;
    }

    private void ResetSizes()
    {
        if (_messagesHiddenByUser) ToggleMessagesPanel();
        _layout.RowStyles[OutputRow].Height = 70F;
        _layout.RowStyles[MessagesRow].Height = 30F;
    }

    private void ToggleMessagesPanel()
    {
        _messagesHiddenByUser = !_messagesHiddenByUser;
        var visible = !_messagesHiddenByUser;
        if (!visible && _rtbMessages.Focused) _txtInput.Focus();

        _lblMessages.Visible = visible;
        _rtbMessages.Visible = visible;
        _layout.RowStyles[MessagesRow] = visible ? new RowStyle(SizeType.Percent, 30F) : new RowStyle(SizeType.Absolute, 0F);
        _layout.RowStyles[OutputRow].Height = visible ? 70F : 100F;
        _miToggleMessages.Text = visible ? Strings.Menu_ViewHideMessages : Strings.Menu_ViewShowMessages;
    }

    // ── Tools ──────────────────────────────────────────────────────────────

    private void OpenAliases() => OpenDialog(needsCharacter: true, () => _dialogs.ShowAliases(this, _session.Profile));
    private void OpenTriggers() => OpenDialog(needsCharacter: true, () => _dialogs.ShowTriggers(this, _session.Profile));
    private void OpenPaths() => OpenDialog(needsCharacter: true, () => _dialogs.ShowPaths(this, _session.Profile));

    private void OpenDialog(bool needsCharacter, Func<bool> show)
    {
        if (needsCharacter && _session.Profile.CharacterId is null)
        {
            MessageBox.Show(this, Strings.Client_NeedsCharacter, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!show()) return;
        Run(async () =>
        {
            await _session.ReloadAsync();
            ApplyOptions();
        });
    }

    private void ToggleMute()
    {
        // Say it before muting and after unmuting, so the change is always heard.
        if (!_announcer.Muted) _announcer.Announce(Strings.Client_MutedOn, AnnouncePriority.Interrupt);
        _announcer.Muted = !_announcer.Muted;
        _miMute.Checked = _announcer.Muted;
        if (!_announcer.Muted) _announcer.Announce(Strings.Client_MutedOff, AnnouncePriority.Interrupt);
    }

    private void _announcer_StopSpeech() => _announcer.StopSpeech();

    // ── Help ───────────────────────────────────────────────────────────────

    private void OpenHelpFile(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "docs", fileName);
        if (!File.Exists(path))
        {
            MessageBox.Show(this, string.Format(Strings.Help_NotFound, path), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        UrlOpener.OpenFile(path);
    }

    private void ShowAbout()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2";
        MessageBox.Show(this, string.Format(Strings.About_Text, version), Strings.App_Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
