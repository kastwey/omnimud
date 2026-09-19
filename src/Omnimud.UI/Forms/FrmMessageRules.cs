using Omnimud.Core.Scripting;
using Omnimud.Core.Session;
using Omnimud.Data.Entities;
using Omnimud.Data.Repositories;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;
using Omnimud.UI.Services.Accessibility;

namespace Omnimud.UI.Forms;

/// <summary>
/// Message rule sets (left) and, on the right, what the selected one works with: its ordered
/// pattern rules, or its Lua script (type of the set, chosen under the list of sets). Below, a box
/// to try sample text against the set. Built-in sets are read-only: they can only be duplicated,
/// and the duplicate is the user's to adapt. Logic in <see cref="MessageRulesPresenter"/>.
/// </summary>
public sealed class FrmMessageRules : Form
{
    private readonly MessageRulesPresenter _presenter;
    private readonly IAnnouncer _announcer;
    private readonly IScriptEngine _scriptEngine;
    private readonly bool _ownsEngine;
    private readonly Font _monospace = new(FontFamily.GenericMonospace, 10F);

    private readonly ListBox _listSets;
    private readonly ComboBox _cboType;
    private readonly Label _lblRules;
    private readonly ListView _listRules;
    private readonly Button _btnDuplicate;
    private readonly Button _btnRename;
    private readonly Button _btnRemoveSet;
    private readonly Button _btnAddRule;
    private readonly Button _btnEditRule;
    private readonly Button _btnRemoveRule;
    private readonly Button _btnUp;
    private readonly Button _btnDown;
    private readonly Button _btnToggle;
    private readonly Label _lblKeys;
    private readonly Label _lblScript;
    private readonly TextBox _txtScript;
    private readonly Label _lblTabHint;
    private readonly Button _btnSaveScript;
    private readonly Button _btnTest;
    private readonly TextBox _txtSample;
    private readonly TextBox _txtResult;
    private readonly Label _lblStatus;
    private bool _painting;
    private bool _closeConfirmed;

    /// <param name="scriptEngine">Validates and tests Lua scripts. Null = the window creates (and disposes) one.</param>
    public FrmMessageRules(IMessageRuleRepository repository, IUserPrompts prompts, IAnnouncer? announcer = null, IScriptEngine? scriptEngine = null)
        : this(repository, prompts, announcer, dialogs: null, scriptEngine)
    {
    }

