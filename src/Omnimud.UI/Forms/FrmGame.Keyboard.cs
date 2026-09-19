using Omnimud.Core.Session;
using Omnimud.UI.Controls;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;

namespace Omnimud.UI.Forms;

// Keyboard behaviour that is not a menu shortcut.
partial class FrmGame
{
    // ── Whole window ───────────────────────────────────────────────────────

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
        HandleReviewKey(keyData) || base.ProcessCmdKey(ref msg, keyData);

    /// <summary>Message review keys (Ctrl+digit, Ctrl+º). Returns true when the key was consumed.</summary>
    internal bool HandleReviewKey(Keys keyData)
    {
        // Exactly Ctrl + key: AltGr arrives as Ctrl+Alt and must keep typing characters.
        if ((keyData & Keys.Modifiers) == Keys.Control)
        {
            var key = keyData & Keys.KeyCode;
            if (DigitOf(key) is { } digit)
            {
                Speak(_reviewer.PressDigit(digit, _session.Messages));
                return true;
            }
            if (key == Keys.Oem5)
            {
                Speak(_reviewer.PressSequential(_session.Messages));
                return true;
            }
        }
        else if (keyData == Keys.Escape)
        {
            _reviewer.Cancel();
        }

        return false;
    }

    private void Speak(string? text)
    {
        if (text is null) System.Media.SystemSounds.Beep.Play();
        else _announcer.Announce(text, AnnouncePriority.Interrupt);
    }

    private static int? DigitOf(Keys key) => key switch
    {
        >= Keys.D0 and <= Keys.D9 => key - Keys.D0,
        >= Keys.NumPad0 and <= Keys.NumPad9 => key - Keys.NumPad0,
        _ => null,
    };

    // ── Text to send ───────────────────────────────────────────────────────

    private void TxtInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (HandleInputKey(e.KeyData)) e.SuppressKeyPress = true;
    }

    /// <summary>A key pressed in the text to send. Returns true when the key was consumed (the box
    /// must not see it); false leaves the key its normal behaviour.</summary>
    internal bool HandleInputKey(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        var modifiers = keyData & Keys.Modifiers;

        // Movement mode (F2): the numeric keypad always; arrows, Page Up/Down, Home and End only on an
        // empty box, so with text they still move the cursor. A key without command is not swallowed.
        if (MovementKeyMap.Resolve(keyData, _session, _txtInput.TextLength == 0) is { } movement)
        {
            Run(() => _session.ExecuteMovementAsync((int)movement));
            return true;
        }

        switch (key)
        {
            case Keys.Enter when modifiers == Keys.None:
                SubmitInput();
                return true;

            case Keys.Enter when modifiers is Keys.Shift or Keys.Control:
                if (_txtInput.TextLength == 0 || _session.PasswordMode)
                    Run(_session.SendBlankLineAsync);
                else
                    _txtInput.SelectedText = Environment.NewLine;
                return true;

            case Keys.Up when modifiers == Keys.None:
            case Keys.Down when modifiers == Keys.None:
                // Inside multi-line text the arrows move between its lines (the screen reader reads
                // them); at the first or last line they browse the command history.
                if (CanMoveWithinInput(key == Keys.Up)) return false;
                BrowseHistory(key == Keys.Up ? -1 : +1);
                return true;

            case Keys.Up when modifiers == Keys.Control:
            case Keys.Down when modifiers == Keys.Control:
                // Always the history: the way to reach it while the plain arrows are movement keys.
                BrowseHistory(key == Keys.Up ? -1 : +1);
                return true;

            case Keys.Escape when modifiers == Keys.None:
                SetInputText(string.Empty);
                _historyIndex = -1;
                return true;
        }

        return false;
    }

    internal void SubmitInput()
    {
        var text = _txtInput.Text;
        SetInputText(string.Empty);
        _historyIndex = -1;
        Run(() => _session.SubmitInputAsync(text));
    }

    private bool CanMoveWithinInput(bool up)
    {
        if (!_txtInput.Multiline || _txtInput.Lines.Length < 2) return false;
        var line = _txtInput.GetLineFromCharIndex(_txtInput.SelectionStart);
        var last = _txtInput.GetLineFromCharIndex(_txtInput.TextLength);
        return up ? line > 0 : line < last;
    }

    /// <summary>History is oldest first. Index -1 means "not browsing" (the empty line after the newest).</summary>
    internal void BrowseHistory(int direction)
    {
        if (_session.PasswordMode) return;
        var history = _session.History;
        if (history.Count == 0) return;

        int next;
        if (direction < 0)
            next = _historyIndex < 0 ? history.Count - 1 : Math.Max(0, _historyIndex - 1);
        else if (_historyIndex < 0)
            return;
        else
            next = _historyIndex + 1;

        if (next >= history.Count)
        {
            _historyIndex = -1;
            SetInputText(string.Empty);
        }
        else
        {
            _historyIndex = next;
            SetInputText(history[next]);
            _txtInput.SelectAll();
        }
        _sound.PlayUiSound("click");
    }

    private void SetInputText(string text)
    {
        _settingInputText = true;
        _txtInput.Text = text;
        _settingInputText = false;
    }

    private void TxtInput_TextChanged(object? sender, EventArgs e)
    {
        // Typing abandons history browsing, so the next Up arrow starts from the newest command.
        if (!_settingInputText) _historyIndex = -1;
    }

    // ── Received and Messages (read-only) ──────────────────────────────────

    private void ReadOnlyBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not AnsiTerminalBox box) return;

        if (e.KeyCode == Keys.Enter && e.Modifiers == Keys.None)
        {
            e.SuppressKeyPress = true;
            if (UrlOpener.Parse(box.GetWordAtCaret()) is { } uri) OpenLink(uri);
            else System.Media.SystemSounds.Beep.Play();
        }
        else if (e.KeyCode == Keys.F && e.Modifiers == Keys.Control)
        {
            e.SuppressKeyPress = true;
            var message = box == _rtbMessages ? MessageAtCaret() : _shownMessages.Count > 0 ? _shownMessages[^1].Message : null;
            _announcer.Announce(
                message is null ? Strings.Client_MessageTimeNotFound : string.Format(Strings.Client_MessageTime, message.Time),
                AnnouncePriority.Interrupt);
        }
    }

    /// <summary>Typing while reviewing text sends the character to the input box instead of being lost.</summary>
    private void ReadOnlyBox_KeyPress(object? sender, KeyPressEventArgs e)
    {
        if (char.IsControl(e.KeyChar) || !_txtInput.Enabled) return;
        if ((ModifierKeys & (Keys.Control | Keys.Alt)) is Keys.Control or Keys.Alt) return;

        e.Handled = true;
        _txtInput.Focus();
        _txtInput.SelectedText = e.KeyChar.ToString();
    }

    private void ReadOnlyBox_LinkClicked(object? sender, LinkClickedEventArgs e)
    {
        if (UrlOpener.Parse(e.LinkText) is { } uri) OpenLink(uri);
    }

    private void OpenLink(Uri uri)
    {
        if (UrlOpener.Open(uri))
            _sound.PlayUiSound("url");
        else
            MessageBox.Show(this, string.Format(Strings.Client_UrlFailed, uri), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
