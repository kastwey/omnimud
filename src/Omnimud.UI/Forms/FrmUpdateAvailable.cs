using Omnimud.Core.Updates;
using Omnimud.UI.Controls;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Forms;

/// <summary>
/// "There is a new version". Nothing is downloaded from here: the only action is opening the page of the release
/// in the browser (DialogResult.OK), and the button only exists when the checker trusted that page.
/// <para>Screen readers: the window title carries the version, so it is heard when the dialog opens. The initial
/// focus is on the first button, not on the notes (they can be long); the notes are one Shift+Tab away, in a
/// <see cref="ProtectedTextBox"/> — a normal edit box that refuses edits, because NVDA reads the whole value of a
/// read-only box when it gets the focus.</para>
/// </summary>
public sealed class FrmUpdateAvailable : Form
{
    private readonly Button _btnOpen;
    private readonly Button _btnClose;

    public FrmUpdateAvailable(UpdateAvailable update, string currentVersion)
    {
        ArgumentNullException.ThrowIfNull(update);
        FormKit.SetupDialog(this, nameof(FrmUpdateAvailable), string.Format(Strings.Update_DialogTitle, update.Version), new Size(560, 420));

        var summary = string.Format(Strings.Update_Summary, update.Version, currentVersion, update.Title);
        var lblSummary = FormKit.Hint("_lblSummary", summary, new Rectangle(12, 12, 536, 44), 0);

        var lblNotes = FormKit.Label("_lblNotes", Strings.Update_NotesLabel, 12, 60, 1);
        var txtNotes = new ProtectedTextBox
        {
            Name = "_txtNotes", AccessibleName = FormKit.NameFrom(lblNotes.Text), Bounds = new Rectangle(12, 86, 536, 270), TabIndex = 2,
        };
        // The summary opens the box too: with a screen reader everything is read in one place, with the arrow keys.
        txtNotes.SetProtectedText(summary + Environment.NewLine + Environment.NewLine + (update.Notes.Length == 0 ? Strings.Update_NoNotes : update.Notes));

        var canOpen = OffersDownloadPage = update.ReleasePage is not null;
        _btnOpen = FormKit.Button("_btnOpen", Strings.Update_OpenPage, new Rectangle(232, 372, 210, 32), 3);
        _btnOpen.DialogResult = DialogResult.OK;
        _btnOpen.Visible = _btnOpen.Enabled = canOpen;
        _btnClose = FormKit.Button("_btnClose", Strings.Update_Close, new Rectangle(448, 372, 100, 32), 4);
        _btnClose.DialogResult = DialogResult.Cancel;

        Controls.AddRange([lblSummary, lblNotes, txtNotes, _btnOpen, _btnClose]);
        AcceptButton = canOpen ? _btnOpen : _btnClose;
        CancelButton = _btnClose;
        ActiveControl = canOpen ? _btnOpen : _btnClose;
    }

    /// <summary>True when the dialog offers to open the page of the release.</summary>
    internal bool OffersDownloadPage { get; }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        (OffersDownloadPage ? _btnOpen : _btnClose).Select();
    }
}