    /// <summary>For tests: the editors can be answered without opening a window.</summary>
    internal FrmMessageRules(IMessageRuleRepository repository, IUserPrompts prompts, IAnnouncer? announcer, IMessageRulesDialogs? dialogs,
        IScriptEngine? scriptEngine = null)
    {
        _announcer = announcer ?? new Announcer(new ControlUiaNotifier(this));
        _ownsEngine = scriptEngine is null;
        _scriptEngine = scriptEngine ?? new LuaScriptEngine();
        _presenter = new MessageRulesPresenter(repository, prompts, dialogs ?? new MessageRulesDialogs(this, prompts),
            text => _announcer.Announce(text, AnnouncePriority.MostRecent), _scriptEngine);
        _presenter.Changed += Repaint;
        _presenter.ScriptRejected += ScriptRejected;

        FormKit.SetupDialog(this, nameof(FrmMessageRules), Strings.MsgRules_Title, new Size(900, 690));

        var tab = 0;
        var lblSets = FormKit.Label("_lblSets", Strings.MsgRules_SetsLabel, 12, 9, tab++);
        _listSets = new ListBox
        {
            Name = "_listSets", AccessibleName = FormKit.NameFrom(lblSets.Text), Bounds = new Rectangle(12, 34, 260, 250),
            TabIndex = tab++, IntegralHeight = false,
        };
        _listSets.SelectedIndexChanged += (_, _) => SetSelected();
        _listSets.KeyDown += Sets_KeyDown;

        var btnNewSet = FormKit.Button("_btnNewSet", Strings.MsgRules_NewSet, new Rectangle(12, 292, 126, 30), tab++, () => Run(() => _presenter.AddSetAsync()));
        _btnDuplicate = FormKit.Button("_btnDuplicate", Strings.MsgRules_DuplicateSet, new Rectangle(146, 292, 126, 30), tab++, () => Run(() => _presenter.DuplicateSetAsync()));
        _btnRename = FormKit.Button("_btnRename", Strings.MsgRules_RenameSet, new Rectangle(12, 328, 126, 30), tab++, () => Run(() => _presenter.RenameSetAsync()));
        _btnRemoveSet = FormKit.Button("_btnRemoveSet", Strings.MsgRules_RemoveSet, new Rectangle(146, 328, 126, 30), tab++, () => Run(() => _presenter.RemoveSetAsync()));

        var lblType = FormKit.Label("_lblType", Strings.MsgRules_TypeLabel, 12, 368, tab++);
        _cboType = new ComboBox
        {
            Name = "_cboType", AccessibleName = FormKit.NameFrom(lblType.Text), Bounds = new Rectangle(12, 394, 260, 25),
            TabIndex = tab++, DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _cboType.Items.AddRange([Strings.MsgRules_TypePatterns, Strings.MsgRules_TypeScript]);
        _cboType.SelectedIndexChanged += (_, _) => TypeSelected();

        // ── Type "patterns": the ordered rules ─────────────────────────────
        _lblRules = FormKit.Label("_lblRules", Strings.MsgRules_RulesLabel, 290, 9, tab++);
        _listRules = new ListView
        {
            Name = "_listRules", AccessibleName = FormKit.NameFrom(_lblRules.Text), Bounds = new Rectangle(290, 34, 598, 250),
            TabIndex = tab++, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
        };
        _listRules.Columns.Add(Strings.MsgRules_ColPattern, 220);
        _listRules.Columns.Add(Strings.MsgRules_ColState, 90);
        _listRules.Columns.Add(Strings.MsgRules_ColTemplate, 130);
        _listRules.Columns.Add(Strings.MsgRules_ColChannel, 80);
        _listRules.Columns.Add(Strings.MsgRules_ColCase, 70);
        _listRules.SelectedIndexChanged += (_, _) => RuleSelected();
        _listRules.KeyDown += Rules_KeyDown;
        _listRules.DoubleClick += (_, _) => Run(() => _presenter.EditRuleAsync());

        _btnAddRule = FormKit.Button("_btnAddRule", Strings.MsgRules_AddRule, new Rectangle(290, 292, 144, 30), tab++, () => Run(() => _presenter.AddRuleAsync()));
        _btnEditRule = FormKit.Button("_btnEditRule", Strings.MsgRules_EditRule, new Rectangle(442, 292, 144, 30), tab++, () => Run(() => _presenter.EditRuleAsync()));
        _btnRemoveRule = FormKit.Button("_btnRemoveRule", Strings.MsgRules_RemoveRule, new Rectangle(594, 292, 144, 30), tab++, () => Run(() => _presenter.RemoveRuleAsync()));
        _btnUp = FormKit.Button("_btnUp", Strings.MsgRules_MoveUp, new Rectangle(290, 328, 144, 30), tab++, () => Run(() => _presenter.MoveUpAsync()));
        _btnDown = FormKit.Button("_btnDown", Strings.MsgRules_MoveDown, new Rectangle(442, 328, 144, 30), tab++, () => Run(() => _presenter.MoveDownAsync()));
        _btnToggle = FormKit.Button("_btnToggle", Strings.MsgRules_Toggle, new Rectangle(594, 328, 200, 30), tab++, () => Run(() => _presenter.ToggleRuleAsync()));
        _lblKeys = FormKit.Hint("_lblKeys", Strings.MsgRules_KeysHint, new Rectangle(290, 366, 598, 56), tab++);

        // ── Type "Lua script": the same area holds the script ──────────────
        _lblScript = FormKit.Label("_lblScript", Strings.MsgRules_ScriptLabel, 290, 9, tab++);
        _txtScript = new TextBox
        {
            Name = "_txtScript", AccessibleName = FormKit.NameFrom(_lblScript.Text), Bounds = new Rectangle(290, 34, 598, 324),
            TabIndex = tab++, Multiline = true, AcceptsReturn = true, AcceptsTab = true, // Ctrl+Tab still leaves the box
            WordWrap = false, ScrollBars = ScrollBars.Both, Font = _monospace, MaxLength = 0,
        };
        _txtScript.TextChanged += (_, _) => ScriptEdited();
        _lblTabHint = FormKit.Hint("_lblTabHint", Strings.MsgRules_ScriptTabHint, new Rectangle(290, 368, 440, 24), tab++);
        _btnSaveScript = FormKit.Button("_btnSaveScript", Strings.MsgRules_SaveScript, new Rectangle(744, 364, 144, 30), tab++, () => Run(SaveScriptAsync));

        // ── Test ───────────────────────────────────────────────────────────
        var lblSample = FormKit.Label("_lblSample", Strings.MsgRules_SampleLabel, 12, 432, tab++);
        _txtSample = FormKit.TextBox("_txtSample", lblSample, new Rectangle(290, 432, 486, 84), tab++);
        _txtSample.Multiline = true;
        _txtSample.ScrollBars = ScrollBars.Vertical;
        _btnTest = FormKit.Button("_btnTest", Strings.MsgRules_TestButton, new Rectangle(784, 430, 104, 29), tab++, RunTest);

        var lblResult = FormKit.Label("_lblResult", Strings.MsgRules_ResultLabel, 12, 526, tab++);
        _txtResult = FormKit.TextBox("_txtResult", lblResult, new Rectangle(290, 526, 598, 96), tab++);
        _txtResult.Multiline = true;
        _txtResult.ReadOnly = true;
        _txtResult.ScrollBars = ScrollBars.Vertical;

        // Read by screen readers as the status of the last action.
        _lblStatus = FormKit.Hint("_lblStatus", string.Empty, new Rectangle(12, 632, 760, 48), tab++);
        var btnClose = FormKit.Button("_btnClose", Strings.Common_Close, new Rectangle(784, 648, 104, 30), tab);
        btnClose.DialogResult = DialogResult.Cancel;

        _listSets.ContextMenuStrip = BuildMenu(
            (Strings.MsgRules_NewSet, "Ins", () => _presenter.AddSetAsync()),
            (Strings.MsgRules_DuplicateSet, null, () => _presenter.DuplicateSetAsync()),
            (Strings.MsgRules_RenameSet, "F2", () => _presenter.RenameSetAsync()),
            (Strings.MsgRules_RemoveSet, Strings.Launcher_KeyDelete, () => _presenter.RemoveSetAsync()));
        _listRules.ContextMenuStrip = BuildMenu(
            (Strings.MsgRules_AddRule, "Ins", () => _presenter.AddRuleAsync()),
            (Strings.MsgRules_EditRule, "F2", () => _presenter.EditRuleAsync()),
            (Strings.MsgRules_RemoveRule, Strings.Launcher_KeyDelete, () => _presenter.RemoveRuleAsync()),
            (Strings.MsgRules_MoveUp, Strings.MsgRules_KeyAltUp, () => _presenter.MoveUpAsync()),
            (Strings.MsgRules_MoveDown, Strings.MsgRules_KeyAltDown, () => _presenter.MoveDownAsync()),
            (Strings.MsgRules_Toggle, Strings.MsgRules_KeySpace, () => _presenter.ToggleRuleAsync()));

        Controls.AddRange([
            lblSets, _listSets, btnNewSet, _btnDuplicate, _btnRename, _btnRemoveSet, lblType, _cboType,
            _lblRules, _listRules, _btnAddRule, _btnEditRule, _btnRemoveRule, _btnUp, _btnDown, _btnToggle, _lblKeys,
            _lblScript, _txtScript, _lblTabHint, _btnSaveScript,
            lblSample, _txtSample, _btnTest, lblResult, _txtResult, _lblStatus, btnClose]);
        CancelButton = btnClose;
        AcceptButton = _btnEditRule;

        // Enter does what the focused area expects: rename a set, edit a rule, test the sample.
        // (In the script box, and in the sample box of a script set, Enter is a line break.)
        _listSets.Enter += (_, _) => AcceptButton = _btnRename;
        _listRules.Enter += (_, _) => AcceptButton = _btnEditRule;
        _txtSample.Enter += (_, _) => AcceptButton = _btnTest;
        _txtSample.Leave += (_, _) => AcceptButton = DefaultAcceptButton;

        ShowType(script: false);
        UpdateButtons();
    }

    internal MessageRulesPresenter Presenter => _presenter;

    private Button DefaultAcceptButton => _presenter.IsScriptSet ? _btnTest : _btnEditRule;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Run(LoadAsync);
    }

