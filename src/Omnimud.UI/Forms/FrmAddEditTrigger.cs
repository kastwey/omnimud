using Omnimud.Core.Scripting;
using Omnimud.Core.Session;
using Omnimud.Core.Sound;
using Omnimud.Core.Text;
using Omnimud.Data.Entities;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;
using Omnimud.UI.Services.Accessibility;
using PatternKind = Omnimud.Core.Triggers.PatternType;

namespace Omnimud.UI.Forms;

/// <summary>
/// Add/edit one trigger. The form only moves values between the controls and
/// <see cref="TriggerEditorModel"/>, which decides what is available and what is valid;
/// the Test button uses <see cref="TriggerTester"/>.
/// </summary>
public sealed class FrmAddEditTrigger : Form
{
    private readonly TriggerEditorModel _model;
    private readonly IUserPrompts _prompts;
    private readonly IScriptEngine? _scriptEngine;
    private readonly bool _ownsEngine;
    private readonly IAnnouncer _announcer;
    private readonly ISoundPlayer? _soundPlayer;
    private readonly TriggerTester _tester;

    private readonly TextBox _txtName;
    private readonly TextBox _txtPattern;
    private readonly Label _lblCommandInfo;
    private readonly ComboBox _cboPatternType;
    private readonly CheckBox _chkCaseSensitive;
    private readonly CheckBox _chkMultiline;
    private readonly CheckBox _chkGag;
    private readonly NumericUpDown _nudPriority;
    private readonly CheckBox _chkEnabled;
    private readonly ComboBox _cboActionType;
    private readonly Label _lblAction;
    private readonly Label _lblTabHint;
    private readonly TextBox _txtAction;
    private readonly TextBox _txtSound;
    private readonly Button _btnBrowse;
    private readonly Button _btnPlaySound;
    private readonly TextBox _txtSample;
    private readonly Button _btnTest;
    private readonly TextBox _txtResult;

    private readonly Font _monospace = new("Consolas", 10F);
    private bool _syncing;
    private bool _wasCommandTrigger;

    /// <summary>Compatible with the previous dialog: real message boxes and a script engine of its own.</summary>
    public FrmAddEditTrigger(TriggerEntity? existing = null)
        : this(new TriggerEditorModel(existing, existing is null), new WinFormsUserPrompts(), null, null, null)
    {
    }

