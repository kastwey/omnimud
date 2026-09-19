using Omnimud.Core.Session;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;

namespace Omnimud.UI.Forms;

/// <summary>
/// Connection to a server that is not saved as a MUD. Empty the first time; afterwards it comes
/// filled with the last connection used. Returns a complete <see cref="SessionProfile"/>.
/// </summary>
public sealed class FrmQuickConnect : Form
{
    private readonly QuickConnectModel _model;
    private readonly IUserPrompts _prompts;

    private readonly TextBox _txtHost;
    private readonly TextBox _txtPort;
    private readonly CheckBox _chkTls;
    private readonly CheckBox _chkValidate;
    private readonly ComboBox _cboEncoding;
    private bool _loaded;

    public FrmQuickConnect() : this(new WinFormsUserPrompts())
    {
    }

    /// <param name="store">Where the last connection is remembered. Null = only while the process lives.</param>
    public FrmQuickConnect(IUserPrompts prompts, IQuickConnectStore? store = null)
    {
        _prompts = prompts;
        _model = new QuickConnectModel(store);

        const int labelX = 12, fieldX = 150, fieldW = 300, row = 34;
        FormKit.SetupDialog(this, nameof(FrmQuickConnect), Strings.QuickConnect_Title, new Size(470, 240));

        var tab = 0;
        var y = 12;

        var lblHost = FormKit.Label("_lblHost", Strings.QuickConnect_ServerLabel, labelX, y, tab++);
        _txtHost = FormKit.TextBox("_txtHost", lblHost, new Rectangle(fieldX, y, fieldW, 25), tab++);
        _txtHost.MaxLength = 255;
        y += row;

        var lblPort = FormKit.Label("_lblPort", Strings.QuickConnect_PortLabel, labelX, y, tab++);
        _txtPort = FormKit.TextBox("_txtPort", lblPort, new Rectangle(fieldX, y, 100, 25), tab++);
        _txtPort.MaxLength = 5;
        y += row;

        _chkTls = FormKit.CheckBox("_chkTls", Strings.QuickConnect_UseTls, fieldX, y, tab++);
        y += row - 4;
        _chkValidate = FormKit.CheckBox("_chkValidate", Strings.QuickConnect_ValidateCertificate, fieldX, y, tab++);
        _chkTls.CheckedChanged += (_, _) => _chkValidate.Enabled = _chkTls.Checked;
        y += row;

        var lblEncoding = FormKit.Label("_lblEncoding", Strings.QuickConnect_EncodingLabel, labelX, y, tab++);
        _cboEncoding = new ComboBox
        {
            Name = "_cboEncoding", AccessibleName = FormKit.NameFrom(lblEncoding.Text), Bounds = new Rectangle(fieldX, y, 200, 25),
            DropDownStyle = ComboBoxStyle.DropDown, TabIndex = tab++,
        };
        _cboEncoding.Items.AddRange([.. MudEncodings.Common]);
        _cboEncoding.Enter += (_, _) => _cboEncoding.SelectAll();
        y += row + 8;

        var btnOk = FormKit.Button("_btnOk", Strings.QuickConnect_ConnectButton, new Rectangle(fieldX + fieldW - 216, y, 104, 30), tab++,
            () => ErrorReporter.Run(this, AcceptAsync));
        var btnCancel = FormKit.Button("_btnCancel", Strings.Common_CancelButton, new Rectangle(fieldX + fieldW - 104, y, 104, 30), tab);
        btnCancel.DialogResult = DialogResult.Cancel;
        ClientSize = new Size(470, y + 42);

        Controls.AddRange([lblHost, _txtHost, lblPort, _txtPort, _chkTls, _chkValidate, lblEncoding, _cboEncoding, btnOk, btnCancel]);
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        ModelToControls();
    }

    /// <summary>The connection the user asked for. Null until the dialog is accepted.</summary>
    public SessionProfile? Profile { get; private set; }

    public string Host => Profile?.Host ?? string.Empty;
    public int Port => Profile?.Port ?? 0;
    public bool UseTls => Profile?.UseTls ?? false;
    public bool ValidateCertificate => Profile?.ValidateCertificate ?? true;
    public string MudEncoding => Profile?.Encoding ?? MudEncodings.Default;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ErrorReporter.Run(this, LoadAsync);
    }

    /// <summary>Brings back the last connection used, with the host selected so typing replaces it.</summary>
    internal async Task LoadAsync()
    {
        if (_loaded) return;
        _loaded = true;
        await _model.LoadAsync();
        ModelToControls();
    }

    private void ModelToControls()
    {
        _txtHost.Text = _model.Host;
        _txtPort.Text = _model.Port;
        _chkTls.Checked = _model.UseTls;
        _chkValidate.Checked = _model.ValidateCertificate;
        _chkValidate.Enabled = _model.CanValidateCertificate;
        _cboEncoding.Text = _model.Encoding;
        _txtPort.SelectAll();
        FormKit.FocusField(this, _txtHost);
    }

    internal async Task<bool> AcceptAsync()
    {
        _model.Host = _txtHost.Text;
        _model.Port = _txtPort.Text;
        _model.UseTls = _chkTls.Checked;
        _model.ValidateCertificate = _chkValidate.Checked;
        _model.Encoding = _cboEncoding.Text;

        var (profile, error) = await _model.AcceptAsync();
        if (error is not null)
        {
            _prompts.Warn(error.Message, Text);
            FormKit.FocusField(this, error.Field switch
            {
                QuickConnectField.Port => _txtPort,
                QuickConnectField.Encoding => _cboEncoding,
                _ => _txtHost,
            });
            return false;
        }

        Profile = profile;
        DialogResult = DialogResult.OK;
        return true;
    }
}
