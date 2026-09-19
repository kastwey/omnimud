using Omnimud.UI.Resources;

namespace Omnimud.UI.Forms;

/// <summary>Find dialog for the Received and Messages boxes (Ctrl+B).</summary>
public sealed partial class FrmFind : Form
{
    public FrmFind(string boxName, string? initialText, bool matchCase, bool searchUp)
    {
        InitializeComponent();

        Text = string.Format(Strings.Find_Title, boxName);
        _lblFind.Text = Strings.Find_Label;
        _txtFind.AccessibleName = Strings.Find_Name;
        _chkMatchCase.Text = Strings.Find_MatchCase;
        _grpDirection.Text = Strings.Find_Direction;
        _rbDown.Text = Strings.Find_Down;
        _rbUp.Text = Strings.Find_Up;
        _btnFind.Text = Strings.Find_Button;
        _btnCancel.Text = Strings.Find_Cancel;

        _txtFind.Text = initialText ?? string.Empty;
        _txtFind.SelectAll();
        _chkMatchCase.Checked = matchCase;
        _rbUp.Checked = searchUp;
        _rbDown.Checked = !searchUp;
        UpdateFindButton();
    }

    public string SearchText => _txtFind.Text;
    public bool MatchCase => _chkMatchCase.Checked;
    public bool SearchUp => _rbUp.Checked;

    private void TxtFind_TextChanged(object? sender, EventArgs e) => UpdateFindButton();

    // An empty search can never be submitted, so the dialog can always be closed.
    private void UpdateFindButton() => _btnFind.Enabled = _txtFind.TextLength > 0;
}
