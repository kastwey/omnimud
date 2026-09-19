using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;

namespace Omnimud.UI.Forms;

/// <summary>
/// Name and e-mail address of the user, both optional. They only sign a report sent by e-mail (and the user is asked
/// each time). Unlike the original client, nobody is asked for them at startup. Logic in <see cref="PersonalInfoModel"/>.
/// </summary>
public sealed class FrmPersonalInfo : Form
{
    private readonly PersonalInfoModel _model;
    private readonly IUserPrompts _prompts;
    private readonly TextBox _txtName;
    private readonly TextBox _txtEmail;
    private Task? _initialization;

    public FrmPersonalInfo(PersonalInfoModel model, IUserPrompts prompts)
    {
        _model = model;
        _prompts = prompts;

        const int labelX = 12, fieldX = 170, fieldW = 330;
        FormKit.SetupDialog(this, nameof(FrmPersonalInfo), Strings.PersonalInfo_Title, new Size(520, 210));

        var lblHint = FormKit.Hint("_lblHint", Strings.PersonalInfo_Hint, new Rectangle(labelX, 10, 496, 62), 0);

        var lblName = FormKit.Label("_lblName", Strings.PersonalInfo_NameLabel, labelX, 80, 1);
        _txtName = FormKit.TextBox("_txtName", lblName, new Rectangle(fieldX, 80, fieldW, 25), 2);
        _txtName.MaxLength = PersonalInfoModel.MaxLength;

        var lblEmail = FormKit.Label("_lblEmail", Strings.PersonalInfo_EmailLabel, labelX, 114, 3);
        _txtEmail = FormKit.TextBox("_txtEmail", lblEmail, new Rectangle(fieldX, 114, fieldW, 25), 4);
        _txtEmail.MaxLength = PersonalInfoModel.MaxLength;

        var btnOk = FormKit.Button("_btnOk", Strings.Common_OKButton, new Rectangle(fieldX + fieldW - 216, 162, 104, 32), 5,
            () => ErrorReporter.Run(this, AcceptAsync));
        var btnCancel = FormKit.Button("_btnCancel", Strings.Common_CancelButton, new Rectangle(fieldX + fieldW - 104, 162, 104, 32), 6);
        btnCancel.DialogResult = DialogResult.Cancel;

        Controls.AddRange([lblHint, lblName, _txtName, lblEmail, _txtEmail, btnOk, btnCancel]);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
        ActiveControl = _txtName;
    }

    /// <summary>Loads the stored information into the boxes. Runs once; OnLoad calls it if nobody did before.</summary>
    internal Task InitializeAsync() => _initialization ??= InitializeCoreAsync();

    private async Task InitializeCoreAsync()
    {
        await _model.LoadAsync();
        if (IsDisposed) return;
        _txtName.Text = _model.Name;
        _txtEmail.Text = _model.Email;
        _txtName.SelectAll();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ErrorReporter.Run(this, InitializeAsync);
    }

    internal async Task AcceptAsync()
    {
        _model.Name = _txtName.Text;
        _model.Email = _txtEmail.Text;
        if (await _model.SaveAsync() is { } issue)
        {
            _prompts.Warn(issue.Message, Text);
            FormKit.FocusField(this, issue.Field == nameof(PersonalInfoModel.Email) ? _txtEmail : _txtName);
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }
}
