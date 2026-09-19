using Omnimud.Core.Session;
using Omnimud.UI.Controls;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;
using Omnimud.UI.Services.Accessibility;

namespace Omnimud.UI.Forms;

/// <summary>
/// Shown by <see cref="ErrorReporter"/> when something nobody expected fails. The application carries on.
/// Buttons: report this error (opens the report dialog with the exception), copy the details, continue.
/// The initial focus is on Continue; the details are in a <see cref="ProtectedTextBox"/> (see there why it is not
/// a read-only box). While it is open, further errors are appended to the details instead of opening more dialogs.
/// </summary>
public sealed class FrmUnexpectedError : Form, IUnexpectedErrorView
{
    private readonly ProtectedTextBox _txtDetails;
    private readonly Button _btnContinue;
    private readonly Label _lblMessage;
    private readonly IClipboardService _clipboard;
    private readonly IAnnouncer _announcer;
    private int _errors;

    public FrmUnexpectedError(bool canReport = true, IClipboardService? clipboard = null, IAnnouncer? announcer = null)
    {
        _clipboard = clipboard ?? new WinFormsClipboard();
        FormKit.SetupDialog(this, nameof(FrmUnexpectedError), Strings.UnexpectedError_Title, new Size(600, 400));
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true; // it may appear when no other window of Omnimud is visible

        _lblMessage = FormKit.Hint("_lblMessage", Strings.UnexpectedError_Message, new Rectangle(12, 12, 576, 62), 0);

        var lblDetails = FormKit.Label("_lblDetails", Strings.UnexpectedError_DetailsLabel, 12, 78, 1);
        _txtDetails = new ProtectedTextBox
        {
            Name = "_txtDetails", AccessibleName = FormKit.NameFrom(lblDetails.Text), Bounds = new Rectangle(12, 104, 576, 236), TabIndex = 2,
            WordWrap = false, ScrollBars = ScrollBars.Both,
        };

        var btnReport = FormKit.Button("_btnReport", Strings.UnexpectedError_Report, new Rectangle(12, 354, 230, 32), 3);
        btnReport.DialogResult = DialogResult.Yes;
        btnReport.Visible = btnReport.Enabled = canReport;
        var btnCopy = FormKit.Button("_btnCopy", Strings.UnexpectedError_Copy, new Rectangle(250, 354, 170, 32), 4, CopyDetails);
        _btnContinue = FormKit.Button("_btnContinue", Strings.UnexpectedError_Continue, new Rectangle(428, 354, 160, 32), 5);
        _btnContinue.DialogResult = DialogResult.Cancel;

        Controls.AddRange([_lblMessage, lblDetails, _txtDetails, btnReport, btnCopy, _btnContinue]);
        AcceptButton = _btnContinue;
        CancelButton = _btnContinue;
        ActiveControl = _btnContinue;
        _announcer = announcer ?? new Announcer(new ControlUiaNotifier(this));
    }

    public string Details => _txtDetails.Text;

    public void AddError(string details)
    {
        _errors++;
        if (_errors == 1)
        {
            _txtDetails.SetProtectedText(details);
            return;
        }

        _txtDetails.AppendProtectedText(Environment.NewLine + Environment.NewLine + string.Format(Strings.UnexpectedError_Another, _errors)
                                        + Environment.NewLine + details);
        _lblMessage.Text = string.Format(Strings.UnexpectedError_MessageMany, _errors);
    }

    public UnexpectedErrorChoice ShowModal()
    {
        var owner = ActiveForm is { IsDisposed: false, Visible: true } active && !ReferenceEquals(active, this) ? active : null;
        return ShowDialog(owner) == DialogResult.Yes ? UnexpectedErrorChoice.Report : UnexpectedErrorChoice.Continue;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _btnContinue.Select();
    }

    internal void CopyDetails()
    {
        var copied = _clipboard.SetText(Details);
        _announcer.Announce(copied ? Strings.UnexpectedError_Copied : Strings.UnexpectedError_NotCopied, AnnouncePriority.Interrupt);
    }
}
