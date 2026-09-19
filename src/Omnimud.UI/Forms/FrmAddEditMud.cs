using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;

namespace Omnimud.UI.Forms;

/// <summary>Add or edit a MUD. Only binds controls: validation and saving live in <see cref="MudEditorModel"/>.</summary>
public sealed class FrmAddEditMud : Form
{
    private readonly MudEditorModel _model;
    private readonly IUserPrompts _prompts;

    private readonly TextBox _txtName;
    private readonly TextBox _txtHost;
    private readonly NumericUpDown _nudPort;
    private readonly CheckBox _chkTls;
    private readonly CheckBox _chkValidate;
    private readonly ComboBox _cboEncoding;
    private readonly ComboBox _cboRuleSet;
    private readonly TextBox _txtSound;
    private readonly TextBox _txtSaveCommand;
    private readonly TextBox _txtQuitCommand;
    private readonly TextBox _txtLoginScript;
    private bool _loaded;

    public FrmAddEditMud(MudEditorModel model, IUserPrompts prompts)
    {
        _model = model;
        _prompts = prompts;

        const int labelX = 12, fieldX = 250, fieldW = 330, row = 34;
        FormKit.SetupDialog(this, nameof(FrmAddEditMud), model.Title, new Size(600, 590));

        var tab = 0;
        var y = 12;

        var lblName = FormKit.Label("_lblName", Strings.MudEdit_NameLabel, labelX, y, tab++);
        _txtName = FormKit.TextBox("_txtName", lblName, new Rectangle(fieldX, y, fieldW, 25), tab++);
        _txtName.MaxLength = MudEditorModel.MaxNameLength;
        y += row;

        var lblHost = FormKit.Label("_lblHost", Strings.MudEdit_HostLabel, labelX, y, tab++);
        _txtHost = FormKit.TextBox("_txtHost", lblHost, new Rectangle(fieldX, y, fieldW, 25), tab++);
        _txtHost.MaxLength = 255;
        y += row;

        var lblPort = FormKit.Label("_lblPort", Strings.MudEdit_PortLabel, labelX, y, tab++);
        _nudPort = new NumericUpDown
        {
            Name = "_nudPort", AccessibleName = FormKit.NameFrom(lblPort.Text), Bounds = new Rectangle(fieldX, y, 100, 25),
            Minimum = 1, Maximum = 65535, Value = 23, TabIndex = tab++,
        };
        y += row;

        _chkTls = FormKit.CheckBox("_chkTls", Strings.MudEdit_UseTls, fieldX, y, tab++);
        y += row - 4;
        _chkValidate = FormKit.CheckBox("_chkValidate", Strings.MudEdit_ValidateCertificate, fieldX, y, tab++);
        _chkTls.CheckedChanged += (_, _) => _chkValidate.Enabled = _chkTls.Checked;
        y += row;

        var lblEncoding = FormKit.Label("_lblEncoding", Strings.MudEdit_EncodingLabel, labelX, y, tab++);
        _cboEncoding = new ComboBox
        {
            Name = "_cboEncoding", AccessibleName = FormKit.NameFrom(lblEncoding.Text), Bounds = new Rectangle(fieldX, y, 200, 25),
            DropDownStyle = ComboBoxStyle.DropDown, TabIndex = tab++,
        };
        _cboEncoding.Items.AddRange([.. MudEncodings.Common]);
        y += row;

        var lblRuleSet = FormKit.Label("_lblRuleSet", Strings.MudEdit_RuleSetLabel, labelX, y, tab++);
        _cboRuleSet = new ComboBox
        {
            Name = "_cboRuleSet", AccessibleName = FormKit.NameFrom(lblRuleSet.Text), Bounds = new Rectangle(fieldX, y, fieldW, 25),
            DropDownStyle = ComboBoxStyle.DropDownList, TabIndex = tab++,
        };
        y += row;

        var lblSound = FormKit.Label("_lblSound", Strings.MudEdit_SoundLabel, labelX, y, tab++);
        _txtSound = FormKit.TextBox("_txtSound", lblSound, new Rectangle(fieldX, y, fieldW - 110, 25), tab++);
        var btnBrowse = FormKit.Button("_btnBrowse", Strings.MudEdit_Browse, new Rectangle(fieldX + fieldW - 104, y - 2, 104, 29), tab++, BrowseSoundFolder);
        y += row - 6;
        var lblSoundHint = FormKit.Hint("_lblSoundHint", Strings.MudEdit_SoundHint, new Rectangle(fieldX, y, fieldW, 20), tab++);
        y += row - 8;

        var lblSave = FormKit.Label("_lblSaveCommand", Strings.MudEdit_SaveCommandLabel, labelX, y, tab++);
        _txtSaveCommand = FormKit.TextBox("_txtSaveCommand", lblSave, new Rectangle(fieldX, y, fieldW, 25), tab++);
        _txtSaveCommand.MaxLength = 255;
        y += row;

        var lblQuit = FormKit.Label("_lblQuitCommand", Strings.MudEdit_QuitCommandLabel, labelX, y, tab++);
        _txtQuitCommand = FormKit.TextBox("_txtQuitCommand", lblQuit, new Rectangle(fieldX, y, fieldW, 25), tab++);
        _txtQuitCommand.MaxLength = 255;
        y += row;

        var lblLogin = FormKit.Label("_lblLoginScript", Strings.MudEdit_LoginScriptLabel, labelX, y, tab++);
        _txtLoginScript = FormKit.TextBox("_txtLoginScript", lblLogin, new Rectangle(fieldX, y, fieldW, 96), tab++);
        _txtLoginScript.Multiline = true;
        _txtLoginScript.AcceptsReturn = true;
        _txtLoginScript.ScrollBars = ScrollBars.Vertical;
        _txtLoginScript.WordWrap = false;
        y += 102;
        var lblLoginHint = FormKit.Hint("_lblLoginHint", Strings.MudEdit_LoginScriptHint, new Rectangle(labelX, y, fieldX + fieldW - labelX, 58), tab++);
        y += 64;

        var btnOk = FormKit.Button("_btnOk", Strings.Common_OKButton, new Rectangle(fieldX + fieldW - 216, y, 104, 30), tab++,
            () => ErrorReporter.Run(this, AcceptAsync));
        var btnCancel = FormKit.Button("_btnCancel", Strings.Common_CancelButton, new Rectangle(fieldX + fieldW - 104, y, 104, 30), tab,
            () => DialogResult = DialogResult.Cancel);
        btnCancel.DialogResult = DialogResult.Cancel;
        ClientSize = new Size(600, y + 42);

        Controls.AddRange([
            lblName, _txtName, lblHost, _txtHost, lblPort, _nudPort, _chkTls, _chkValidate, lblEncoding, _cboEncoding,
            lblRuleSet, _cboRuleSet, lblSound, _txtSound, btnBrowse, lblSoundHint, lblSave, _txtSaveCommand, lblQuit, _txtQuitCommand,
            lblLogin, _txtLoginScript, lblLoginHint, btnOk, btnCancel]);
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        ModelToControls();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ErrorReporter.Run(this, LoadAsync);
    }

