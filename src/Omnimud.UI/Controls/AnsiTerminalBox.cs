using System.ComponentModel;
using System.Runtime.InteropServices;
using Omnimud.Core.Options;
using Omnimud.Core.Text;

namespace Omnimud.UI.Controls;

/// <summary>
/// Protected rich text box for MUD output and for the Messages box: the user can move around,
/// select and copy, but never change the text.
///
/// It is a plain RichEdit so screen readers can review it with the caret like any other text.
/// The key requirement is that incoming text must NOT move the user's caret or selection
/// while they are reading (see <see cref="CursorBehavior"/>). To achieve that, text is
/// appended through the Text Object Model (ITextDocument) with a range at the end of the
/// document, which never touches the selection. If TOM is not available it falls back to
/// select-append-restore.
/// </summary>
public sealed class AnsiTerminalBox : RichTextBox
{
    private const int WM_USER = 0x0400;
    private const int WM_SETREDRAW = 0x000B;
    private const int WM_GETOBJECT = 0x003D;
    private const int WM_CONTEXTMENU = 0x007B;
    private const int UiaRootObjectId = -25;
    private const int WM_CHAR = 0x0102;
    private const int WM_IME_CHAR = 0x0286;
    private const int WM_CUT = 0x0300;
    private const int WM_PASTE = 0x0302;
    private const int WM_CLEAR = 0x0303;
    private const int WM_UNDO = 0x0304;
    private const int EM_UNDO = 0x00C7;
    private const int EM_REDO = WM_USER + 84;
    private const int WM_VSCROLL = 0x0115;
    private const int SB_BOTTOM = 7;
    private const int EM_GETOLEINTERFACE = WM_USER + 60;
    private const int EM_GETSCROLLPOS = WM_USER + 221;
    private const int EM_SETSCROLLPOS = WM_USER + 222;
    private const int TomTrue = -1;
    private const int TomFalse = 0;
    private const int TomSuspend = -9999995;
    private const int TomResume = -9999994;