    internal Task LoadAsync() => _presenter.LoadAsync();

    private void Run(Func<Task> action) => ErrorReporter.Run(this, action);

    private ContextMenuStrip BuildMenu(params (string Text, string? Keys, Func<Task> Action)[] items)
    {
        var menu = new ContextMenuStrip();
        foreach (var (text, keys, action) in items)
            menu.Items.Add(new ToolStripMenuItem(text, null, (_, _) => Run(action)) { ShortcutKeyDisplayString = keys });
        return menu;
    }

    // ── Painting ───────────────────────────────────────────────────────────

    private void Repaint()
    {
        _painting = true;
        try
        {
            _listSets.BeginUpdate();
            _listSets.Items.Clear();
            foreach (var set in _presenter.Sets)
                _listSets.Items.Add(new Choice<int>(set.Id, MessageRulesPresenter.DisplayName(set)));
            _listSets.EndUpdate();
            // A list is never left without a selected element.
            _listSets.SelectedIndex = _presenter.SelectedSet is { } selected
                ? _presenter.Sets.ToList().FindIndex(s => s.Id == selected.Id)
                : -1;

            _cboType.SelectedIndex = _presenter.IsScriptSet ? 1 : 0;
            ShowType(_presenter.IsScriptSet);

            _listRules.BeginUpdate();
            _listRules.Items.Clear();
            foreach (var rule in _presenter.Rules)
                _listRules.Items.Add(new ListViewItem([
                    rule.Pattern,
                    rule.Enabled ? Strings.MsgRules_StateEnabled : Strings.MsgRules_StateDisabled,
                    rule.Template,
                    rule.Channel ?? string.Empty,
                    rule.CaseSensitive ? Strings.Common_Yes : Strings.Common_No]) { Tag = rule });
            _listRules.EndUpdate();

            if (_presenter.SelectedRuleIndex >= 0)
            {
                var item = _listRules.Items[_presenter.SelectedRuleIndex];
                item.Selected = true;
                item.Focused = true;
                item.EnsureVisible();
            }

            // The box keeps its caret while the text is the same (saving must not send it to the top).
            var script = _presenter.ScriptDraft.ReplaceLineEndings("\r\n");
            if (_txtScript.Text != script) _txtScript.Text = script;
            _txtScript.ReadOnly = !_presenter.CanSaveScript;
        }
        finally
        {
            _painting = false;
        }

        var status = _presenter.Status;
        if (_presenter.SelectedSet is { IsBuiltIn: true } builtIn)
            status = (status.Length > 0 ? status + " " : string.Empty) + string.Format(Strings.MsgRules_BuiltInReadOnly, builtIn.Name);
        _lblStatus.Text = status;
        UpdateButtons();
    }

