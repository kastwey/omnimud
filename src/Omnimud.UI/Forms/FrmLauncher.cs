using Microsoft.Extensions.DependencyInjection;
using Omnimud.Core.Options;
using Omnimud.Core.Reports;
using Omnimud.Core.Security;
using Omnimud.Core.Session;
using Omnimud.Core.Updates;
using Omnimud.Data.Options;
using Omnimud.Data.Exchange;
using Omnimud.Data.Repositories;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;
using Omnimud.UI.Services.Accessibility;

namespace Omnimud.UI.Forms;

/// <summary>
/// Start window: tree of MUDs with their characters, connect, manage, import and export.
/// Only binds controls; everything it does is a <see cref="LauncherCommand"/> of <see cref="LauncherPresenter"/>,
/// the same one whether it comes from a button, the main menu, the context menu or a key.
/// </summary>
public sealed class FrmLauncher : Form
{
    private readonly Func<IGameWindowFactory> _windows;
    private readonly IMessageRuleRepository _ruleRepo;
    private readonly IOptionRepository _optionRepo;
    private readonly IUserPrompts _prompts;
    private readonly LauncherPresenter _presenter;
    private readonly Func<FrmOptions>? _globalOptionsDialog;
    private readonly IAppDialogs _app;
    private readonly UpdateCheckPresenter? _updates;
    private ToolStripMenuItem? _miCheckOnStartup;

    private readonly LauncherTreeView _tree;
    private readonly ContextMenuStrip _treeMenu;
    private readonly Label _lblStatus;
    private readonly List<(Control Control, LauncherCommand Command)> _commandControls = [];
    private readonly List<(ToolStripMenuItem Item, LauncherCommand Command)> _commandItems = [];
    private bool _painting;
    private bool _emptyAreaMenu;
    private bool _treeMenuFromMouse;

    /// <summary>The constructor the container resolves today: what is missing comes from <paramref name="services"/>.</summary>
    public FrmLauncher(IServiceProvider services, IMudRepository mudRepo, ICharacterRepository charRepo, IPasswordProtector protector)
        : this(services, mudRepo, charRepo, protector,
            services.GetRequiredService<IMessageRuleRepository>(),
            services.GetRequiredService<IExchangeService>(),
            services.GetRequiredService<IOptionRepository>(),
            services.GetService<IUserPrompts>() ?? new WinFormsUserPrompts())
    {
    }

    /// <summary><paramref name="services"/> is only used to create game windows (<see cref="IGameWindowFactory"/>) when connecting.</summary>
    public FrmLauncher(IServiceProvider services, IMudRepository mudRepo, ICharacterRepository charRepo, IPasswordProtector protector,
        IMessageRuleRepository ruleRepo, IExchangeService exchange, IOptionRepository optionRepo, IUserPrompts prompts)
        : this(mudRepo, charRepo, protector, ruleRepo, exchange, optionRepo, prompts,
            () => services.GetRequiredService<IGameWindowFactory>(), dialogs: null, conflicts: null,
            app: services.GetService<IAppDialogs>(), updateChecker: services.GetService<IUpdateChecker>(),
            optionsService: services.GetService<IOptionsService>(), externalLauncher: services.GetService<IExternalLauncher>())
    {
        // The shared options service, so open game windows hear about the change.
        _globalOptionsDialog = () => new FrmOptions(services.GetRequiredService<Omnimud.Core.Options.IOptionsService>(),
            Omnimud.Core.Options.OptionScope.Global, null, string.Empty, prompts, parentMudId: null, exchange: exchange,
            credentials: services.GetService<IProxyCredentialStore>());
    }

