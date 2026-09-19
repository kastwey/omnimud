namespace Omnimud.UI.Controls;

/// <summary>
/// A multiline text box for LONG text the user only reads (release notes, error details). It is deliberately
/// NOT declared read-only: NVDA reads the whole value of a read-only edit box when it gets the focus, which for
/// a long text is unbearable; a normal edit box is announced with its name and the line of the caret, and is
/// read with the arrow keys. Instead the box protects itself: typing, deleting, pasting, cutting and undoing do
/// nothing. Selecting and copying work as usual. (Same approach as the Received box of the game window.)
/// </summary>
public class ProtectedTextBox : TextBox
{
    private const int WmKeyDown = 0x0100;
    private const int WmChar = 0x0102;
    private const int WmImeChar = 0x0286;
    private const int WmImeComposition = 0x010F;
    private const int WmCut = 0x0300;
    private const int WmPaste = 0x0302;
    private const int WmClear = 0x0303;
    private const int WmUndo = 0x0304;
    private const int EmUndo = 0x00C7;
    private const int EmReplaceSel = 0x00C2;

    private bool _settingText;

    public ProtectedTextBox()
    {
        Multiline = true;
        ScrollBars = ScrollBars.Vertical;
        WordWrap = true;
        AcceptsReturn = false; // Enter reaches the default button of the dialog
        AcceptsTab = false;
        HideSelection = false;
    }

    /// <summary>The only way in: sets the text and leaves the caret at the beginning.</summary>
    public void SetProtectedText(string? text)
    {
        _settingText = true;
        try
        {
            Text = (text ?? string.Empty).ReplaceLineEndings(Environment.NewLine);
            Select(0, 0);
        }
        finally
        {
            _settingText = false;
        }
    }

    /// <summary>Adds text at the end without moving the caret nor the selection.</summary>
    public void AppendProtectedText(string text)
    {
        var (start, length) = (SelectionStart, SelectionLength);
        _settingText = true;
        try
        {
            Text += text.ReplaceLineEndings(Environment.NewLine);
            Select(start, length);
        }
        finally
        {
            _settingText = false;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (!_settingText && IsEdit(ref m)) return;
        base.WndProc(ref m);
    }

    private static bool IsEdit(ref Message m)
    {
        switch (m.Msg)
        {
            case WmCut or WmPaste or WmClear or WmUndo or EmUndo or EmReplaceSel or WmImeChar or WmImeComposition:
                return true;
            case WmChar:
                // Ctrl+C (3) and Ctrl+A (1) arrive as characters and are harmless; everything else would edit.
                var c = (int)m.WParam;
                return c is not (1 or 3);
            case WmKeyDown:
                var key = (Keys)(int)m.WParam;
                return key is Keys.Delete or Keys.Back;
            default:
                return false;
        }
    }

    /// <summary>For tests: what the control does with a window message, without a real key press.</summary>
    internal void SimulateMessage(int msg, nint wParam = 0)
    {
        var message = Message.Create(Handle, msg, wParam, 0);
        WndProc(ref message);
    }
}