    /// <param name="scriptEngine">Validates and tests Lua. Null = the dialog creates (and disposes) one.</param>
    /// <param name="announcer">Null = UI Automation notifications raised from the dialog itself.</param>
    /// <param name="soundPlayer">Null = no "Play sound" button.</param>
    public FrmAddEditTrigger(TriggerEditorModel model, IUserPrompts prompts, IScriptEngine? scriptEngine,
        IAnnouncer? announcer = null, ISoundPlayer? soundPlayer = null)
    {
        _model = model;
        _prompts = prompts;
        _ownsEngine = scriptEngine is null;
        _scriptEngine = scriptEngine ?? new LuaScriptEngine();
        _soundPlayer = soundPlayer;
        _tester = new TriggerTester(_scriptEngine);

        EditorDialogs.Prepare(this, nameof(FrmAddEditTrigger), model.IsNew ? Strings.TrigEdit_AddTitle : Strings.TrigEdit_EditTitle, new Size(640, 716));
        AutoScroll = true; // small screens and big fonts

        const int left = 12, width = 616, right = 330;
        var tab = 0;

        var lblName = EditorDialogs.Label("_lblName", Strings.TrigEdit_NameLabel, left, 10, tab++);
        _txtName = EditorDialogs.TextBox("_txtName", Strings.TrigEdit_NameName, left, 30, width, tab++);

        var lblPattern = EditorDialogs.Label("_lblPattern", Strings.TrigEdit_PatternLabel, left, 62, tab++);
        _txtPattern = EditorDialogs.TextBox("_txtPattern", Strings.TrigEdit_PatternName, left, 82, width, tab++);

        // Explains command triggers when the text starts with "@". Also announced, since the focus does not show it.
        _lblCommandInfo = new Label { Name = "_lblCommandInfo", AutoSize = false, UseMnemonic = false, Location = new Point(left, 112), Size = new Size(width, 36), TabIndex = tab++ };

        var lblPatternType = EditorDialogs.Label("_lblPatternType", Strings.TrigEdit_PatternTypeLabel, left, 152, tab++);
        _cboPatternType = new ComboBox
        {
            Name = "_cboPatternType", AccessibleName = Strings.TrigEdit_PatternTypeName, DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(left, 172), Size = new Size(300, 25), TabIndex = tab++,
        };
        _cboPatternType.Items.AddRange([Strings.TrigEdit_TypeLiteral, Strings.TrigEdit_TypeRegex, Strings.TrigEdit_TypeWildcards]);

        _chkCaseSensitive = Check("_chkCaseSensitive", Strings.TrigEdit_CaseSensitive, right, 150, tab++);
        _chkMultiline = Check("_chkMultiline", Strings.TrigEdit_Multiline, right, 174, tab++);
        _chkGag = Check("_chkGag", Strings.TrigEdit_GagLine, right, 198, tab++);

        var lblPriority = EditorDialogs.Label("_lblPriority", Strings.TrigEdit_PriorityLabel, left, 204, tab++);
        _nudPriority = new NumericUpDown
        {
            Name = "_nudPriority", AccessibleName = Strings.TrigEdit_PriorityName, Location = new Point(left, 224), Size = new Size(90, 25),
            Minimum = TriggerEditorModel.MinPriority, Maximum = TriggerEditorModel.MaxPriority, TabIndex = tab++,
        };
        _chkEnabled = Check("_chkEnabled", Strings.TrigEdit_Enabled, 130, 226, tab++);

        var lblActionType = EditorDialogs.Label("_lblActionType", Strings.TrigEdit_DoLabel, left, 258, tab++);
        _cboActionType = new ComboBox
        {
            Name = "_cboActionType", AccessibleName = Strings.TrigEdit_DoName, DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(left, 278), Size = new Size(300, 25), TabIndex = tab++,
        };
        _cboActionType.Items.AddRange([Strings.TrigEdit_DoCommand, Strings.TrigEdit_DoSound, Strings.TrigEdit_DoBoth, Strings.TrigEdit_DoScript]);

        _lblAction = EditorDialogs.Label("_lblAction", Strings.TrigEdit_ActionLabel, left, 312, tab++);
        _txtAction = new TextBox
        {
            Name = "_txtAction", AccessibleName = Strings.TrigEdit_ActionName, Location = new Point(left, 332), Size = new Size(width, 130),
            Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, TabIndex = tab++,
        };
        // Visible and short; not an AccessibleDescription, which would be read on every focus.
        _lblTabHint = new Label { Name = "_lblTabHint", Text = Strings.TrigEdit_TabHint, AutoSize = true, UseMnemonic = false, Location = new Point(right, 312), TabIndex = tab++ };

        var lblSound = EditorDialogs.Label("_lblSound", Strings.TrigEdit_SoundLabel, left, 470, tab++);
        _txtSound = EditorDialogs.TextBox("_txtSound", Strings.TrigEdit_SoundName, left, 490, 380, tab++);
        _btnBrowse = new Button { Name = "_btnBrowse", Text = Strings.TrigEdit_Browse, Location = new Point(400, 488), Size = new Size(104, 30), TabIndex = tab++ };
        _btnBrowse.Click += (_, _) => Browse();
        _btnPlaySound = new Button { Name = "_btnPlaySound", Text = Strings.TrigEdit_PlaySound, Location = new Point(510, 488), Size = new Size(118, 30), TabIndex = tab++, Visible = soundPlayer is not null };
        _btnPlaySound.Click += (_, _) => PlaySound();

        var lblSample = EditorDialogs.Label("_lblSample", Strings.TrigEdit_SampleLabel, left, 526, tab++);
        _txtSample = EditorDialogs.TextBox("_txtSample", Strings.TrigEdit_SampleName, left, 546, 506, tab++);
        _btnTest = new Button { Name = "_btnTest", Text = Strings.TrigEdit_Test, Location = new Point(524, 544), Size = new Size(104, 30), TabIndex = tab++ };
        _btnTest.Click += (_, _) => ErrorReporter.Run(this, TestAsync);

        var lblResult = EditorDialogs.Label("_lblResult", Strings.TrigEdit_ResultLabel, left, 580, tab++);
        _txtResult = new TextBox
        {
            Name = "_txtResult", AccessibleName = Strings.TrigEdit_ResultName, Location = new Point(left, 600), Size = new Size(width, 66),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, TabIndex = tab++,
        };

        Controls.AddRange(
        [
            lblName, _txtName, lblPattern, _txtPattern, _lblCommandInfo, lblPatternType, _cboPatternType,
            _chkCaseSensitive, _chkMultiline, _chkGag, lblPriority, _nudPriority, _chkEnabled,
            lblActionType, _cboActionType, _lblAction, _txtAction, _lblTabHint,
            lblSound, _txtSound, _btnBrowse, _btnPlaySound, lblSample, _txtSample, _btnTest, lblResult, _txtResult,
        ]);
        EditorDialogs.Buttons(this, tab, () => { if (TryAccept()) DialogResult = DialogResult.OK; });

        if (announcer is null)
        {
            var own = new FormAnnouncer();
            own.Attach(_btnTest);
            announcer = own;
        }
        _announcer = announcer;

        Push();
        _wasCommandTrigger = _model.IsCommandTrigger;

        _txtPattern.TextChanged += (_, _) => OnEdited();
        _cboPatternType.SelectedIndexChanged += (_, _) => OnEdited();
        _chkMultiline.CheckedChanged += (_, _) => OnEdited();
        _cboActionType.SelectedIndexChanged += (_, _) => OnEdited();
    }

