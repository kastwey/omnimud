using Omnimud.Core.Options;
using Omnimud.Core.Session;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;
using Omnimud.UI.Services.Accessibility;

namespace Omnimud.UI.Forms;

/// <summary>
/// Window shared by the alias, trigger and path lists. It only binds controls to an
/// <see cref="EntityListPresenter{T}"/>: label with mnemonic + named list, the button column,
/// Del / Insert / F2 / Enter / Space / Alt+arrows, the context menu, keyboard sorting and a
/// selection that survives every reload. Rules live in the presenter.
/// </summary>
public abstract class EntityListForm<T> : Form where T : class
{
    private readonly EntityListPresenter<T> _presenter;
    private readonly ListExchangePresenter? _exchange;
    private readonly Func<Task<IReadOnlyList<CharacterChoice>>>? _loadOtherCharacters;

    private readonly ListView _list = new();
    private readonly Label _lblStatus = new();
    private readonly ContextMenuStrip _sortMenu = new();
    private readonly ContextMenuStrip _importMenu = new();
    private readonly ContextMenuStrip _listMenu = new();
    private Button _btnEdit = null!;
    private Button _btnRemove = null!;
    private Button? _btnToggle;
    private Button? _btnUp;
    private Button? _btnDown;
    private Button _btnSort = null!;
    private Button _btnImport = null!;
    private Button _btnExport = null!;
    private ToolStripMenuItem? _menuToggle;
    private bool _rendering;
    private bool _busy;

    protected EntityListForm(EntityListPresenter<T> presenter, IUserPrompts prompts, IAnnouncer? announcer,
        ListExchangePresenter? exchange, Func<Task<IReadOnlyList<CharacterChoice>>>? loadOtherCharacters)
    {
        _presenter = presenter;
        Prompts = prompts;
        _exchange = exchange;
        _loadOtherCharacters = exchange is null ? null : loadOtherCharacters;
        (announcer as FormAnnouncer)?.Attach(_list);
        _presenter.Changed += (_, _) => Render();
    }

    protected IUserPrompts Prompts { get; }

    /// <summary>True when something was written: the session must reload its data.</summary>
    public bool Changed => _presenter.DataChanged;

    /// <summary>Tests replace the modal editor with a function.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal EntityEditor<T>? EditorOverride { get; set; }

    /// <summary>Tests replace the "pick a character" dialog with a function.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<IReadOnlyList<CharacterChoice>, CharacterChoice?>? PickCharacterOverride { get; set; }

    /// <summary>The action started by the last key, button or menu, so tests can wait for it.</summary>
    internal Task LastAction { get; private set; } = Task.CompletedTask;

    internal ListView List => _list;
    internal string StatusText => _lblStatus.Text;

    protected sealed record ListFormTexts(string Title, string ListLabel, string ListAccessibleName);

    protected abstract string[] Cells(T item);
    protected abstract T? ShowEditor(T? current, bool isNew, IReadOnlyList<T> all);

    /// <summary>Lists whose elements can be switched on and off say here whether the element is on.</summary>
    protected virtual bool IsItemEnabled(T item) => true;
    protected virtual Task ToggleAsync() => Task.CompletedTask;
    protected virtual Task MoveAsync(int delta) => Task.CompletedTask;

    // ── Construction ─────────────────────────────────────────────────────