    /// <summary>The right-hand area shows either the rules of a pattern set or the script of a script set.</summary>
    private void ShowType(bool script)
    {
        foreach (var control in new Control[] { _lblRules, _listRules, _btnAddRule, _btnEditRule, _btnRemoveRule, _btnUp, _btnDown, _btnToggle, _lblKeys })
            control.Visible = !script;
        foreach (var control in new Control[] { _lblScript, _txtScript, _lblTabHint, _btnSaveScript })
            control.Visible = script;

        // A script is tried against a block of several lines: there Enter is a line break.
        _txtSample.AcceptsReturn = script;
        if (AcceptButton != _btnRename && AcceptButton != _btnTest)
            AcceptButton = DefaultAcceptButton;
    }

    private void UpdateButtons()
    {
        _btnDuplicate.Enabled = _presenter.CanDuplicateSet;
        _btnRename.Enabled = _presenter.CanModifySet;
        _btnRemoveSet.Enabled = _presenter.CanModifySet;
        _cboType.Enabled = _presenter.CanChangeType;
        _btnAddRule.Enabled = _presenter.CanAddRule;
        _btnEditRule.Enabled = _presenter.CanModifyRule;
        _btnRemoveRule.Enabled = _presenter.CanModifyRule;
        _btnToggle.Enabled = _presenter.CanModifyRule;
        _btnUp.Enabled = _presenter.CanMoveUp;
        _btnDown.Enabled = _presenter.CanMoveDown;
        _btnSaveScript.Enabled = _presenter.CanSaveScript;
        _btnTest.Enabled = _presenter.SelectedSet is not null;
    }

