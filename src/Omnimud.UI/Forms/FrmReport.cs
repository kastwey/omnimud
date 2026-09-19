using Omnimud.Core.Reports;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;

namespace Omnimud.UI.Forms;

/// <summary>
/// Error report or suggestion. The user writes a description and sees, in an editable box, the exact text that will
/// go out. Nothing is sent from here: the buttons open the browser (a new GitHub issue, filled in) or the mail
/// program (a message to the author, filled in), or copy the text. All the logic is in <see cref="ReportPresenter"/>.
/// </summary>
public sealed class FrmReport : Form
{
    private static readonly ReportKind[] Kinds = [ReportKind.Error, ReportKind.Suggestion];

    private readonly ReportPresenter _presenter;
    private readonly Action<IWin32Window>? _editPersonalInfo;
    private readonly ComboBox _cboKind;
    private readonly TextBox _txtDescription;
    private readonly CheckBox _chkDiagnostics;
    private readonly TextBox _txtTitle;
    private readonly TextBox _txtPreview;
    private bool _binding;

    /// <param name="editPersonalInfo">Opens the personal information dialog; null hides the button.</param>
    public FrmReport(ReportPresenter presenter, Action<IWin32Window>? editPersonalInfo = null)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        _presenter = presenter;
        _editPersonalInfo = editPersonalInfo;

        const int x = 12, w = 676;
        FormKit.SetupDialog(this, nameof(FrmReport), presenter.DialogTitle, new Size(700, 640));
        var tab = 0;

        var lblPrivacy = FormKit.Hint("_lblPrivacy", Strings.ReportDlg_Privacy, new Rectangle(x, 10, w, 62), tab++);

        var lblKind = FormKit.Label("_lblKind", Strings.ReportDlg_KindLabel, x, 78, tab++);
        _cboKind = new ComboBox
        {
            Name = "_cboKind", AccessibleName = FormKit.NameFrom(lblKind.Text), Bounds = new Rectangle(200, 78, 220, 25),
            DropDownStyle = ComboBoxStyle.DropDownList, TabIndex = tab++,
            // An unexpected error is always an error report.
            Enabled = !presenter.HasException,
        };
        _cboKind.Items.AddRange([Strings.ReportDlg_KindError, Strings.ReportDlg_KindSuggestion]);

        var lblDescription = FormKit.Label("_lblDescription", Strings.ReportDlg_DescriptionLabel, x, 112, tab++);
        _txtDescription = new TextBox
        {
            Name = "_txtDescription", AccessibleName = FormKit.NameFrom(lblDescription.Text), Bounds = new Rectangle(x, 138, w, 120), TabIndex = tab++,
            Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, MaxLength = 20_000,
        };

        _chkDiagnostics = FormKit.CheckBox("_chkDiagnostics", Strings.ReportDlg_IncludeDiagnostics, x, 266, tab++);

        var lblTitle = FormKit.Label("_lblTitle", Strings.ReportDlg_TitleLabel, x, 298, tab++);
        _txtTitle = FormKit.TextBox("_txtTitle", lblTitle, new Rectangle(200, 298, w - 188, 25), tab++);
        _txtTitle.MaxLength = 200;

        var lblPreview = FormKit.Label("_lblPreview", Strings.ReportDlg_PreviewLabel, x, 332, tab++);
        _txtPreview = new TextBox
        {
            Name = "_txtPreview", AccessibleName = FormKit.NameFrom(lblPreview.Text), Bounds = new Rectangle(x, 358, w, 190), TabIndex = tab++,
            Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Both, WordWrap = false, MaxLength = 100_000,
        };

        var btnGitHub = FormKit.Button("_btnGitHub", Strings.ReportDlg_OpenGitHub, new Rectangle(x, 560, 160, 32), tab++,
            () => Finish(_presenter.OpenInGitHub()));
        var btnEmail = FormKit.Button("_btnEmail", Strings.ReportDlg_SendEmail, new Rectangle(x + 168, 560, 160, 32), tab++,
            () => ErrorReporter.Run(this, async () => Finish(await _presenter.SendByEmailAsync())));
        var btnCopy = FormKit.Button("_btnCopy", Strings.ReportDlg_Copy, new Rectangle(x + 336, 560, 190, 32), tab++,
            () => Finish(_presenter.CopyToClipboard()));
        var btnPersonal = FormKit.Button("_btnPersonalInfo", Strings.ReportDlg_PersonalInfo, new Rectangle(x, 598, 220, 32), tab++,
            () => _editPersonalInfo?.Invoke(this));
        btnPersonal.Visible = btnPersonal.Enabled = editPersonalInfo is not null;
        var btnCancel = FormKit.Button("_btnCancel", Strings.Common_CancelButton, new Rectangle(x + w - 110, 598, 110, 32), tab);
        btnCancel.DialogResult = DialogResult.Cancel;

        Controls.AddRange([lblPrivacy, lblKind, _cboKind, lblDescription, _txtDescription, _chkDiagnostics, lblTitle, _txtTitle,
            lblPreview, _txtPreview, btnGitHub, btnEmail, btnCopy, btnPersonal, btnCancel]);
        AcceptButton = btnGitHub;
        CancelButton = btnCancel;
        ActiveControl = _txtDescription;

        ModelToControls();
        _presenter.PreviewChanged += ShowPreview;
        _cboKind.SelectedIndexChanged += (_, _) => FromControls(() => _presenter.Kind = Kinds[Math.Max(_cboKind.SelectedIndex, 0)]);
        _txtDescription.TextChanged += (_, _) => FromControls(() => _presenter.Description = _txtDescription.Text);
        _chkDiagnostics.CheckedChanged += (_, _) => FromControls(() => _presenter.IncludeDiagnostics = _chkDiagnostics.Checked);
        _txtTitle.TextChanged += (_, _) => FromControls(() => _presenter.Title = _txtTitle.Text);
        _txtPreview.TextChanged += (_, _) => FromControls(() => _presenter.Preview = _txtPreview.Text);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _presenter.PreviewChanged -= ShowPreview;
        base.Dispose(disposing);
    }

    private void ModelToControls()
    {
        _binding = true;
        try
        {
            _cboKind.SelectedIndex = Array.IndexOf(Kinds, _presenter.Kind);
            _txtDescription.Text = _presenter.Description;
            _chkDiagnostics.Checked = _presenter.IncludeDiagnostics;
        }
        finally
        {
            _binding = false;
        }
        ShowPreview();
    }

    private void ShowPreview()
    {
        _binding = true;
        try
        {
            Text = _presenter.DialogTitle;
            _txtTitle.Text = _presenter.Title;
            _txtPreview.Text = _presenter.Preview.ReplaceLineEndings(Environment.NewLine);
            _txtPreview.Select(0, 0);
        }
        finally
        {
            _binding = false;
        }
    }

    private void FromControls(Action update)
    {
        if (!_binding) update();
    }

    private void Finish(ReportActionResult result)
    {
        if (result.Close)
        {
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        Control? target = result.Focus switch
        {
            ReportField.Description => _txtDescription,
            ReportField.Title => _txtTitle,
            ReportField.Preview => _txtPreview,
            _ => null
        };
        if (target is null) return;
        ActiveControl = target;
        if (target.CanFocus) target.Focus();
    }
}