    protected void Build(ListFormTexts texts, IReadOnlyList<(string Text, int Width)> columns, bool hasToggle, bool hasMove)
    {
        SuspendLayout();
        Text = texts.Title;
        Font = new Font("Segoe UI", 9.75F);
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(800, 440);

        var tab = 0;
        var label = new Label { Name = "_lblList", Text = texts.ListLabel, AutoSize = true, Location = new Point(12, 12), TabIndex = tab++ };

        _list.Name = "_list";
        _list.Location = new Point(12, 34);
        _list.Size = new Size(620, 364);
        _list.TabIndex = tab++;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.MultiSelect = false;
        _list.HideSelection = false;
        _list.AccessibleName = texts.ListAccessibleName;
        foreach (var (text, width) in columns) _list.Columns.Add(text, width);
        _list.SelectedIndexChanged += (_, _) => OnListSelectionChanged();
        _list.DoubleClick += (_, _) => Run(EditAsync);
        _list.KeyDown += (_, e) =>
        {
            if (!HandleListKey(e.KeyData)) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
        };
        _list.ColumnClick += (_, e) => SortByColumnIndex(e.Column);

        var top = 34;
        Button Next(string name, string text, Func<Task> action)
        {
            var button = new Button { Name = name, Text = text, Location = new Point(644, top), Size = new Size(144, 30), TabIndex = tab++ };
            button.Click += (_, _) => Run(action);
            top += 34;
            Controls.Add(button);
            return button;
        }

        Controls.Add(label);
        Controls.Add(_list);
        Next("_btnAdd", Strings.Lst_Add, AddAsync);
        _btnEdit = Next("_btnEdit", Strings.Lst_Edit, EditAsync);
        _btnRemove = Next("_btnRemove", Strings.Lst_Remove, RemoveAsync);
        if (hasToggle) _btnToggle = Next("_btnToggle", Strings.Lst_Disable, ToggleAsync);
        if (hasMove)
        {
            _btnUp = Next("_btnUp", Strings.Lst_MoveUp, () => MoveAsync(-1));
            _btnDown = Next("_btnDown", Strings.Lst_MoveDown, () => MoveAsync(+1));
        }
        _btnSort = Next("_btnSort", Strings.Lst_SortBy, () =>
        {
            FillSortItems(_sortMenu.Items); // an empty menu refuses to open
            return ShowMenu(_sortMenu, _btnSort);
        });
        _btnImport = Next("_btnImport", Strings.Lst_Import, () => ShowMenu(_importMenu, _btnImport));
        _btnExport = Next("_btnExport", Strings.Lst_Export, ExportAsync);

        var close = new Button
        {
            Name = "_btnClose", Text = Strings.Lst_Close, DialogResult = DialogResult.Cancel,
            Location = new Point(644, 368), Size = new Size(144, 30), TabIndex = tab++,
        };
        Controls.Add(close);

        // Mirrors what is announced, for those who do not use a screen reader.
        _lblStatus.Name = "_lblStatus";
        _lblStatus.AutoSize = true;
        _lblStatus.UseMnemonic = false;
        _lblStatus.Location = new Point(12, 408);
        _lblStatus.TabIndex = tab;
        Controls.Add(_lblStatus);

        _importMenu.Items.Add(new ToolStripMenuItem(Strings.Lst_ImportFromFile, null, (_, _) => Run(ImportFromFileAsync)) { Name = "_mnuImportFile" });
        _importMenu.Items.Add(new ToolStripMenuItem(Strings.Lst_ImportFromCharacter, null, (_, _) => Run(ImportFromCharacterAsync))
            { Name = "_mnuImportCharacter", Enabled = _loadOtherCharacters is not null });

        BuildListMenu(hasToggle, hasMove);
        _list.ContextMenuStrip = _listMenu;
        foreach (var menu in new[] { _listMenu, _sortMenu, _importMenu }) ContextMenuAccessibility.Attach(menu);

        CancelButton = close;
        AcceptButton = _btnEdit; // Enter on the list edits
        UpdateButtons();
        ResumeLayout(false);
        PerformLayout();
    }