    private static readonly Guid IidTextDocument = new("8CC497C0-A1DF-11CE-8098-00AA0047BE5D");

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, out IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref Point lParam);

    // Length in characters of each line currently in the box, newline included.
    private readonly LinkedList<int> _lineLengths = new();
    private object? _textDocument;
    private bool _tomUnavailable;
    // True while the box itself edits the text (append, trim, clear): only then edits get through.
    private bool _internalEdit;
    private int _maxLines = 10_000;

    public AnsiTerminalBox()
    {
        // Deliberately NOT ReadOnly. NVDA does not treat a read-only edit box as editable text, so on
        // focus it speaks the MSAA value - the whole content - instead of the line under the caret, which
        // also drowns the label. The box protects itself instead (see OnKeyDown, OnKeyPress and WndProc).
        ReadOnly = false;
        DetectUrls = true;
        HideSelection = false;
        ShortcutsEnabled = true;
        WordWrap = true;
        ScrollBars = RichTextBoxScrollBars.ForcedVertical;
        BorderStyle = BorderStyle.FixedSingle;
        ApplyTheme();
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public CursorBehavior CursorBehavior { get; set; } = CursorBehavior.FollowIfAtEnd;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int MaxLines
    {
        get => _maxLines;
        set => _maxLines = Math.Max(100, value);
    }

    /// <summary>Forces the select-append-restore path. For tests.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool DisableTextObjectModel
    {
        get => _tomUnavailable;
        set { _tomUnavailable = value; if (value) ReleaseTextDocument(); }
    }

    public int LineCount => _lineLengths.Count;

    public bool CaretAtEnd => SelectionLength == 0 && SelectionStart >= TextLength;

    /// <summary>Appends one line. Must be called on the UI thread.</summary>
    public void AppendLine(IReadOnlyList<StyledSegment> segments)
    {
        if (IsDisposed) return;
        if (!IsHandleCreated) CreateHandle();

        var prependNewline = _lineLengths.Count > 0;
        var length = (prependNewline ? 1 : 0);
        foreach (var s in segments) length += s.Text.Length;

        var selStart = SelectionStart;
        var selLength = SelectionLength;
        var follow = CursorBehavior switch
        {
            CursorBehavior.GoToEnd => true,
            CursorBehavior.Keep => false,
            _ => selLength == 0 && selStart >= TextLength,
        };

        var scrollPos = Point.Empty;
        if (!follow) SendMessage(Handle, EM_GETSCROLLPOS, IntPtr.Zero, ref scrollPos);

        SendMessage(Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        _internalEdit = true;
        try
        {
            if (!TryAppendWithTom(segments, prependNewline))
                AppendWithSelection(segments, prependNewline);

            _lineLengths.AddLast(length);
            var removed = TrimIfNeeded();

            if (follow)
            {
                Select(TextLength, 0);
            }
            else
            {
                var start = Math.Max(0, selStart - removed);
                var end = Math.Max(0, selStart + selLength - removed);
                if (SelectionStart != start || SelectionLength != end - start)
                    Select(start, end - start);
            }
        }
        finally
        {
            _internalEdit = false;
            SendMessage(Handle, WM_SETREDRAW, 1, IntPtr.Zero);
        }

        if (follow)
            SendMessage(Handle, WM_VSCROLL, SB_BOTTOM, IntPtr.Zero);
        else
            SendMessage(Handle, EM_SETSCROLLPOS, IntPtr.Zero, ref scrollPos);

        Invalidate();
    }

    public void AppendPlainLine(string text) => AppendLine([new StyledSegment(text, AnsiStyle.Default)]);

    public new void Clear()
    {
        _internalEdit = true;
        try { base.Clear(); }
        finally { _internalEdit = false; }
        _lineLengths.Clear();
    }

    public void MoveCaretToEnd()
    {
        Select(TextLength, 0);
        if (IsHandleCreated) SendMessage(Handle, WM_VSCROLL, SB_BOTTOM, IntPtr.Zero);
    }

    /// <summary>The run of non-blank characters under the caret (for opening URLs with Enter).</summary>
    public string GetWordAtCaret()
    {
        var text = Text;
        if (text.Length == 0) return string.Empty;
        var pos = Math.Clamp(SelectionStart, 0, text.Length);
        static bool IsBreak(char c) => char.IsWhiteSpace(c) || c is '\'' or '"' or '<' or '>';

        var start = pos;
        while (start > 0 && !IsBreak(text[start - 1])) start--;
        var end = pos;
        while (end < text.Length && !IsBreak(text[end])) end++;
        return text[start..end].TrimEnd('.', ',', ';', ':', ')', ']', '!', '?');
    }

    /// <summary>Client position of the right click, or null when the menu was asked from the keyboard
    /// (Applications key, Shift+F10). While nobody listens, WinForms handles the context menu as usual.</summary>
    public event Action<Point?>? ContextMenuRequested;

    internal void RequestContextMenu(Point? clientLocation) => ContextMenuRequested?.Invoke(clientLocation);

    /// <summary>Where a menu asked from the keyboard opens: just under the caret, always inside the box
    /// (the caret may be scrolled out of view).</summary>
    public Point KeyboardMenuLocation()
    {
        var client = ClientRectangle;
        if (!IsHandleCreated || client.Width <= 0 || client.Height <= 0) return Point.Empty;

        var caret = GetPositionFromCharIndex(Math.Clamp(SelectionStart, 0, TextLength));
        return new Point(
            Math.Clamp(caret.X, client.Left, client.Right - 1),
            Math.Clamp(caret.Y + Font.Height, client.Top, client.Bottom - 1));
    }

    public void ApplyTheme()
    {
        if (SystemInformation.HighContrast)
        {
            BackColor = SystemColors.Window;
            ForeColor = SystemColors.WindowText;
        }
        else
        {
            BackColor = Color.Black;
            ForeColor = Color.LightGray;
        }
    }

    protected override void WndProc(ref Message m)
    {
        // The native RichEdit answers UI Automation itself and presents the box as a "Document"
        // whose value is the whole text and whose name ignores AccessibleName. Screen readers then
        // skip the label and read everything when the box gets the focus. Declining the UIA root
        // request makes clients fall back to the MSAA view WinForms provides: a named edit box that
        // is reviewed line by line, exactly like the original client.
        if (m.Msg == WM_GETOBJECT && (int)(long)m.LParam == UiaRootObjectId)
        {
            m.Result = IntPtr.Zero;
            return;
        }
        // The box asks for its context menu itself. WinForms would put a menu asked from the keyboard
        // (Applications key, Shift+F10: no position) in the middle of the box; next to the caret is
        // where a screen reader user is reading.
        if (m.Msg == WM_CONTEXTMENU && ContextMenuRequested is not null)
        {
            var lParam = m.LParam.ToInt64();
            if ((int)lParam == -1)
            {
                RequestContextMenu(null);
            }
            else
            {
                var screen = new Point(unchecked((short)(lParam & 0xFFFF)), unchecked((short)((lParam >> 16) & 0xFFFF)));
                RequestContextMenu(PointToClient(screen));
            }
            return;
        }
        // Whatever route an edit takes (typing, IME, clipboard, undo), it ends here.
        if (!_internalEdit && m.Msg is WM_CHAR or WM_IME_CHAR or WM_CUT or WM_PASTE or WM_CLEAR or WM_UNDO or EM_UNDO or EM_REDO)
        {
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
    }

    // WinForms' RichTextBox.OnGotFocus deliberately raises a UI Automation notification carrying the
    // control's whole Text (meant to give Narrator something to read). With a screen of MUD output that
    // means the entire buffer is spoken every time the box gets the focus, right after the normal
    // "name, role, current line" announcement. There is no switch for it, so while the base focus
    // handling runs the box reports an empty Text: an empty notification is ignored by screen readers.
    private bool _hideTextFromFocusNotification;

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => _hideTextFromFocusNotification ? string.Empty : base.Text;
        set => base.Text = value;
    }

    protected override void OnGotFocus(EventArgs e)
    {
        _hideTextFromFocusNotification = true;
        try
        {
            base.OnGotFocus(e);
        }
        finally
        {
            _hideTextFromFocusNotification = false;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // Handled (without suppressing the key press) stops the rich edit from acting on Delete, Backspace,
        // Ctrl+V, Ctrl+X, Ctrl+Z, formatting shortcuts... while KeyPress still fires, so the window can
        // forward typed characters to the input box.
        if (!IsNavigationOrCopy(e)) e.Handled = true;
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        e.Handled = true;
    }

    private static bool IsNavigationOrCopy(KeyEventArgs e) => e.KeyCode switch
    {
        Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown => true,
        Keys.ShiftKey or Keys.ControlKey or Keys.Menu or Keys.Apps or Keys.Tab or Keys.Escape => true,
        Keys.C or Keys.Insert or Keys.A => e.Control && !e.Alt && !e.Shift,
        >= Keys.F1 and <= Keys.F24 => true,
        _ => false,
    };

    protected override void OnSystemColorsChanged(EventArgs e)
    {
        base.OnSystemColorsChanged(e);
        ApplyTheme();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        ReleaseTextDocument();
        base.OnHandleDestroyed(e);
    }

    // ── Text Object Model path ─────────────────────────────────────────────

    private bool TryAppendWithTom(IReadOnlyList<StyledSegment> segments, bool prependNewline)
    {
        if (_tomUnavailable) return false;
        try
        {
            dynamic? doc = GetTextDocument();
            if (doc is null) return false;

            doc.Undo(TomSuspend);
            // WM_SETREDRAW does not stop the Text Object Model from laying the text out and notifying on every
            // insertion; Freeze does (measured: about 10 ms per line with the window visible, 2 ms frozen).
            doc.Freeze();
            try
            {
                dynamic probe = doc.Range(0, 0);
                // The story always ends with a final paragraph mark that cannot be passed.
                int end = (int)probe.StoryLength - 1;

                if (prependNewline)
                    end = InsertWithTom(doc, end, "\r", AnsiStyle.Default);
                foreach (var segment in segments)
                {
                    if (segment.Text.Length == 0) continue;
                    end = InsertWithTom(doc, end, segment.Text, segment.Style);
                }
            }
            finally
            {
                doc.Unfreeze();
                doc.Undo(TomResume);
            }
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            _tomUnavailable = true;
            ReleaseTextDocument();
            return false;
        }
    }

    private int InsertWithTom(dynamic doc, int position, string text, AnsiStyle style)
    {
        var (fore, back, fontStyle) = AnsiPalette.Resolve(style, ForeColor, BackColor, SystemInformation.HighContrast);
        dynamic range = doc.Range(position, position);
        range.Text = text;
        dynamic font = range.Font;
        font.ForeColor = ColorTranslator.ToWin32(fore);
        font.BackColor = ColorTranslator.ToWin32(back);
        font.Bold = fontStyle.HasFlag(FontStyle.Bold) ? TomTrue : TomFalse;
        font.Italic = fontStyle.HasFlag(FontStyle.Italic) ? TomTrue : TomFalse;
        font.Underline = fontStyle.HasFlag(FontStyle.Underline) ? 1 : 0;
        font.StrikeThrough = fontStyle.HasFlag(FontStyle.Strikeout) ? TomTrue : TomFalse;
        return (int)range.End;
    }

    private object? GetTextDocument()
    {
        if (_textDocument is not null) return _textDocument;

        SendMessage(Handle, EM_GETOLEINTERFACE, IntPtr.Zero, out var unknown);
        if (unknown == IntPtr.Zero) return null;
        try
        {
            var iid = IidTextDocument;
            if (Marshal.QueryInterface(unknown, in iid, out var docPtr) != 0 || docPtr == IntPtr.Zero)
                return null;
            try
            {
                _textDocument = Marshal.GetObjectForIUnknown(docPtr);
            }
            finally
            {
                Marshal.Release(docPtr);
            }
        }
        finally
        {
            Marshal.Release(unknown);
        }
        return _textDocument;
    }

    private void ReleaseTextDocument()
    {
        if (_textDocument is not null && Marshal.IsComObject(_textDocument))
            Marshal.ReleaseComObject(_textDocument);
        _textDocument = null;
    }

    // ── Fallback path ──────────────────────────────────────────────────────

    private void AppendWithSelection(IReadOnlyList<StyledSegment> segments, bool prependNewline)
    {
        if (prependNewline)
            InsertWithSelection("\n", AnsiStyle.Default);
        foreach (var segment in segments)
        {
            if (segment.Text.Length == 0) continue;
            InsertWithSelection(segment.Text, segment.Style);
        }
    }

    private void InsertWithSelection(string text, AnsiStyle style)
    {
        var (fore, back, fontStyle) = AnsiPalette.Resolve(style, ForeColor, BackColor, SystemInformation.HighContrast);
        Select(TextLength, 0);
        SelectionColor = fore;
        SelectionBackColor = back;
        if (fontStyle == FontStyle.Regular)
        {
            SelectionFont = Font;
            SelectedText = text;
        }
        else
        {
            using var font = new Font(Font, fontStyle);
            SelectionFont = font;
            SelectedText = text;
        }
    }

    // ── Trimming ───────────────────────────────────────────────────────────

    /// <summary>Removes the oldest lines in batches. Returns how many characters were removed.</summary>
    private int TrimIfNeeded()
    {
        var slack = Math.Max(50, _maxLines / 10);
        if (_lineLengths.Count <= _maxLines + slack) return 0;

        var chars = 0;
        while (_lineLengths.Count > _maxLines)
        {
            chars += _lineLengths.First!.Value;
            _lineLengths.RemoveFirst();
        }

        // Every line but the first was stored with the newline that precedes it. The line that
        // becomes first loses that newline too.
        if (_lineLengths.First is { } first)
        {
            chars += 1;
            first.Value -= 1;
        }

        Select(0, chars);
        SelectedText = string.Empty;
        return chars;
    }
}
