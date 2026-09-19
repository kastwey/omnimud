using System.Globalization;
using Omnimud.Core.Session;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Forms;

/// <summary>
/// Commands bound to the movement keys (movement mode, F2): arrows, Page Up/Down, Home and End
/// for keyboards without numeric keypad, and the keypad digits 0-9.
/// One labelled text box per key instead of a list plus an edit box: every key is one Tab (or one
/// Alt+letter) away and the screen reader reads "Up arrow, edit, north" with no extra steps.
/// The boxes show the EFFECTIVE command (configured, else inherited from the MUD, else the default
/// for the language). The rules live in <see cref="MovementKeysEditor"/>; this class only binds controls.
/// </summary>
public sealed class FrmMovements : Form
{
    private const float DefaultFontPoints = 9.75F;

    private readonly MovementKeysEditor _editor;
    private readonly Dictionary<int, TextBox> _boxes = [];
    private readonly TableLayoutPanel _root;

    /// <summary>Compatible with the previous dialog: <paramref name="current"/> are the owner's keys and nothing is inherited.</summary>
    public FrmMovements(string ownerName, bool ownerIsCharacter, IReadOnlyDictionary<int, string> current)
        : this(ownerName, ownerIsCharacter, new MovementKeysEditor(
            current, new Dictionary<int, string>(), MovementKeys.DefaultCommands(Strings.Culture ?? CultureInfo.CurrentUICulture)))
    {
    }

    public FrmMovements(string ownerName, bool ownerIsCharacter, MovementKeysEditor editor)
        : this(ownerName, ownerIsCharacter, editor, DefaultFontPoints)
    {
    }

