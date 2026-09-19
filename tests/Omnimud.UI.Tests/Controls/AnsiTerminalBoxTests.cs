using System.Runtime.InteropServices;
using Omnimud.Core.Options;
using Omnimud.Core.Text;
using Omnimud.UI.Controls;

namespace Omnimud.UI.Tests.Controls;

/// <summary>
/// The terminal's contract with screen reader users: text that arrives must not move the
/// caret or the selection while they are reviewing earlier text.
/// Every test runs on both append paths (Text Object Model and selection fallback).
/// </summary>
public sealed class AnsiTerminalBoxTests
{
    private static AnsiTerminalBox Create(bool useTom, CursorBehavior behavior = CursorBehavior.FollowIfAtEnd)
    {
        var box = new AnsiTerminalBox { Width = 400, Height = 200, CursorBehavior = behavior };
        box.CreateControl();
        box.DisableTextObjectModel = !useTom;
        return box;
    }

    private static StyledSegment[] Plain(string text) => [new StyledSegment(text, AnsiStyle.Default)];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AppendLine_BuildsTextLineByLine(bool useTom) => Sta.Run(() =>
    {
        using var box = Create(useTom);
        box.AppendLine(Plain("uno"));
        box.AppendLine(Plain(""));
        box.AppendLine(Plain("tres"));

        box.Text.Should().Be("uno\n\ntres");
        box.LineCount.Should().Be(3);
    });