    private void BuildListMenu(bool hasToggle, bool hasMove)
    {
        var needSelection = new List<ToolStripMenuItem>();
        ToolStripMenuItem Item(string text, Func<Task> action, Keys shortcutShown = Keys.None, bool needsSelection = true)
        {
            var item = new ToolStripMenuItem(text, null, (_, _) => Run(action));
            if (needsSelection) needSelection.Add(item);
            if (shortcutShown != Keys.None) item.ShortcutKeyDisplayString = new KeysConverter().ConvertToString(shortcutShown);
            return item;
        }

        _listMenu.Items.Add(Item(Strings.Lst_Add, AddAsync, Keys.Insert, needsSelection: false));
        _listMenu.Items.Add(Item(Strings.Lst_Edit, EditAsync, Keys.F2));
        _listMenu.Items.Add(Item(Strings.Lst_Remove, RemoveAsync, Keys.Delete));
        if (hasToggle)
        {
            _menuToggle = Item(Strings.Lst_Disable, ToggleAsync, Keys.Space);
            _listMenu.Items.Add(_menuToggle);
        }
        if (hasMove)
        {
            _listMenu.Items.Add(Item(Strings.Lst_MoveUp, () => MoveAsync(-1), Keys.Alt | Keys.Up));
            _listMenu.Items.Add(Item(Strings.Lst_MoveDown, () => MoveAsync(+1), Keys.Alt | Keys.Down));
        }
        _listMenu.Items.Add(new ToolStripSeparator());

        var sort = new ToolStripMenuItem(Strings.Lst_SortBy);
        sort.DropDownItems.Add(new ToolStripMenuItem("-")); // so that it shows as a submenu before it is filled
        sort.DropDownOpening += (_, _) => FillSortItems(sort.DropDownItems);
        _listMenu.Items.Add(sort);

        if (_exchange is not null)
        {
            var import = new ToolStripMenuItem(Strings.Lst_Import);
            import.DropDownItems.Add(new ToolStripMenuItem(Strings.Lst_ImportFromFile, null, (_, _) => Run(ImportFromFileAsync)));
            import.DropDownItems.Add(new ToolStripMenuItem(Strings.Lst_ImportFromCharacter, null, (_, _) => Run(ImportFromCharacterAsync))
                { Enabled = _loadOtherCharacters is not null });
            _listMenu.Items.Add(import);
            _listMenu.Items.Add(Item(Strings.Lst_Export, ExportAsync, needsSelection: false));
        }

        _listMenu.Opening += (_, _) =>
        {
            var has = _presenter.Selected is not null;
            foreach (var item in needSelection) item.Enabled = has;
        };
    }

    /// <summary>Every sortable column; the current one is checked and says its direction.</summary>
    internal void FillSortItems(ToolStripItemCollection items)
    {
        items.Clear();
        foreach (var column in _presenter.SortColumns)
        {
            var current = _presenter.Sort.Column == column.Key;
            var text = !current ? column.DisplayName
                : string.Format(_presenter.Sort.Descending ? Strings.Lst_SortItemDescending : Strings.Lst_SortItemAscending, column.DisplayName);
            var key = column.Key;
            items.Add(new ToolStripMenuItem(text, null, (_, _) => Run(() => SortAsync(key))) { Checked = current, Name = "_mnuSort_" + key });
        }
    }

    /// <summary>Header clicks sort too; derived forms map a column index to a sort key.</summary>
    protected virtual string? SortKeyOfColumn(int columnIndex) => null;

    private void SortByColumnIndex(int index)
    {
        if (SortKeyOfColumn(index) is { } key) Run(() => SortAsync(key));
    }