    private void SetSelected()
    {
        if (_painting || _listSets.SelectedItem is not Choice<int> choice) return;
        Run(() => _presenter.SelectSetAsync(choice.Value));
    }

    private void TypeSelected()
    {
        if (_painting || _cboType.SelectedIndex < 0) return;
        Run(() => _presenter.SetTypeAsync(_cboType.SelectedIndex == 1));
    }

    private void RuleSelected()
    {
        if (_painting || _listRules.SelectedIndices.Count == 0) return;
        _presenter.SelectRule(_listRules.SelectedIndices[0]);
        UpdateButtons();
    }

    private void ScriptEdited()
    {
        if (!_painting) _presenter.ScriptDraft = _txtScript.Text;
    }

    // ── Script ─────────────────────────────────────────────────────────────

    /// <summary>Validates and saves the script; on a syntax error the presenter warns and the caret goes to the line.</summary>
    internal Task SaveScriptAsync()
    {
        _presenter.ScriptDraft = _txtScript.Text;
        return _presenter.SaveScriptAsync();
    }

    private void ScriptRejected(EditorIssue issue)
    {
        FormKit.FocusField(this, _txtScript);
        if (issue.Line is { } line) MoveCaretToLine(line);
        else _txtScript.Select(_txtScript.TextLength, 0);
    }

    private void MoveCaretToLine(int line)
    {
        var (start, length) = TriggerEditorModel.LineSpan(_txtScript.Text, line);
        _txtScript.Select(start, length);
        if (_txtScript.IsHandleCreated) _txtScript.ScrollToCaret();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel || _closeConfirmed || !_presenter.IsScriptDirty) return;