    /// <summary>For tests: no container, and the dialogs can be answered without opening a window.</summary>
    internal FrmLauncher(IMudRepository mudRepo, ICharacterRepository charRepo, IPasswordProtector protector,
        IMessageRuleRepository ruleRepo, IExchangeService exchange, IOptionRepository optionRepo, IUserPrompts prompts,
        Func<IGameWindowFactory> windows, ILauncherDialogs? dialogs, IImportConflictResolver? conflicts,
        IAppDialogs? app = null, IUpdateChecker? updateChecker = null, IOptionsService? optionsService = null,
        IExternalLauncher? externalLauncher = null, IUpdateNotice? updateNotice = null)
    {
        _app = app ?? AppDialogs.CreateBasic(prompts);
        // Without a checker (a launcher built by hand) the update entries are simply not there.
        if (updateChecker is not null)
            _updates = new UpdateCheckPresenter(updateChecker, optionsService ?? new OptionsService(optionRepo), prompts,
                updateNotice ?? new LauncherUpdateNotice(this), externalLauncher ?? new ShellExternalLauncher());
        _windows = windows;
        _ruleRepo = ruleRepo;
        _optionRepo = optionRepo;
        _prompts = prompts;
        _presenter = new LauncherPresenter(mudRepo, charRepo, ruleRepo, protector, prompts,
            dialogs ?? new LauncherDialogs(this, prompts, new OptionQuickConnectStore(optionRepo)),
            new ImportExportPresenter(exchange, prompts, conflicts ?? new ImportConflictsDialog(this)),
            OpenGameWindow);
        _presenter.Changed += Repaint;
        _presenter.StatusChanged += () => _lblStatus!.Text = _presenter.Status;

        Name = nameof(FrmLauncher);
        Text = Strings.App_Title;
        Font = new Font("Segoe UI", 9.75F);
        AutoScaleMode = AutoScaleMode.Font;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(700, 500);
        MinimumSize = new Size(560, 420);

        const int buttonX = 500, buttonW = 188;
        var lblTree = new Label { Name = "_lblTree", Text = Strings.Launcher_TreeLabel, AutoSize = true, Location = new Point(12, 34), TabIndex = 0 };
        _tree = new LauncherTreeView
        {
            Name = "_tree", AccessibleName = FormKit.NameFrom(lblTree.Text), Bounds = new Rectangle(12, 58, 476, 398), TabIndex = 1,
            HideSelection = false, ShowNodeToolTips = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        };
        _tree.AfterSelect += Tree_AfterSelect;
        _tree.AfterExpand += (_, e) => Expanded(e.Node, true);
        _tree.AfterCollapse += (_, e) => Expanded(e.Node, false);
        _tree.NodeMouseDoubleClick += (_, e) => { if (e.Node?.Tag is LauncherSelection { CharacterId: not null }) Execute(LauncherCommand.Connect); };
        _tree.KeyDown += Tree_KeyDown;
        _tree.ContextMenuRequested += ShowTreeContextMenu;

        var y = 58;
        Button Side(string name, string text, int tabIndex, LauncherCommand command)
        {
            var button = FormKit.Button(name, text, new Rectangle(buttonX, y, buttonW, 32), tabIndex, () => Execute(command));
            button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            button.Tag = command;
            _commandControls.Add((button, command));
            y += 38;
            return button;
        }

        var btnConnect = Side("_btnConnect", Strings.Launcher_Connect, 2, LauncherCommand.Connect);
        var btnQuick = Side("_btnQuickConnect", Strings.Launcher_QuickConnect, 3, LauncherCommand.QuickConnect);
        y += 10;
        var btnAddMud = Side("_btnAddMud", Strings.Launcher_AddMudButton, 4, LauncherCommand.AddMud);
        var btnAddChar = Side("_btnAddChar", Strings.Launcher_AddCharacterButton, 5, LauncherCommand.AddCharacter);
        var btnEdit = Side("_btnEdit", Strings.Launcher_Edit, 6, LauncherCommand.Edit);
        var btnRemove = Side("_btnRemove", Strings.Launcher_Remove, 7, LauncherCommand.Remove);
        var btnSetDefault = Side("_btnSetDefault", Strings.Launcher_SetDefault, 8, LauncherCommand.SetDefault);

        // Read by screen readers as the status of the last action.
        _lblStatus = new Label
        {
            Name = "_lblStatus", AutoSize = false, Bounds = new Rectangle(12, 464, 676, 28), TabIndex = 9, UseMnemonic = false,
            AccessibleName = Strings.Launcher_StatusAccessible,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        };

        var menu = BuildMenu();
        // Filled on Opening for the selected node. Not assigned to the tree: the tree asks for it
        // itself so that, from the keyboard, it opens next to the node and not under the mouse.
        _treeMenu = new ContextMenuStrip { Name = "_treeMenu" };
        _treeMenu.Opening += (_, e) => e.Cancel = FillTreeContextMenu(_emptyAreaMenu) == 0;
        _treeMenu.Closed += (_, _) => _emptyAreaMenu = false;
        // After the handler that fills it: the screen reader hears the menu and its first item as soon as it opens.
        ContextMenuAccessibility.Attach(_treeMenu, () => _treeMenuFromMouse);

        Controls.AddRange([lblTree, _tree, btnConnect, btnQuick, btnAddMud, btnAddChar, btnEdit, btnRemove, btnSetDefault, _lblStatus, menu]);
        MainMenuStrip = menu;
        // Enter on the tree connects; elsewhere it presses the focused button as usual.
        AcceptButton = btnConnect;

        UpdateCommands();
    }