    [Fact]
    public void AppendLine_UsesTextObjectModel_WhenAvailable() => Sta.Run(() =>
    {
        using var box = Create(useTom: true);
        box.AppendLine(Plain("hola"));
        box.DisableTextObjectModel.Should().BeFalse("TOM should work on a standard RichEdit; otherwise every append moves the selection");
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FollowIfAtEnd_CaretAtEnd_FollowsNewText(bool useTom) => Sta.Run(() =>
    {
        using var box = Create(useTom);
        box.AppendLine(Plain("primera"));
        box.AppendLine(Plain("segunda"));

        box.SelectionStart.Should().Be(box.TextLength);
        box.SelectionLength.Should().Be(0);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FollowIfAtEnd_UserReviewingEarlierText_CaretDoesNotMove(bool useTom) => Sta.Run(() =>
    {
        using var box = Create(useTom);
        box.AppendLine(Plain("primera linea"));
        box.AppendLine(Plain("segunda linea"));
        box.Select(3, 0);

        for (var i = 0; i < 20; i++) box.AppendLine(Plain($"texto nuevo {i}"));

        box.SelectionStart.Should().Be(3);
        box.SelectionLength.Should().Be(0);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IncomingText_PreservesUserSelection(bool useTom) => Sta.Run(() =>
    {
        using var box = Create(useTom);
        box.AppendLine(Plain("primera linea"));
        box.Select(0, 7);

        box.AppendLine(Plain("otra"));

        box.SelectionStart.Should().Be(0);
        box.SelectionLength.Should().Be(7);
        box.SelectedText.Should().Be("primera");
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Keep_NeverMovesCaret_EvenAtEnd(bool useTom) => Sta.Run(() =>
    {
        using var box = Create(useTom, CursorBehavior.Keep);
        box.AppendLine(Plain("primera"));
        var caret = box.SelectionStart;

        box.AppendLine(Plain("segunda"));

        box.SelectionStart.Should().Be(caret);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GoToEnd_AlwaysMovesCaretToEnd(bool useTom) => Sta.Run(() =>
    {
        using var box = Create(useTom, CursorBehavior.GoToEnd);
        box.AppendLine(Plain("primera"));
        box.Select(0, 0);

        box.AppendLine(Plain("segunda"));

        box.SelectionStart.Should().Be(box.TextLength);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Trim_RemovesOldestLines_AndKeepsCaretOnTheSameText(bool useTom) => Sta.Run(() =>
    {
        using var box = Create(useTom, CursorBehavior.Keep);
        box.MaxLines = 100;
        for (var i = 0; i < 100; i++) box.AppendLine(Plain($"linea {i:000}"));

        var marker = box.Text.IndexOf("linea 090", StringComparison.Ordinal);
        box.Select(marker, 9);

        // Past max + slack (max/10, at least 50) the oldest lines go away in one batch.
        for (var i = 100; i < 151; i++) box.AppendLine(Plain($"linea {i:000}"));

        box.LineCount.Should().Be(100);
        box.Text.Should().StartWith("linea 051");
        box.Text.Should().EndWith("linea 150");
        box.Text.Split('\n').Should().HaveCount(100);
        box.SelectedText.Should().Be("linea 090");
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Box_IsNotDeclaredReadOnly_SoScreenReadersReadTheCaretLineInsteadOfEverything(bool useTom) => Sta.Run(() =>
    {
        // NVDA speaks the whole value of a read-only edit box when it gets the focus.
        using var box = Create(useTom);
        box.AppendLine(Plain("texto"));

        box.ReadOnly.Should().BeFalse();
        (Accessibility.Msaa.Read(box.Handle).State & Accessibility.Msaa.StateReadOnly).Should().Be(0);
    });

    [Fact]
    public void GettingTheFocus_DoesNotHandTheWholeTextToTheScreenReader() => Sta.Run(() =>
    {
        // Regression: WinForms' RichTextBox.OnGotFocus raises a UIA notification with the control's
        // whole Text, so NVDA spoke the entire buffer every time the box got the focus.
        // GotFocus is raised from inside that same base method, so what Text returns there is what
        // the notification would carry.
        using var box = Create(useTom: true);
        box.AppendLine(Plain("primera linea"));
        box.AppendLine(Plain("segunda linea"));
        string? textSeenByFocusHandling = null;
        box.GotFocus += (_, _) => textSeenByFocusHandling = box.Text;

        typeof(Control).GetMethod("OnGotFocus", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(box, [EventArgs.Empty]);

        textSeenByFocusHandling.Should().BeEmpty();
        box.Text.Should().Be("primera linea\nsegunda linea", "the text is only hidden while the focus notification is built");
    });

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [Theory]
    [InlineData(0x0102, 'x')]  // WM_CHAR
    [InlineData(0x0102, 8)]    // WM_CHAR backspace
    [InlineData(0x0286, 'x')]  // WM_IME_CHAR
    [InlineData(0x0300, 0)]    // WM_CUT
    [InlineData(0x0302, 0)]    // WM_PASTE
    [InlineData(0x0303, 0)]    // WM_CLEAR
    [InlineData(0x0304, 0)]    // WM_UNDO
    [InlineData(0x00C7, 0)]    // EM_UNDO
    public void TheUserCanNeverChangeTheText_WhateverRouteTheEditTakes(int message, int wParam) => Sta.Run(() =>
    {
        using var box = Create(useTom: true, CursorBehavior.Keep);
        box.AppendLine(Plain("primera linea"));
        box.AppendLine(Plain("segunda linea"));
        // The clipboard is shared with the whole desktop and may be busy: the paste test only needs a best effort.
        try { Clipboard.SetText("pegado"); } catch (System.Runtime.InteropServices.ExternalException) { }
        box.Select(3, 5);

        SendMessage(box.Handle, message, wParam, IntPtr.Zero);

        box.Text.Should().Be("primera linea\nsegunda linea");
    });

    [Theory]
    [InlineData(Keys.Delete)]
    [InlineData(Keys.Back)]
    [InlineData(Keys.Enter)]
    [InlineData(Keys.Control | Keys.V)]
    [InlineData(Keys.Control | Keys.X)]
    [InlineData(Keys.Control | Keys.Z)]
    [InlineData(Keys.Shift | Keys.Insert)]
    [InlineData(Keys.Shift | Keys.Delete)]
    [InlineData(Keys.Control | Keys.E)]
    public void EditingKeys_AreSwallowed(Keys keys) => Sta.Run(() =>
    {
        using var box = Create(useTom: true);
        var args = new KeyEventArgs(keys);
        InvokeOnKeyDown(box, args);
        args.Handled.Should().BeTrue();
    });

    [Theory]
    [InlineData(Keys.Up)]
    [InlineData(Keys.Control | Keys.Home)]
    [InlineData(Keys.Shift | Keys.End)]
    [InlineData(Keys.PageDown)]
    [InlineData(Keys.Control | Keys.C)]
    [InlineData(Keys.Control | Keys.A)]
    [InlineData(Keys.Control | Keys.Insert)]
    [InlineData(Keys.Apps)]                 // context menu
    [InlineData(Keys.Shift | Keys.F10)]     // context menu
    public void NavigationSelectionAndCopyKeys_StillWork(Keys keys) => Sta.Run(() =>
    {
        using var box = Create(useTom: true);
        var args = new KeyEventArgs(keys);
        InvokeOnKeyDown(box, args);
        args.Handled.Should().BeFalse();
    });

    [Fact]
    public void TypedCharacters_StillRaiseKeyPress_SoTheWindowCanForwardThemToTheInputBox() => Sta.Run(() =>
    {
        using var box = Create(useTom: true);
        char? seen = null;
        box.KeyPress += (_, e) => seen = e.KeyChar;
        var args = new KeyPressEventArgs('m');

        typeof(Control).GetMethod("OnKeyPress", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(box, [args]);

        seen.Should().Be('m');
        args.Handled.Should().BeTrue("the character must not be inserted into the box");
    });

    // ── Context menu ───────────────────────────────────────────────────────

    private const int WmContextMenu = 0x007B;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int VkShift = 0x10;
    private const int VkApps = 0x5D;
    private const int VkF10 = 0x79;

    [DllImport("user32.dll")]
    private static extern bool GetKeyboardState(byte[] state);

    [DllImport("user32.dll")]
    private static extern bool SetKeyboardState(byte[] state);

    [Fact]
    public void ContextMenuAskedFromTheKeyboard_IsReportedWithoutPosition() => Sta.Run(() =>
    {
        using var box = Create(useTom: true);
        var requests = new List<Point?>();
        box.ContextMenuRequested += requests.Add;

        SendMessage(box.Handle, WmContextMenu, box.Handle, new IntPtr(-1));

        requests.Should().Equal([null]);
    });

    [Fact]
    public void ContextMenuAskedWithTheMouse_IsReportedAtTheClientPointOfTheClick() => Sta.Run(() =>
    {
        using var box = Create(useTom: true);
        var requests = new List<Point?>();
        box.ContextMenuRequested += requests.Add;
        var screen = box.PointToScreen(new Point(25, 40));

        SendMessage(box.Handle, WmContextMenu, box.Handle, new IntPtr((screen.Y << 16) | (screen.X & 0xFFFF)));

        requests.Should().Equal([new Point(25, 40)]);
    });

    [Fact]
    public void WithoutListeners_TheContextMenuMessage_IsLeftToWinForms() => Sta.Run(() =>
    {
        using var box = Create(useTom: true);

        var act = () => SendMessage(box.Handle, WmContextMenu, box.Handle, new IntPtr(-1));

        act.Should().NotThrow();
    });

    /// <summary>
    /// Windows turns the release of the Applications key into WM_CONTEXTMENU only for real keyboard
    /// input (sent or posted key messages do not do it, not even on a plain Control), so what can be
    /// guarded here is that the box lets both halves of the key through to the system untouched.
    /// </summary>
    [Fact]
    public void TheApplicationsKey_IsLeftToTheSystem_DownAndUp() => Sta.Run(() =>
    {
        using var box = Create(useTom: true);
        box.AppendLine(Plain("una linea"));
        var down = new KeyEventArgs(Keys.Apps);
        var up = new KeyEventArgs(Keys.Apps);

        InvokeOnKeyDown(box, down);
        typeof(Control).GetMethod("OnKeyUp", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(box, [up]);
        SendMessage(box.Handle, WmKeyDown, VkApps, new IntPtr(0x015D0001L));
        SendMessage(box.Handle, WmKeyUp, VkApps, new IntPtr(0xC15D0001L));

        down.Handled.Should().BeFalse();
        down.SuppressKeyPress.Should().BeFalse();
        up.Handled.Should().BeFalse();
        box.Text.Should().Be("una linea");
    });

    [Fact]
    public void ShiftF10_StillAsksForTheContextMenu_DespiteTheKeyProtection() => Sta.Run(() =>
    {
        using var box = Create(useTom: true);
        var requests = new List<Point?>();
        box.ContextMenuRequested += requests.Add;
        var original = new byte[256];
        GetKeyboardState(original);
        var shifted = (byte[])original.Clone();
        shifted[VkShift] |= 0x80;

        try
        {
            SetKeyboardState(shifted);   // only this thread's view of the keyboard
            SendMessage(box.Handle, WmSysKeyDown, VkF10, IntPtr.Zero);
        }
        finally
        {
            SetKeyboardState(original);
        }

        requests.Should().Equal([null]);
    });

    [Fact]
    public void KeyboardMenuLocation_IsJustUnderTheCaret() => Sta.Run(() =>
    {
        using var box = Create(useTom: true, CursorBehavior.Keep);
        box.AppendLine(Plain("primera linea"));
        box.AppendLine(Plain("segunda linea"));
        box.AppendLine(Plain("tercera linea"));
        box.Select(21, 0);   // inside the second line

        var location = box.KeyboardMenuLocation();

        var caret = box.GetPositionFromCharIndex(21);
        location.Should().Be(new Point(caret.X, caret.Y + box.Font.Height));
        location.Y.Should().BeGreaterThan(box.GetPositionFromCharIndex(0).Y, "it follows the caret, not the top of the box");
        box.ClientRectangle.Contains(location).Should().BeTrue();
    });

    [Fact]
    public void KeyboardMenuLocation_StaysInsideTheBox_WhenTheCaretIsScrolledOutOfView() => Sta.Run(() =>
    {
        using var box = Create(useTom: true, CursorBehavior.Keep);
        for (var i = 0; i < 200; i++) box.AppendLine(Plain($"linea {i}"));
        box.Select(0, 0);
        SendMessage(box.Handle, 0x0115 /* WM_VSCROLL */, 7 /* SB_BOTTOM */, IntPtr.Zero);

        box.ClientRectangle.Contains(box.KeyboardMenuLocation()).Should().BeTrue();

        box.Select(box.TextLength, 0);
        SendMessage(box.Handle, 0x0115, 6 /* SB_TOP */, IntPtr.Zero);
        box.ClientRectangle.Contains(box.KeyboardMenuLocation()).Should().BeTrue();
    });

    [Fact]
    public void KeyboardMenuLocation_OnAnEmptyBox_IsInsideIt() => Sta.Run(() =>
    {
        using var box = Create(useTom: true);

        box.ClientRectangle.Contains(box.KeyboardMenuLocation()).Should().BeTrue();
    });

    private static void InvokeOnKeyDown(Control control, KeyEventArgs args) =>
        typeof(Control).GetMethod("OnKeyDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(control, [args]);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Colors_AreApplied(bool useTom) => Sta.Run(() =>
    {
        using var box = Create(useTom, CursorBehavior.Keep);
        box.AppendLine([
            new StyledSegment("rojo", new AnsiStyle(AnsiColor.BrightRed, AnsiColor.DefaultBackground)),
            new StyledSegment(" normal", AnsiStyle.Default)]);

        if (SystemInformation.HighContrast) return;

        box.Select(1, 1);
        box.SelectionColor.ToArgb().Should().Be(Color.FromArgb(255, 85, 85).ToArgb());
        box.Select(6, 1);
        box.SelectionColor.ToArgb().Should().Be(box.ForeColor.ToArgb());
    });

    [Fact]
    public void Clear_ResetsLineAccounting() => Sta.Run(() =>
    {
        using var box = Create(useTom: true);
        box.AppendLine(Plain("a"));
        box.Clear();
        box.AppendLine(Plain("b"));

        box.Text.Should().Be("b");
        box.LineCount.Should().Be(1);
    });

    [Theory]
    [InlineData("mira http://ejemplo.org/pagina, vale", 10, "http://ejemplo.org/pagina")]
    [InlineData("escribe a info@omnimud.org.", 14, "info@omnimud.org")]
    [InlineData("sin enlaces", 2, "sin")]
    public void GetWordAtCaret_ReturnsTheTokenUnderTheCaret(string line, int caret, string expected) => Sta.Run(() =>
    {
        using var box = Create(useTom: true, CursorBehavior.Keep);
        box.AppendLine(Plain(line));
        box.Select(caret, 0);

        box.GetWordAtCaret().Should().Be(expected);
    });
}