    private static CheckBox Check(string name, string text, int left, int top, int tabIndex) =>
        new() { Name = name, Text = text, AutoSize = true, Location = new Point(left, top), TabIndex = tabIndex };

    // ── Values of the previous dialog, kept for its callers ──────────────

    public string TriggerName => _txtName.Text.Trim();
    public string Pattern => _txtPattern.Text.Trim();
    public int PatternType => Entity.PatternType;
    public string TriggerAction => Entity.Action;
    public int ActionType => Entity.ActionType;
    public string? Sound => Entity.Sound;
    public int Priority => (int)_nudPriority.Value;
    public bool TriggerEnabled => _chkEnabled.Checked;
    public bool CaseSensitive => _chkCaseSensitive.Checked;
    public bool Multiline => Entity.Multiline;
    public bool GagLine => Entity.GagLine;

    /// <summary>The trigger as it must be stored.</summary>
    public TriggerEntity Entity
    {
        get
        {
            Pull();
            return _model.ToEntity();
        }
    }

    internal string ResultText => _txtResult.Text;

    // ── Model ↔ controls ─────────────────────────────────────────────────

    private void Push()
    {
        _syncing = true;
        _txtName.Text = _model.Name;
        _txtPattern.Text = _model.Pattern;
        _cboPatternType.SelectedIndex = (int)_model.PatternType;
        _chkCaseSensitive.Checked = _model.CaseSensitive;
        _chkMultiline.Checked = _model.Multiline;
        _chkGag.Checked = _model.GagLine;
        _nudPriority.Value = _model.Priority;
        _chkEnabled.Checked = _model.Enabled;
        _cboActionType.SelectedIndex = (int)_model.ActionChoice;
        _txtAction.Text = _model.Action.ReplaceLineEndings("\r\n");
        _txtSound.Text = _model.Sound;
        _syncing = false;
        ApplyState();
    }

    private void Pull()
    {
        _model.Name = _txtName.Text;
        _model.Pattern = _txtPattern.Text;
        if (_cboPatternType.Enabled) _model.PatternType = (PatternKind)Math.Max(_cboPatternType.SelectedIndex, 0);
        _model.CaseSensitive = _chkCaseSensitive.Checked;
        if (_chkMultiline.Enabled) _model.Multiline = _chkMultiline.Checked;
        if (_chkGag.Enabled) _model.GagLine = _chkGag.Checked;
        _model.Priority = (int)_nudPriority.Value;
        _model.Enabled = _chkEnabled.Checked;
        _model.ActionChoice = (TriggerActionChoice)Math.Max(_cboActionType.SelectedIndex, 0);
        _model.Action = _txtAction.Text;
        _model.Sound = _txtSound.Text;
    }

    private void OnEdited()
    {
        if (_syncing) return;
        Pull();
        ApplyState();

        if (_model.IsCommandTrigger && !_wasCommandTrigger)
            _announcer.Announce(Strings.TrigEdit_CommandInfo, AnnouncePriority.MostRecent);
        _wasCommandTrigger = _model.IsCommandTrigger;
    }