    /// <summary>The font size stands for the display scale in tests: at 200 % the same font takes twice the pixels.</summary>
    internal FrmMovements(string ownerName, bool ownerIsCharacter, MovementKeysEditor editor, float fontPoints)
    {
        _editor = editor;
        var culture = Strings.Culture ?? CultureInfo.CurrentUICulture;

        Text = string.Format(Strings.Movements_Title, ownerName);
        Name = nameof(FrmMovements);
        Font = new Font("Segoe UI", fontPoints);
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        // Not AutoSize: the window is fitted to the screen by hand and scrolls when the screen is too
        // small (big scale on a small laptop). Focusing a box scrolls it into view.
        AutoScroll = true;

        // Every size derives from the font, so the dialog grows with the display scale.
        var em = Font.Height;

        _root = new TableLayoutPanel
        {
            Name = "_layout", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2, Location = Point.Empty, Padding = new Padding(em * 2 / 3), TabIndex = 0,
        };
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var intro = new Label
        {
            Name = "_lblIntro", AutoSize = true, MaximumSize = new Size(em * 38, 0), UseMnemonic = false, TabIndex = 0,
            Margin = new Padding(3, 3, 3, em / 2),
            Text = Strings.Movements_Intro + " " + string.Format(
                ownerIsCharacter ? Strings.Movements_ScopeCharacter : Strings.Movements_ScopeMud, ownerName),
        };
        _root.Controls.Add(intro, 0, 0);
        _root.SetColumnSpan(intro, 2);

        var navigation = CreateGroup("_grpNavigation", Strings.Movements_GroupNavigation, MovementKeys.NavigationCodes, culture, em, tabIndex: 1);
        // Keypad order as it is laid out physically would be confusing when tabbing; plain 0..9 is predictable.
        var numPad = CreateGroup("_grpNumPad", Strings.Movements_GroupNumPad, MovementKeys.NumPadCodes, culture, em, tabIndex: 2);
        _root.Controls.Add(navigation, 0, 1);
        _root.Controls.Add(numPad, 1, 1);

        var buttons = new FlowLayoutPanel
        {
            Name = "_buttons", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft,
            Anchor = AnchorStyles.Right, WrapContents = false, TabIndex = 3, Margin = new Padding(3, em / 2, 3, 3),
        };
        var buttonSize = new Size(em * 5, em * 5 / 3);
        var cancel = new Button { Name = "_btnCancel", Text = Strings.Common_CancelButton, DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = buttonSize, TabIndex = 2 };
        var ok = new Button { Name = "_btnOk", Text = Strings.Common_OKButton, DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = buttonSize, TabIndex = 1 };
        var restore = new Button { Name = "_btnRestore", Text = Strings.Movements_RestoreButton, AutoSize = true, MinimumSize = buttonSize, TabIndex = 0 };
        restore.Click += (_, _) => RestoreDefaults();
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        buttons.Controls.Add(restore);
        _root.Controls.Add(buttons, 0, 2);
        _root.SetColumnSpan(buttons, 2);

        Controls.Add(_root);
        AcceptButton = ok;
        CancelButton = cancel;

        FitToScreen(Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1024, 728));
    }

    private GroupBox CreateGroup(string name, string title, IReadOnlyList<int> keys, CultureInfo culture, int em, int tabIndex)
    {
        var table = new TableLayoutPanel
        {
            Name = name + "Layout", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2, Dock = DockStyle.Fill, TabIndex = 0,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var tab = 0;
        var row = 0;
        foreach (var key in keys)
        {
            var label = new Label
            {
                Name = $"_lblKey{key}", AutoSize = true, Anchor = AnchorStyles.Left, TabIndex = tab++,
                Text = LabelText(key),
            };
            var box = new TextBox
            {
                Name = $"_txtKey{key}", Width = em * 10, Anchor = AnchorStyles.Left, MaxLength = 255, TabIndex = tab++,
                AccessibleName = MovementKeys.DisplayName(key, culture),
                Text = _editor.Effective(key),
            };
            box.Enter += (_, _) => box.SelectAll();
            _boxes[key] = box;
            table.Controls.Add(label, 0, row);
            table.Controls.Add(box, 1, row);
            row++;
        }

        var group = new GroupBox
        {
            // No mnemonic on the group: every key inside has its own.
            Name = name, Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Top | AnchorStyles.Left, Padding = new Padding(em / 2), TabIndex = tabIndex,
        };
        group.Controls.Add(table);
        return group;
    }

    private static string LabelText(int key) => (MovementKey)key switch
    {
        MovementKey.ArrowUp => Strings.Movements_LabelArrowUp,
        MovementKey.ArrowDown => Strings.Movements_LabelArrowDown,
        MovementKey.ArrowLeft => Strings.Movements_LabelArrowLeft,
        MovementKey.ArrowRight => Strings.Movements_LabelArrowRight,
        MovementKey.PageUp => Strings.Movements_LabelPageUp,
        MovementKey.PageDown => Strings.Movements_LabelPageDown,
        MovementKey.Home => Strings.Movements_LabelHome,
        MovementKey.End => Strings.Movements_LabelEnd,
        _ => string.Format(Strings.Movements_KeyLabel, key),
    };

    /// <summary>Back to what the owner gets with no keys of its own. Focus goes to the first box, with
    /// its text selected, so the screen reader reads a restored value instead of nothing.</summary>
    internal void RestoreDefaults()
    {
        foreach (var (key, box) in _boxes) box.Text = _editor.Baseline(key);

        var first = _boxes[MovementKeys.AllCodes[0]];
        first.Focus();
        first.SelectAll();
    }

    /// <summary>What has to be stored for the owner (key → command); see <see cref="MovementKeysEditor.ToSave"/>.
    /// Empty means "delete the owner's keys": it then follows its MUD or the language defaults.</summary>
    public IReadOnlyDictionary<int, string> Commands =>
        _editor.ToSave(_boxes.ToDictionary(pair => pair.Key, pair => pair.Value.Text));

    protected override void OnLoad(EventArgs e)
    {
        var area = Screen.FromControl(Owner ?? this).WorkingArea;
        FitToScreen(area);
        base.OnLoad(e); // centres on the parent

        // Centred on a parent near an edge, part of the dialog could fall outside the screen.
        Location = new Point(
            Math.Max(area.Left, Math.Min(Left, area.Right - Width)),
            Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));
    }

    /// <summary>As big as its content, but never bigger than the screen: then it scrolls.</summary>
    internal void FitToScreen(Rectangle workingArea)
    {
        var preferred = _root.GetPreferredSize(Size.Empty);
        _root.Size = preferred;
        var chrome = Size - ClientSize;
        ClientSize = FitClientSize(preferred, workingArea.Size - chrome,
            SystemInformation.VerticalScrollBarWidth, SystemInformation.HorizontalScrollBarHeight);
    }

    /// <summary>Client size for a content of <paramref name="preferred"/> pixels when at most
    /// <paramref name="available"/> fit: a scroll bar in one direction takes room from the other.</summary>
    internal static Size FitClientSize(Size preferred, Size available, int verticalBarWidth, int horizontalBarHeight)
    {
        var width = preferred.Width;
        var height = preferred.Height;

        var verticalBar = height > available.Height;
        if (verticalBar) width += verticalBarWidth;
        if (width > available.Width)
        {
            height += horizontalBarHeight;
            if (!verticalBar && height > available.Height) width += verticalBarWidth;
        }

        return new Size(Math.Min(width, available.Width), Math.Min(height, available.Height));
    }
}
