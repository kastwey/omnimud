using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;

namespace Omnimud.UI.Forms;

/// <summary>
/// Add or edit a character. The stored password is never shown: the box is always empty and a
/// visible hint says what leaving it empty means. Logic lives in <see cref="CharacterEditorModel"/>.
/// </summary>
public sealed class FrmAddEditCharacter : Form
{
    private readonly CharacterEditorModel _model;
    private readonly IUserPrompts _prompts;

    private readonly TextBox _txtName;
    private readonly CheckBox _chkRemember;
    private readonly Label _lblPassword;
    private readonly TextBox _txtPassword;
    private readonly Label _lblPasswordHint;
    private readonly ComboBox _cboMud;
    private bool _loaded;

    public FrmAddEditCharacter(CharacterEditorModel model, IUserPrompts prompts)
    {
        _model = model;
        _prompts = prompts;

        const int labelX = 12, fieldX = 170, fieldW = 300, row = 34;
        FormKit.SetupDialog(this, nameof(FrmAddEditCharacter), model.Title, new Size(490, 260));

        var tab = 0;
        var y = 12;

        var lblName = FormKit.Label("_lblName", Strings.CharEdit_NameLabel, labelX, y, tab++);
        _txtName = FormKit.TextBox("_txtName", lblName, new Rectangle(fieldX, y, fieldW, 25), tab++);
        _txtName.MaxLength = CharacterEditorModel.MaxNameLength;
        y += row;

        _chkRemember = FormKit.CheckBox("_chkRemember", Strings.CharEdit_Remember, fieldX, y, tab++);
        y += row - 4;

        _lblPassword = FormKit.Label("_lblPassword", Strings.CharEdit_PasswordLabel, labelX, y, tab++);
        _txtPassword = FormKit.TextBox("_txtPassword", _lblPassword, new Rectangle(fieldX, y, fieldW, 25), tab++);
        _txtPassword.UseSystemPasswordChar = true;
        _txtPassword.MaxLength = 255;
        y += row - 4;
        _lblPasswordHint = FormKit.Hint("_lblPasswordHint", string.Empty, new Rectangle(fieldX, y, fieldW, 40), tab++);
        y += 46;

        var lblMud = FormKit.Label("_lblMud", Strings.CharEdit_MudLabel, labelX, y, tab++);
        _cboMud = new ComboBox
        {
            Name = "_cboMud", AccessibleName = FormKit.NameFrom(lblMud.Text), Bounds = new Rectangle(fieldX, y, fieldW, 25),
            DropDownStyle = ComboBoxStyle.DropDownList, TabIndex = tab++, Enabled = model.CanChooseMud,
        };
        y += row + 8;

        var btnOk = FormKit.Button("_btnOk", Strings.Common_OKButton, new Rectangle(fieldX + fieldW - 216, y, 104, 30), tab++,
            () => ErrorReporter.Run(this, AcceptAsync));
        var btnCancel = FormKit.Button("_btnCancel", Strings.Common_CancelButton, new Rectangle(fieldX + fieldW - 104, y, 104, 30), tab);
        btnCancel.DialogResult = DialogResult.Cancel;
        ClientSize = new Size(490, y + 42);

        Controls.AddRange([lblName, _txtName, _chkRemember, _lblPassword, _txtPassword, _lblPasswordHint, lblMud, _cboMud, btnOk, btnCancel]);
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        _txtName.Text = model.Name;
        _txtName.SelectAll();
        _chkRemember.Checked = model.RememberPassword;
        _chkRemember.CheckedChanged += (_, _) => RememberChanged();
        RememberChanged();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ErrorReporter.Run(this, LoadAsync);
    }

    /// <summary>Fills the MUD combo.</summary>
    internal async Task LoadAsync()
    {
        if (_loaded) return;
        _loaded = true;

        var choices = await _model.GetMudChoicesAsync();
        _cboMud.BeginUpdate();
        _cboMud.Items.Clear();
        foreach (var choice in choices) _cboMud.Items.Add(choice);
        _cboMud.EndUpdate();
        _cboMud.SelectedItem = choices.FirstOrDefault(c => c.Value == _model.MudId);
    }

    private void RememberChanged()
    {
        _model.RememberPassword = _chkRemember.Checked;
        _lblPassword.Enabled = _model.CanEditPassword;
        _txtPassword.Enabled = _model.CanEditPassword;
        if (!_model.CanEditPassword) _txtPassword.Clear();
        _lblPasswordHint.Text = _model.PasswordHint;
    }

    internal async Task<bool> AcceptAsync()
    {
        _model.Name = _txtName.Text;
        _model.RememberPassword = _chkRemember.Checked;
        _model.Password = _txtPassword.Text;
        if (_cboMud.SelectedItem is Choice<int> mud) _model.MudId = mud.Value;

        if (await _model.SaveAsync() is { } error)
        {
            _prompts.Warn(error.Message, Text);
            FormKit.FocusField(this, error.Field switch
            {
                CharacterField.Password => _txtPassword,
                CharacterField.Mud => _cboMud,
                _ => _txtName,
            });
            return false;
        }

        DialogResult = DialogResult.OK;
        return true;
    }
}
