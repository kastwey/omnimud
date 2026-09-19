using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Forms;

/// <summary>Picks the character to copy a list from. Each entry reads "Name (MUD)", sorted by MUD.</summary>
public sealed class FrmPickCharacter : Form
{
    private readonly ListBox _list;

    public FrmPickCharacter(IReadOnlyList<CharacterChoice> choices)
    {
        EditorDialogs.Prepare(this, nameof(FrmPickCharacter), Strings.PickChar_Title, new Size(420, 360));

        var label = EditorDialogs.Label("_lblList", Strings.PickChar_Label, 12, 12, 0);
        _list = new ListBox
        {
            Name = "_list", AccessibleName = Strings.PickChar_Name, Location = new Point(12, 34), Size = new Size(396, 268),
            IntegralHeight = false, TabIndex = 1,
        };
        foreach (var choice in choices) _list.Items.Add(choice);
        if (_list.Items.Count > 0) _list.SelectedIndex = 0; // never without a selection

        Controls.AddRange([label, _list]);
        var (ok, _) = EditorDialogs.Buttons(this, 2, () => { if (Selected is not null) DialogResult = DialogResult.OK; });
        ok.Enabled = _list.Items.Count > 0;
        _list.DoubleClick += (_, _) => ok.PerformClick();
    }

    public CharacterChoice? Selected => _list.SelectedItem as CharacterChoice;

    internal ListBox List => _list;
}