    /// <summary>Fills the rule set combo (the only part that needs the database).</summary>
    internal async Task LoadAsync()
    {
        if (_loaded) return;
        _loaded = true;

        var choices = await _model.GetRuleSetChoicesAsync();
        _cboRuleSet.BeginUpdate();
        _cboRuleSet.Items.Clear();
        foreach (var choice in choices) _cboRuleSet.Items.Add(choice);
        _cboRuleSet.EndUpdate();
        _cboRuleSet.SelectedItem = choices.FirstOrDefault(c => c.Value == _model.MessageRuleSetId) ?? choices[0];
    }

    private void ModelToControls()
    {
        _txtName.Text = _model.Name;
        _txtHost.Text = _model.Host;
        _nudPort.Value = Math.Clamp(_model.Port, 1, 65535);
        _chkTls.Checked = _model.UseTls;
        _chkValidate.Checked = _model.ValidateCertificate;
        _chkValidate.Enabled = _model.CanValidateCertificate;
        _cboEncoding.Text = _model.Encoding;
        _txtSound.Text = _model.SoundDirectory;
        _txtSaveCommand.Text = _model.SaveCommand;
        _txtQuitCommand.Text = _model.QuitCommand;
        _txtLoginScript.Text = _model.LoginScript.ReplaceLineEndings(Environment.NewLine);

        // Loaded boxes come with all their text selected: typing replaces it.
        foreach (var box in new[] { _txtName, _txtHost, _txtSound, _txtSaveCommand, _txtQuitCommand })
            box.SelectAll();
    }

    private void ControlsToModel()
    {
        _model.Name = _txtName.Text;
        _model.Host = _txtHost.Text;
        _model.Port = (int)_nudPort.Value;
        _model.UseTls = _chkTls.Checked;
        _model.ValidateCertificate = _chkValidate.Checked;
        _model.Encoding = _cboEncoding.Text;
        if (_cboRuleSet.SelectedItem is Choice<int?> choice)
            _model.MessageRuleSetId = choice.Value;
        _model.SoundDirectory = _txtSound.Text;
        _model.SaveCommand = _txtSaveCommand.Text;
        _model.QuitCommand = _txtQuitCommand.Text;
        _model.LoginScript = _txtLoginScript.Text;
    }

    /// <summary>OK: saves through the model. On a problem, tells it and moves the focus to the field.</summary>
    internal async Task<bool> AcceptAsync()
    {
        ControlsToModel();
        if (await _model.SaveAsync() is { } error)
        {
            _prompts.Warn(error.Message, Text);
            FormKit.FocusField(this, ControlOf(error.Field));
            return false;
        }

        DialogResult = DialogResult.OK;
        return true;
    }

    private Control ControlOf(MudField field) => field switch
    {
        MudField.Host => _txtHost,
        MudField.Port => _nudPort,
        MudField.Encoding => _cboEncoding,
        MudField.SoundDirectory => _txtSound,
        _ => _txtName,
    };

    internal void BrowseSoundFolder()
    {
        if (_prompts.PickFolder(Strings.MudEdit_SoundBrowseTitle, _txtSound.Text.Trim()) is not { } folder) return;
        _txtSound.Text = folder;
        FormKit.FocusField(this, _txtSound);
    }
}