    /// <summary>Enables, renames and restyles the controls as the model says.</summary>
    private void ApplyState()
    {
        _syncing = true;

        _lblCommandInfo.Text = _model.IsCommandTrigger ? Strings.TrigEdit_CommandInfo : string.Empty;
        _cboPatternType.Enabled = _model.PatternTypeEnabled;
        _cboPatternType.SelectedIndex = (int)_model.EffectivePatternType;
        _chkMultiline.Enabled = _model.MultilineEnabled;
        _chkMultiline.Checked = _model.EffectiveMultiline;
        _chkGag.Enabled = _model.GagLineEnabled;
        _chkGag.Checked = _model.EffectiveGagLine;

        // Label and AccessibleName change together: the reader must say "Lua script" too.
        _lblAction.Text = _model.ActionLabel;
        _txtAction.AccessibleName = _model.ActionAccessibleName;
        _txtAction.Enabled = _model.ActionEnabled;
        _txtAction.AcceptsReturn = _model.IsScript;
        _txtAction.AcceptsTab = _model.IsScript; // Ctrl+Tab still leaves the box
        _txtAction.Font = _model.IsScript ? _monospace : Font;
        _lblTabHint.Visible = _model.IsScript;

        _txtSound.Enabled = _model.SoundEnabled;
        _btnBrowse.Enabled = _model.SoundEnabled;
        _btnPlaySound.Enabled = _model.SoundEnabled;

        _syncing = false;
    }

    // ── Actions ──────────────────────────────────────────────────────────

    private void Browse()
    {
        if (_prompts.PickOpenFile(Strings.TrigEdit_BrowseTitle, Strings.TrigEdit_SoundFilter) is not { } file) return;
        _txtSound.Text = file;
        EditorDialogs.FocusField(this, _txtSound);
    }

    private void PlaySound()
    {
        var file = _txtSound.Text.Trim();
        if (_soundPlayer is null || file.Length == 0) return;
        if (!File.Exists(file) || _soundPlayer.Play(new SoundPlayRequest { FilePath = file, Type = SoundType.Trigger }) == 0)
            _prompts.Warn(string.Format(Strings.TrigEdit_ErrCannotPlay, file), Text);
    }

    /// <summary>Runs the trigger against the sample text and writes (and announces) what it would do.</summary>
    internal async Task TestAsync()
    {
        Pull();
        _btnTest.Enabled = false;
        try
        {
            string report;
            if ((_model.ValidatePattern() ?? (_model.IsScript ? _model.ValidateAction(_scriptEngine) : null)) is { } issue)
            {
                report = issue.Message;
                if (issue.Line is { } line) MoveCaretToLine(line);
            }
            else
            {
                report = (await _tester.TestAsync(_model.ToDefinition(), _txtSample.Text)).Describe();
            }

            if (IsDisposed) return;
            _txtResult.Text = report.ReplaceLineEndings("\r\n");
            _announcer.Announce(report.ReplaceLineEndings(". "), AnnouncePriority.MostRecent);
        }
        finally
        {
            if (!IsDisposed) _btnTest.Enabled = true;
        }
    }

    /// <summary>Validates; on error shows the message and focuses the field (for Lua, the line). True = may close with OK.</summary>
    internal bool TryAccept()
    {
        Pull();
        return EditorDialogs.Accept(_prompts, Text, _model.Validate(_scriptEngine), _model.Warnings, issue =>
        {
            EditorDialogs.FocusField(this, issue.Field switch
            {
                TriggerEditorModel.FieldName => _txtName,
                TriggerEditorModel.FieldAction => _txtAction,
                TriggerEditorModel.FieldSound => _txtSound,
                _ => _txtPattern,
            });
            if (issue.Line is { } line) MoveCaretToLine(line);
        });
    }

    private void MoveCaretToLine(int line)
    {
        var (start, length) = TriggerEditorModel.LineSpan(_txtAction.Text, line);
        _txtAction.Select(start, length);
        if (_txtAction.IsHandleCreated) _txtAction.ScrollToCaret();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _monospace.Dispose();
            if (_ownsEngine) _scriptEngine?.Dispose();
        }
        base.Dispose(disposing);
    }
}