    internal LauncherPresenter Presenter => _presenter;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Run(LoadAsync);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _treeMenu.Dispose();
        base.Dispose(disposing);
    }

    internal Task LoadAsync() => _presenter.LoadAsync();

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Run(CheckUpdatesOnStartupAsync);
    }

    /// <summary>
    /// Only when the user switched it on (off by default). In the background: the window is already usable, nothing
    /// is said unless there is a new version, and a failure is silent.
    /// </summary>
    internal Task CheckUpdatesOnStartupAsync() => _updates?.CheckOnStartupAsync() ?? Task.CompletedTask;

    internal UpdateCheckPresenter? Updates => _updates;

    private void Run(Func<Task> action) => ErrorReporter.Run(this, action);

    /// <summary>Every button, menu entry and key ends here.</summary>
    internal void Execute(LauncherCommand command) => Run(() => _presenter.ExecuteAsync(command));

    private void OpenGameWindow(SessionProfile profile)
    {
        var window = _windows().Create(profile);
        window.Show();
    }

    // ── Main menu ──────────────────────────────────────────────────────────

    private MenuStrip BuildMenu()
    {
        var file = new ToolStripMenuItem(Strings.Launcher_MenuFile);
        file.DropDownItems.AddRange([
            Item(Strings.Launcher_Connect, LauncherCommand.Connect, Keys.None, Strings.Launcher_KeyEnter),
            Item(Strings.Launcher_QuickConnect, LauncherCommand.QuickConnect, Keys.Control | Keys.Q),
            new ToolStripSeparator(),
            Item(Strings.Launcher_AddMudButton, LauncherCommand.AddMud, Keys.Control | Keys.N),
            Item(Strings.Launcher_AddCharacterButton, LauncherCommand.AddCharacter, Keys.Control | Keys.Shift | Keys.N),
            Item(Strings.Launcher_Edit, LauncherCommand.Edit, Keys.None, "F2"),
            Item(Strings.Launcher_Remove, LauncherCommand.Remove, Keys.None, Strings.Launcher_KeyDelete),
            Item(Strings.Launcher_SetDefault, LauncherCommand.SetDefault, Keys.Control | Keys.D),
            Item(Strings.Launcher_ClearDefault, LauncherCommand.ClearDefault),
            new ToolStripSeparator(),
            Item(Strings.Launcher_MenuExit, Close, Keys.None, "Alt+F4")]);

        var tools = new ToolStripMenuItem(Strings.Menu_Tools);
        tools.DropDownItems.AddRange([
            Item(Strings.Launcher_MenuGlobalOptions, ShowGlobalOptions, Keys.Control | Keys.O),
            Item(Strings.Launcher_MenuMessageRules, ShowMessageRules, Keys.Control | Keys.R),
            new ToolStripSeparator(),
            Item(Strings.Launcher_MenuImport, LauncherCommand.Import, Keys.Control | Keys.I),
            Item(Strings.Launcher_MenuExportMud, LauncherCommand.ExportMud, Keys.Control | Keys.E),
            Item(Strings.Launcher_MenuExportCharacter, LauncherCommand.ExportCharacter, Keys.Control | Keys.Shift | Keys.E),
            new ToolStripSeparator(),
            Named("_miPersonalInfo", Item(Strings.Launcher_MenuPersonalInfo, () => _app.ShowPersonalInfo(this)))]);
        if (_updates is not null)
        {
            _miCheckOnStartup = Named("_miCheckOnStartup", Item(Strings.Launcher_MenuCheckUpdatesOnStartup, ToggleCheckOnStartup));
            tools.DropDownItems.AddRange([
                new ToolStripSeparator(),
                Named("_miCheckUpdates", Item(Strings.Launcher_MenuCheckUpdates, () => Run(() => _updates.CheckNowAsync()))),
                _miCheckOnStartup]);
            // The box always shows what is stored (it can also be changed in the global options).
            tools.DropDownOpening += (_, _) => RefreshCheckOnStartup();
        }

        var help = new ToolStripMenuItem(Strings.Menu_Help);
        help.DropDownItems.AddRange([
            Item(Strings.Menu_HelpManual, _app.Help.OpenManual, Keys.F1),
            Item(Strings.Menu_HelpLua, _app.Help.OpenLuaReference),
            new ToolStripSeparator(),
            Named("_miSuggestion", Item(Strings.Menu_HelpSuggestion, () => _app.ShowReport(this, ReportKind.Suggestion))),
            Named("_miReportError", Item(Strings.Menu_HelpReportError, () => _app.ShowReport(this, ReportKind.Error))),
            new ToolStripSeparator(),
            Item(Strings.Menu_HelpAbout, ShowAbout)]);

        var menu = new MenuStrip { Name = "_menu", TabIndex = 10 };
        menu.Items.AddRange([file, tools, help]);
        return menu;
    }

    /// <summary>A menu entry bound to a presenter command: the same one its button runs.</summary>
    private ToolStripMenuItem Item(string text, LauncherCommand command, Keys shortcut = Keys.None, string? keysText = null)
    {
        var item = Item(text, () => Execute(command), shortcut, keysText);
        item.Tag = command;
        _commandItems.Add((item, command));
        return item;
    }

    private static ToolStripMenuItem Item(string text, Action onClick, Keys shortcut = Keys.None, string? keysText = null)
    {
        var item = new ToolStripMenuItem(text, null, (_, _) => onClick()) { ShortcutKeys = shortcut };
        // Keys handled by the tree itself (Enter, F2, Del) are only shown, so they do nothing outside it.
        if (keysText is not null) item.ShortcutKeyDisplayString = keysText;
        return item;
    }

    private static ToolStripMenuItem Named(string name, ToolStripMenuItem item)
    {
        item.Name = name;
        return item;
    }

    internal void RefreshCheckOnStartup()
    {
        if (_updates is null || _miCheckOnStartup is null) return;
        try
        {
            _miCheckOnStartup.Checked = Task.Run(() => _updates.GetCheckOnStartupAsync()).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            _miCheckOnStartup.Checked = false;
        }
    }

    private void ToggleCheckOnStartup()
    {
        if (_updates is null || _miCheckOnStartup is null) return;
        var value = !_miCheckOnStartup.Checked;
        _miCheckOnStartup.Checked = value;
        Run(() => _updates.SetCheckOnStartupAsync(value));
    }

    private void ShowGlobalOptions()
    {
        using var dialog = _globalOptionsDialog?.Invoke() ?? new FrmOptions(_optionRepo);
        dialog.ShowDialog(this);
    }

    private void ShowMessageRules()
    {
        using var dialog = new FrmMessageRules(_ruleRepo, _prompts);
        dialog.ShowDialog(this);
    }

    private void ShowAbout() =>
        _prompts.Info(string.Format(Strings.About_Text, AppInfo.Version), Strings.App_Title);

    // ── Context menu of the tree ───────────────────────────────────────────

    internal ContextMenuStrip TreeContextMenu => _treeMenu;

    /// <summary>
    /// Rebuilds the context menu for the selected node (or for the empty area) from the presenter:
    /// same commands as buttons and main menu, nothing is decided here. Returns the number of entries.
    /// </summary>
    internal int FillTreeContextMenu(bool emptyArea = false)
    {
        _treeMenu.Items.Clear();
        foreach (var entry in emptyArea ? _presenter.GetEmptyAreaContextMenu() : _presenter.GetContextMenu())
        {
            var command = entry.Command;
            _treeMenu.Items.Add(new ToolStripMenuItem(entry.Text, null, (_, _) => Execute(command))
            {
                Name = "_ctx" + command, Tag = command, Enabled = entry.Enabled, Checked = entry.Checked,
            });
        }
        return _treeMenu.Items.Count;
    }

    /// <summary>Where the menu opens from the keyboard: right under the selected node, never at the mouse.</summary>
    internal Point KeyboardMenuLocation()
    {
        if (_tree.SelectedNode is not { } node) return new Point(4, 4);
        node.EnsureVisible();
        var bounds = node.Bounds;
        return bounds.IsEmpty ? new Point(4, 4) : new Point(bounds.Left, bounds.Bottom);
    }

    /// <summary>Selects the node under a right click and says whether the click fell on the empty area.</summary>
    internal bool PrepareMouseMenu(Point clientLocation)
    {
        // Right click acts on the node under the mouse, not on the one that was selected.
        var node = _tree.GetNodeAt(clientLocation);
        if (node is not null) _tree.SelectedNode = node;
        return node is null;
    }

    /// <param name="mouseLocation">Client point of the right click; null when asked from the keyboard (Applications key, Shift+F10).</param>
    private void ShowTreeContextMenu(Point? mouseLocation)
    {
        _treeMenuFromMouse = mouseLocation is not null;
        if (mouseLocation is { } point)
        {
            _emptyAreaMenu = PrepareMouseMenu(point);
            _treeMenu.Show(_tree, point);
        }
        else
        {
            _emptyAreaMenu = false;
            _treeMenu.Show(_tree, KeyboardMenuLocation());
        }
    }

    // ── Tree ───────────────────────────────────────────────────────────────

    /// <summary>Rebuilds the tree from the presenter keeping expansion, selection and (as the control is the same) the focus.</summary>
    private void Repaint()
    {
        _painting = true;
        TreeNode? toSelect = null;
        _tree.BeginUpdate();
        try
        {
            _tree.Nodes.Clear();
            foreach (var node in _presenter.Nodes)
            {
                var mudSelection = new LauncherSelection(node.Mud.Id);
                var mudNode = new TreeNode(node.Text) { Name = $"mud{node.Mud.Id}", Tag = mudSelection };
                foreach (var character in node.Characters)
                {
                    var selection = new LauncherSelection(node.Mud.Id, character.Id);
                    var charNode = new TreeNode(LauncherMudNode.CharacterText(character)) { Name = $"char{character.Id}", Tag = selection };
                    mudNode.Nodes.Add(charNode);
                    if (selection == _presenter.Selection) toSelect = charNode;
                }
                _tree.Nodes.Add(mudNode);
                if (_presenter.IsExpanded(node.Mud.Id)) mudNode.Expand();
                if (mudSelection == _presenter.Selection) toSelect = mudNode;
            }
        }
        finally
        {
            _tree.EndUpdate();
            _painting = false;
        }

        if (toSelect is not null)
        {
            _painting = true;
            try { _tree.SelectedNode = toSelect; }
            finally { _painting = false; }
            if (_tree.IsHandleCreated) toSelect.EnsureVisible();
        }
        _lblStatus.Text = _presenter.Status;
        UpdateCommands();
    }

    private void Tree_AfterSelect(object? sender, TreeViewEventArgs e)
    {
        if (_painting) return;
        _presenter.Select(e.Node?.Tag as LauncherSelection);
        UpdateCommands();
    }

    private void Expanded(TreeNode? node, bool expanded)
    {
        if (!_painting && node?.Tag is LauncherSelection { CharacterId: null } mud)
            _presenter.SetExpanded(mud.MudId, expanded);
    }

    private void UpdateCommands()
    {
        foreach (var (control, command) in _commandControls)
            control.Enabled = _presenter.CanExecute(command);
        foreach (var (item, command) in _commandItems)
            item.Enabled = _presenter.CanExecute(command);
    }

    private void Tree_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Modifiers != Keys.None) return;
        switch (e.KeyCode)
        {
            case Keys.Enter: Execute(LauncherCommand.Connect); break;
            case Keys.Delete: Execute(LauncherCommand.Remove); break;
            case Keys.F2: Execute(LauncherCommand.Edit); break;
            // A sibling of what is selected: a character on a character, a MUD otherwise.
            case Keys.Insert: Run(() => _presenter.AddForSelectionAsync()); break;
            default: return;
        }
        e.Handled = e.SuppressKeyPress = true;
    }
}