    // ── Loading and painting ─────────────────────────────────────────────

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Run(InitializeAsync);
        _list.Focus();
    }

    internal virtual Task InitializeAsync() => _presenter.LoadAsync();

    private void Render()
    {
        _rendering = true;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var item in _presenter.Items)
            _list.Items.Add(new ListViewItem(Cells(item)) { Tag = item });
        _list.EndUpdate();

        if (_presenter.SelectedIndex >= 0 && _presenter.SelectedIndex < _list.Items.Count)
        {
            var selected = _list.Items[_presenter.SelectedIndex];
            selected.Selected = true;
            selected.Focused = true;
            selected.EnsureVisible();
        }
        _rendering = false;

        _lblStatus.Text = _presenter.Status;
        UpdateButtons();
    }

    private void OnListSelectionChanged()
    {
        if (_rendering) return;
        if (_list.SelectedIndices.Count > 0) _presenter.Select(_list.SelectedIndices[0]);
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var selected = _presenter.Selected;
        var has = selected is not null;
        _btnEdit.Enabled = has;
        _btnRemove.Enabled = has;
        if (_btnUp is not null) _btnUp.Enabled = has;
        if (_btnDown is not null) _btnDown.Enabled = has;
        _btnImport.Enabled = _exchange is not null;
        _btnExport.Enabled = _exchange is not null;

        if (_btnToggle is not null)
        {
            _btnToggle.Enabled = has;
            // The text says what the button WILL do; a button's name is its text, so both change together.
            var text = selected is not null && !IsItemEnabled(selected) ? Strings.Lst_Enable : Strings.Lst_Disable;
            _btnToggle.Text = text;
            if (_menuToggle is not null) _menuToggle.Text = text;
        }
    }

    // ── Keyboard ─────────────────────────────────────────────────────────

    /// <summary>Keys of the list. Enter is the AcceptButton (Edit). Returns true when the key was taken.</summary>
    internal bool HandleListKey(Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Delete: Run(RemoveAsync); return true;
            case Keys.Insert: Run(AddAsync); return true;
            case Keys.F2: Run(EditAsync); return true;
            case Keys.Space when _btnToggle is not null: Run(ToggleAsync); return true;
            case Keys.Alt | Keys.Up when _btnUp is not null: Run(() => MoveAsync(-1)); return true;
            case Keys.Alt | Keys.Down when _btnDown is not null: Run(() => MoveAsync(+1)); return true;
            default: return false;
        }
    }

    // ── Actions ──────────────────────────────────────────────────────────

    /// <summary>One action at a time, errors reported instead of thrown, focus back to the list.</summary>
    private void Run(Func<Task> action)
    {
        if (_busy) return;
        ErrorReporter.Run(this, () => LastAction = Guarded(action));
    }

    private async Task Guarded(Func<Task> action)
    {
        _busy = true;
        try
        {
            await action();
        }
        finally
        {
            _busy = false;
            if (Visible && !_sortMenu.Visible && !_importMenu.Visible) _list.Focus();
        }
    }

    private EntityEditor<T> Editor => EditorOverride ?? ShowEditor;

    private Task AddAsync() => _presenter.AddAsync(Editor);
    private Task EditAsync() => _presenter.EditSelectedAsync(Editor);
    private Task RemoveAsync() => _presenter.RemoveSelectedAsync();
    private Task SortAsync(string column) => _presenter.SortByAsync(column);

    private static Task ShowMenu(ContextMenuStrip menu, Control anchor)
    {
        menu.Show(anchor, new Point(0, anchor.Height)); // ContextMenuAccessibility selects the first item
        return Task.CompletedTask;
    }

    private Task ExportAsync() => _exchange is null ? Task.CompletedTask : _exchange.ExportAsync();

    private async Task ImportFromFileAsync()
    {
        if (_exchange is null) return;
        if (await _exchange.ImportFromFileAsync()) await AfterImportAsync();
    }

    private async Task ImportFromCharacterAsync()
    {
        if (_exchange is null || _loadOtherCharacters is null) return;

        var choices = await _loadOtherCharacters();
        if (choices.Count == 0)
        {
            Prompts.Info(Strings.PickChar_None, Text);
            return;
        }

        var choice = PickCharacterOverride is { } pick ? pick(choices) : PickCharacter(choices);
        if (choice is null) return;
        if (await _exchange.ImportFromCharacterAsync(choice)) await AfterImportAsync();
    }

    private CharacterChoice? PickCharacter(IReadOnlyList<CharacterChoice> choices)
    {
        using var dialog = new FrmPickCharacter(choices);
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Selected : null;
    }

    private Task AfterImportAsync()
    {
        _presenter.MarkDataChanged();
        return _presenter.RefreshAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sortMenu.Dispose();
            _importMenu.Dispose();
            _listMenu.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Announcer for the constructors that receive none: UI Automation notifications raised from the
/// list itself (with the JAWS/NVDA libraries as fallback), created once the list exists.
/// </summary>
internal sealed class FormAnnouncer : IAnnouncer
{
    private IAnnouncer? _inner;

    public void Attach(Control source) => _inner ??= new Announcer(new ControlUiaNotifier(source));

    public ScreenReaderMode Mode { get; set; } = ScreenReaderMode.Automatic;
    public bool Muted { get; set; }

    public bool Announce(string text, AnnouncePriority priority)
    {
        if (_inner is null || Muted) return false;
        _inner.Mode = Mode;
        return _inner.Announce(text, priority);
    }

    public void StopSpeech() => _inner?.StopSpeech();
}

/// <summary>Optional services of the list windows turned into what the base form needs (null = feature off).</summary>
internal static class ListFormServices
{
    public static ListExchangePresenter? Exchange(Omnimud.Data.Exchange.IExchangeService? exchange, IUserPrompts prompts,
        IImportConflictResolver? conflicts, Omnimud.Data.Exchange.ExchangeParts part, int characterId, string characterName) =>
        exchange is null ? null : new ListExchangePresenter(exchange, prompts, conflicts ?? new ImportConflictsDialog(), part, characterId, characterName);

    public static Func<Task<IReadOnlyList<CharacterChoice>>>? OtherCharacters(
        Omnimud.Data.Repositories.ICharacterRepository? characters, Omnimud.Data.Repositories.IMudRepository? muds, int characterId) =>
        characters is null || muds is null ? null : () => ListExchangePresenter.LoadOtherCharactersAsync(characters, muds, characterId);
}

/// <summary>What the three editor dialogs share: look, fields and the accept flow.</summary>
internal static class EditorDialogs
{
    public static void Prepare(Form form, string name, string title, Size clientSize)
    {
        form.Name = name;
        form.Text = title;
        form.Font = new Font("Segoe UI", 9.75F);
        form.AutoScaleMode = AutoScaleMode.Font;
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.MaximizeBox = false;
        form.MinimizeBox = false;
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.CenterParent;
        form.ClientSize = clientSize;
    }

    public static Label Label(string name, string text, int left, int top, int tabIndex) =>
        new() { Name = name, Text = text, AutoSize = true, Location = new Point(left, top), TabIndex = tabIndex };

    /// <summary>A single-line box that selects its whole text when it gets the focus.</summary>
    public static TextBox TextBox(string name, string accessibleName, int left, int top, int width, int tabIndex, int maxLength = 0)
    {
        var box = new TextBox
        {
            Name = name, AccessibleName = accessibleName, Location = new Point(left, top), Size = new Size(width, 25),
            TabIndex = tabIndex, MaxLength = maxLength > 0 ? maxLength : 32767,
        };
        box.Enter += (_, _) => box.SelectAll();
        return box;
    }

    /// <summary>OK and Cancel at the bottom right. OK has no DialogResult: the form validates first.</summary>
    public static (Button Ok, Button Cancel) Buttons(Form form, int tabIndex, Action accept)
    {
        var size = form.ClientSize;
        var ok = new Button { Name = "_btnOk", Text = Strings.Common_OKButton, Location = new Point(size.Width - 214, size.Height - 42), Size = new Size(95, 30), TabIndex = tabIndex };
        ok.Click += (_, _) => accept();
        var cancel = new Button
        {
            Name = "_btnCancel", Text = Strings.Common_CancelButton, DialogResult = DialogResult.Cancel,
            Location = new Point(size.Width - 107, size.Height - 42), Size = new Size(95, 30), TabIndex = tabIndex + 1,
        };
        form.Controls.Add(ok);
        form.Controls.Add(cancel);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        return (ok, cancel);
    }

    /// <summary>After a validation error the focus goes to the offending field. Works on a form that is not shown too.</summary>
    public static void FocusField(Form form, Control field)
    {
        form.ActiveControl = field;
        field.Focus();
    }

    /// <summary>Error → message and false. Warnings → each one must be answered Yes.</summary>
    public static bool Accept(IUserPrompts prompts, string title, EditorIssue? issue, Func<IReadOnlyList<string>> warnings, Action<EditorIssue> focus)
    {
        if (issue is not null)
        {
            prompts.Warn(issue.Message, title);
            focus(issue);
            return false;
        }
        return warnings().All(w => prompts.Confirm(w, title));
    }
}