        // Unsaved script: ask (through the presenter) and close afterwards unless the script was rejected.
        e.Cancel = true;
        Run(async () =>
        {
            if (!await _presenter.LeaveScriptAsync()) return;
            _closeConfirmed = true;
            Close();
        });
    }

    // ── Keyboard ───────────────────────────────────────────────────────────

    private void Sets_KeyDown(object? sender, KeyEventArgs e)
    {
        Func<Task>? action = e.KeyCode switch
        {
            Keys.Delete => () => _presenter.RemoveSetAsync(),
            Keys.Insert => () => _presenter.AddSetAsync(),
            Keys.F2 => () => _presenter.RenameSetAsync(),
            _ => null,
        };
        if (action is null) return;
        e.Handled = e.SuppressKeyPress = true;
        Run(action);
    }

    private void Rules_KeyDown(object? sender, KeyEventArgs e)
    {
        Func<Task>? action = e switch
        {
            { Alt: true, KeyCode: Keys.Up } => () => _presenter.MoveUpAsync(),
            { Alt: true, KeyCode: Keys.Down } => () => _presenter.MoveDownAsync(),
            { Modifiers: Keys.None, KeyCode: Keys.Delete } => () => _presenter.RemoveRuleAsync(),
            { Modifiers: Keys.None, KeyCode: Keys.Insert } => () => _presenter.AddRuleAsync(),
            { Modifiers: Keys.None, KeyCode: Keys.F2 } => () => _presenter.EditRuleAsync(),
            { Modifiers: Keys.None, KeyCode: Keys.Space } => () => _presenter.ToggleRuleAsync(),
            _ => null,
        };
        if (action is null) return;
        e.Handled = e.SuppressKeyPress = true;
        Run(action);
    }

    // ── Test ───────────────────────────────────────────────────────────────

    internal void RunTest() => Run(RunTestAsync);

    /// <summary>
    /// Pattern set: shows the outcome and moves the focus to the result box, so the screen reader reads it.
    /// Script set: runs the script in the box (saved or not) over the sample block; the outcome is shown
    /// and announced, and on a script error the caret is left on its line.
    /// </summary>
    internal async Task RunTestAsync()
    {
        if (!_presenter.IsScriptSet)
        {
            _txtResult.Text = _presenter.TestSet(_txtSample.Text).Text;
            FormKit.FocusField(this, _txtResult);
            return;
        }

        _presenter.ScriptDraft = _txtScript.Text;
        _btnTest.Enabled = false;
        try
        {
            var result = await _presenter.TestScriptAsync(_txtSample.Text);
            if (IsDisposed) return;
            _txtResult.Text = result.Text.ReplaceLineEndings("\r\n");
            if (result.ErrorLine is { } line) MoveCaretToLine(line);
        }
        finally
        {
            if (!IsDisposed) _btnTest.Enabled = _presenter.SelectedSet is not null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _monospace.Dispose();
            if (_ownsEngine) _scriptEngine.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>The real editors of the message rules window.</summary>
internal sealed class MessageRulesDialogs(IWin32Window owner, IUserPrompts prompts) : IMessageRulesDialogs
{
    public string? AskSetName(string title, string initialName)
    {
        using var dialog = new FrmMessageRuleSetName(title, initialName);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.SetName : null;
    }

    public NewMessageRuleSet? AskNewSet(string title, string initialName, bool initialIsScript)
    {
        using var dialog = new FrmMessageRuleSetName(title, initialName, initialIsScript);
        return dialog.ShowDialog(owner) == DialogResult.OK ? new NewMessageRuleSet(dialog.SetName, dialog.IsScript) : null;
    }

    public bool EditRule(MessageRuleEditorModel model)
    {
        using var dialog = new FrmAddEditMessageRule(model, prompts);
        return dialog.ShowDialog(owner) == DialogResult.OK;
    }
}

/// <summary>Asks for the name of a rule set (new, duplicate, rename) and, for a new one, its type.</summary>
public sealed class FrmMessageRuleSetName : Form
{
    private readonly TextBox _txtName;
    private readonly ComboBox? _cboType;

    /// <param name="isScript">Null = only the name is asked. Otherwise the type is asked too, starting at this value.</param>
    public FrmMessageRuleSetName(string title, string initialName, bool? isScript = null)
    {
        var withType = isScript is not null;
        FormKit.SetupDialog(this, nameof(FrmMessageRuleSetName), title, new Size(440, withType ? 170 : 110));

        var tab = 0;
        var lblName = FormKit.Label("_lblName", Strings.MsgRules_SetNameLabel, 12, 12, tab++);
        _txtName = FormKit.TextBox("_txtName", lblName, new Rectangle(12, 38, 416, 25), tab++);
        _txtName.MaxLength = 255;
        _txtName.Text = initialName;
        _txtName.SelectAll();
        Controls.AddRange([lblName, _txtName]);

        if (withType)
        {
            var lblType = FormKit.Label("_lblType", Strings.MsgRules_NewSetTypeLabel, 12, 72, tab++);
            _cboType = new ComboBox
            {
                Name = "_cboType", AccessibleName = FormKit.NameFrom(lblType.Text), Bounds = new Rectangle(12, 98, 416, 25),
                TabIndex = tab++, DropDownStyle = ComboBoxStyle.DropDownList,
            };
            _cboType.Items.AddRange([Strings.MsgRules_TypePatterns, Strings.MsgRules_TypeScript]);
            _cboType.SelectedIndex = isScript == true ? 1 : 0;
            Controls.AddRange([lblType, _cboType]);
        }

        var top = withType ? 132 : 72;
        var btnOk = FormKit.Button("_btnOk", Strings.Common_OKButton, new Rectangle(212, top, 104, 30), tab++);
        btnOk.DialogResult = DialogResult.OK;
        var btnCancel = FormKit.Button("_btnCancel", Strings.Common_CancelButton, new Rectangle(324, top, 104, 30), tab);
        btnCancel.DialogResult = DialogResult.Cancel;

        Controls.AddRange([btnOk, btnCancel]);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    public string SetName => _txtName.Text.Trim();

    /// <summary>The type chosen for a new set. False when the type was not asked.</summary>
    public bool IsScript => _cboType?.SelectedIndex == 1;
}