/// <summary>
/// The "new version" dialog of the launcher. It never pops up over something else: when the launcher is not the
/// active window (a game is being played, a dialog is open) it waits until the user comes back to the launcher.
/// </summary>
internal sealed class LauncherUpdateNotice(Form launcher) : IUpdateNotice
{
    public async Task<bool> ShowAsync(UpdateAvailable update)
    {
        if (launcher.IsDisposed) return false;
        if (!ReferenceEquals(Form.ActiveForm, launcher) && !await WaitUntilActiveAsync()) return false;

        using var dialog = new FrmUpdateAvailable(update, AppInfo.Version);
        return dialog.ShowDialog(launcher) == DialogResult.OK;
    }

    /// <summary>False when the launcher was closed while waiting.</summary>
    private Task<bool> WaitUntilActiveAsync()
    {
        var waiting = new TaskCompletionSource<bool>();
        void Activated(object? sender, EventArgs e) => Finish(true);
        void Closed(object? sender, FormClosedEventArgs e) => Finish(false);
        void Finish(bool result)
        {
            launcher.Activated -= Activated;
            launcher.FormClosed -= Closed;
            waiting.TrySetResult(result);
        }
        launcher.Activated += Activated;
        launcher.FormClosed += Closed;
        return waiting.Task;
    }
}

/// <summary>
/// Tree that asks for its context menu itself, telling keyboard (Applications key and Shift+F10
/// arrive as WM_CONTEXTMENU without position) from mouse, so the menu can open next to the node.
/// </summary>
internal sealed class LauncherTreeView : TreeView
{
    private const int WmContextMenu = 0x007B;

    /// <summary>Client position of the right click, or null when the menu was asked from the keyboard.</summary>
    public event Action<Point?>? ContextMenuRequested;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmContextMenu && ContextMenuRequested is not null)
        {
            var lParam = m.LParam.ToInt64();
            if ((int)lParam == -1)
            {
                RequestContextMenu(null);
            }
            else
            {
                var screen = new Point(unchecked((short)(lParam & 0xFFFF)), unchecked((short)((lParam >> 16) & 0xFFFF)));
                RequestContextMenu(PointToClient(screen));
            }
            return;
        }
        base.WndProc(ref m);
    }

    internal void RequestContextMenu(Point? clientLocation) => ContextMenuRequested?.Invoke(clientLocation);
}
