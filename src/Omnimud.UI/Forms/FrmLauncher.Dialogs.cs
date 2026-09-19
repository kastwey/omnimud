using Omnimud.Core.Session;
using Omnimud.UI.Presenters;
using Omnimud.UI.Services;

namespace Omnimud.UI.Forms;

/// <summary>The real dialogs of the launcher, owned by its window.</summary>
internal sealed class LauncherDialogs(IWin32Window owner, IUserPrompts prompts, IQuickConnectStore quickConnectStore) : ILauncherDialogs
{
    public bool EditMud(MudEditorModel model)
    {
        using var dialog = new FrmAddEditMud(model, prompts);
        return dialog.ShowDialog(owner) == DialogResult.OK;
    }

    public bool EditCharacter(CharacterEditorModel model)
    {
        using var dialog = new FrmAddEditCharacter(model, prompts);
        return dialog.ShowDialog(owner) == DialogResult.OK;
    }

    public SessionProfile? QuickConnect()
    {
        using var dialog = new FrmQuickConnect(prompts, quickConnectStore);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Profile : null;
    }
}

/// <summary>Small helpers shared by the management dialogs, so every one is built the same accessible way.</summary>
internal static class FormKit
{
    public static void SetupDialog(Form form, string name, string title, Size clientSize)
    {
        form.Name = name;
        form.Text = title;
        form.Font = new Font("Segoe UI", 9.75F);
        form.AutoScaleMode = AutoScaleMode.Font;
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.MaximizeBox = false;
        form.MinimizeBox = false;
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.CenterParent;
        form.ClientSize = clientSize;
    }

    /// <summary>The accessible name of a control is the text of its label without '&amp;' nor the final colon.</summary>
    public static string NameFrom(string labelText)
    {
        var text = labelText.Replace("&&", "").Replace("&", string.Empty).Replace("", "&").Trim();
        return text.TrimEnd(':').Trim();
    }

    public static Label Label(string name, string text, int x, int y, int tabIndex) =>
        new() { Name = name, Text = text, AutoSize = true, Location = new Point(x, y + 3), TabIndex = tabIndex };

    /// <summary>Explanatory text: never a mnemonic, never takes part in the tab order.</summary>
    public static Label Hint(string name, string text, Rectangle bounds, int tabIndex) =>
        new() { Name = name, Text = text, AutoSize = false, Bounds = bounds, TabIndex = tabIndex, UseMnemonic = false };

    /// <summary>A text box named after its label. Single-line boxes select their whole text when they get the focus.</summary>
    public static SelectAllTextBox TextBox(string name, Label label, Rectangle bounds, int tabIndex) =>
        new() { Name = name, AccessibleName = NameFrom(label.Text), Bounds = bounds, TabIndex = tabIndex };

    public static Button Button(string name, string text, Rectangle bounds, int tabIndex, Action? onClick = null)
    {
        var button = new Button { Name = name, Text = text, Bounds = bounds, TabIndex = tabIndex, UseVisualStyleBackColor = true };
        if (onClick is not null) button.Click += (_, _) => onClick();
        return button;
    }

    public static CheckBox CheckBox(string name, string text, int x, int y, int tabIndex) =>
        new() { Name = name, Text = text, AutoSize = true, Location = new Point(x, y), TabIndex = tabIndex, UseVisualStyleBackColor = true };

    /// <summary>Focus to the field with the problem, with its text selected so typing replaces it.</summary>
    public static void FocusField(Form form, Control control)
    {
        form.ActiveControl = control;
        if (control.CanFocus) control.Focus();
        switch (control)
        {
            case TextBoxBase box: box.SelectAll(); break;
            case ComboBox { DropDownStyle: ComboBoxStyle.DropDown } combo: combo.SelectAll(); break;
            case NumericUpDown number: number.Select(0, number.Text.Length); break;
        }
    }
}

/// <summary>
/// Text box that selects all its text every time it gets the focus, so typing replaces what was
/// there (rule 6 of the dialogs round). Multiline boxes keep the caret where it was.
/// </summary>
public class SelectAllTextBox : TextBox
{
    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        if (!Multiline) SelectAll();
    }

    /// <summary>For tests: what happens when the focus arrives.</summary>
    internal void SimulateEnter() => OnEnter(EventArgs.Empty);
}
