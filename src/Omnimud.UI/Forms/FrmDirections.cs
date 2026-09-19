using System.Globalization;
using Omnimud.Data;
using Omnimud.Data.Entities;
using Omnimud.Data.Repositories;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;

namespace Omnimud.UI.Forms;

/// <summary>
/// Direction dictionary of a MUD: full command, one-character abbreviation used inside paths
/// and the opposite direction used to walk a path backwards. Without it paths cannot be
/// created or run, so the dialog offers to load the usual set.
/// </summary>
public sealed class FrmDirections : Form
{
    private readonly IDirectionRepository _repository;
    private readonly int _mudId;
    private readonly ListView _list;
    private readonly Button _btnEdit;
    private readonly Button _btnRemove;
    private readonly Label _lblStatus;

    public bool Changed { get; private set; }

    public FrmDirections(IDirectionRepository repository, int mudId, string mudName)
    {
        _repository = repository;
        _mudId = mudId;

        Text = string.Format(Strings.Directions_Title, mudName);
        Name = nameof(FrmDirections);
        Font = new Font("Segoe UI", 9.75F);
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 400);
        Padding = new Padding(12);

        var label = new Label { Name = "_lblList", Text = Strings.Directions_ListLabel, AutoSize = true, Location = new Point(12, 12), TabIndex = 0 };
        _list = new ListView
        {
            Name = "_list", Location = new Point(12, 34), Size = new Size(400, 320), TabIndex = 1,
            View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false,
            AccessibleName = Strings.Directions_ListName,
        };
        _list.Columns.Add(Strings.Directions_ColDirection, 170);
        _list.Columns.Add(Strings.Directions_ColAbbreviation, 90);
        _list.Columns.Add(Strings.Directions_ColOpposite, 130);
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.ItemActivate += (_, _) => EditSelected();
        _list.KeyDown += List_KeyDown;

        var btnAdd = MakeButton("_btnAdd", Strings.Directions_Add, 34, 2, AddNew);
        _btnEdit = MakeButton("_btnEdit", Strings.Directions_Edit, 70, 3, EditSelected);
        _btnRemove = MakeButton("_btnRemove", Strings.Directions_Remove, 106, 4, RemoveSelected);
        var btnCommon = MakeButton("_btnCommon", Strings.Directions_LoadCommon, 160, 5, LoadCommon);
        btnCommon.Height = 48;
        var btnClose = MakeButton("_btnClose", Strings.Directions_Close, 324, 6, Close);
        btnClose.DialogResult = DialogResult.Cancel;

        // Read by screen readers as the status of the last action.
        _lblStatus = new Label { Name = "_lblStatus", AutoSize = true, Location = new Point(12, 366), UseMnemonic = false, TabIndex = 7 };

        var menu = new ContextMenuStrip();
        menu.Items.Add(Strings.Directions_Add, null, (_, _) => AddNew());
        menu.Items.Add(Strings.Directions_Edit, null, (_, _) => EditSelected());
        menu.Items.Add(Strings.Directions_Remove, null, (_, _) => RemoveSelected());
        _list.ContextMenuStrip = menu;

        Controls.AddRange([label, _list, btnAdd, _btnEdit, _btnRemove, btnCommon, btnClose, _lblStatus]);
        CancelButton = btnClose;
        AcceptButton = _btnEdit;
        UpdateButtons();
    }

    private Button MakeButton(string name, string text, int top, int tabIndex, Action onClick)
    {
        var button = new Button { Name = name, Text = text, Location = new Point(424, top), Size = new Size(124, 30), TabIndex = tabIndex };
        button.Click += (_, _) => onClick();
        return button;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ErrorReporter.Run(this, () => ReloadAsync(selectId: null));
    }

    private async Task ReloadAsync(int? selectId, int fallbackIndex = 0)
    {
        var directions = await _repository.GetByMudAsync(_mudId);

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var d in directions.OrderBy(d => d.Direction, StringComparer.CurrentCultureIgnoreCase))
            _list.Items.Add(new ListViewItem([d.Direction, d.Abbreviation, d.OppositeDirection ?? string.Empty]) { Tag = d });
        _list.EndUpdate();

        // A list must never be left without a selected item: keyboard users would lose their place.
        if (_list.Items.Count > 0)
        {
            var item = _list.Items.Cast<ListViewItem>().FirstOrDefault(i => ((DirectionEntity)i.Tag!).Id == selectId)
                       ?? _list.Items[Math.Clamp(fallbackIndex, 0, _list.Items.Count - 1)];
            item.Selected = true;
            item.Focused = true;
            item.EnsureVisible();
        }
        UpdateButtons();
    }

    private DirectionEntity? Selected => _list.SelectedItems.Count > 0 ? (DirectionEntity)_list.SelectedItems[0].Tag! : null;

    private void UpdateButtons()
    {
        _btnEdit.Enabled = Selected is not null;
        _btnRemove.Enabled = Selected is not null;
    }

    private void List_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Delete: e.Handled = true; RemoveSelected(); break;
            case Keys.Insert: e.Handled = true; AddNew(); break;
            case Keys.F2: e.Handled = true; EditSelected(); break;
        }
    }

    private void AddNew() => ErrorReporter.Run(this, async () =>
    {
        using var dialog = new FrmAddEditDirection(null);
        while (dialog.ShowDialog(this) == DialogResult.OK)
        {
            try
            {
                var id = await _repository.AddAsync(new DirectionEntity
                {
                    MudId = _mudId, Direction = dialog.Direction, Abbreviation = dialog.Abbreviation, OppositeDirection = dialog.Opposite,
                });
                Changed = true;
                await ReloadAsync(id);
                _list.Focus();
                return;
            }
            catch (DuplicateEntityException)
            {
                MessageBox.Show(this, Strings.Directions_ErrDuplicate, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    });

    private void EditSelected() => ErrorReporter.Run(this, async () =>
    {
        if (Selected is not { } current) return;
        using var dialog = new FrmAddEditDirection(current);
        while (dialog.ShowDialog(this) == DialogResult.OK)
        {
            try
            {
                current.Direction = dialog.Direction;
                current.Abbreviation = dialog.Abbreviation;
                current.OppositeDirection = dialog.Opposite;
                await _repository.UpdateAsync(current);
                Changed = true;
                await ReloadAsync(current.Id);
                _list.Focus();
                return;
            }
            catch (DuplicateEntityException)
            {
                MessageBox.Show(this, Strings.Directions_ErrDuplicate, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        await ReloadAsync(current.Id);
    });

    private void RemoveSelected() => ErrorReporter.Run(this, async () =>
    {
        if (Selected is not { } current) return;
        if (MessageBox.Show(this, string.Format(Strings.Directions_ConfirmRemove, current.Direction), Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        var index = _list.SelectedIndices[0];
        await _repository.DeleteAsync(current.Id);
        Changed = true;
        await ReloadAsync(selectId: null, fallbackIndex: index);
        _lblStatus.Text = string.Format(Strings.Directions_Removed, current.Direction);
        _list.Focus();
    });

    private void LoadCommon() => ErrorReporter.Run(this, async () =>
    {
        if (MessageBox.Show(this, Strings.Directions_ConfirmLoadCommon, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        var existing = await _repository.GetByMudAsync(_mudId);
        var added = 0;
        foreach (var (direction, abbreviation, opposite) in CommonDirections(Strings.Culture ?? CultureInfo.CurrentUICulture))
        {
            if (existing.Any(e => string.Equals(e.Direction, direction, StringComparison.OrdinalIgnoreCase) || e.Abbreviation == abbreviation))
                continue;
            await _repository.AddAsync(new DirectionEntity { MudId = _mudId, Direction = direction, Abbreviation = abbreviation, OppositeDirection = opposite });
            added++;
        }

        if (added > 0) Changed = true;
        await ReloadAsync(Selected?.Id);
        _lblStatus.Text = string.Format(Strings.Directions_LoadedCount, added);
        _list.Focus();
    });

    /// <summary>The usual compass set. Abbreviations are one character because a path is a run of "[count]letter".</summary>
    internal static IReadOnlyList<(string Direction, string Abbreviation, string Opposite)> CommonDirections(CultureInfo culture) =>
        culture.TwoLetterISOLanguageName == "es"
            ?
            [
                ("norte", "n", "sur"), ("sur", "s", "norte"), ("este", "e", "oeste"), ("oeste", "o", "este"),
                ("noreste", "r", "sudoeste"), ("noroeste", "q", "sudeste"), ("sudeste", "c", "noroeste"), ("sudoeste", "z", "noreste"),
                ("arriba", "a", "abajo"), ("abajo", "b", "arriba"), ("dentro", "d", "fuera"), ("fuera", "f", "dentro"),
            ]
            :
            [
                ("north", "n", "south"), ("south", "s", "north"), ("east", "e", "west"), ("west", "w", "east"),
                ("northeast", "r", "southwest"), ("northwest", "q", "southeast"), ("southeast", "c", "northwest"), ("southwest", "z", "northeast"),
                ("up", "u", "down"), ("down", "d", "up"), ("in", "i", "out"), ("out", "o", "in"),
            ];
}

/// <summary>Add/edit one direction.</summary>
public sealed class FrmAddEditDirection : Form
{
    private readonly TextBox _txtDirection;
    private readonly TextBox _txtAbbreviation;
    private readonly TextBox _txtOpposite;

    public FrmAddEditDirection(DirectionEntity? existing)
    {
        Text = existing is null ? Strings.Directions_AddTitle : Strings.Directions_EditTitle;
        Name = nameof(FrmAddEditDirection);
        Font = new Font("Segoe UI", 9.75F);
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(400, 250);

        (_, _txtDirection) = Field("Direction", Strings.Directions_DirectionLabel, Strings.Directions_DirectionName, Strings.Directions_DirectionDescription, 12, 0, 255);
        (_, _txtAbbreviation) = Field("Abbreviation", Strings.Directions_AbbreviationLabel, Strings.Directions_AbbreviationName, Strings.Directions_AbbreviationDescription, 72, 2, 1);
        (_, _txtOpposite) = Field("Opposite", Strings.Directions_OppositeLabel, Strings.Directions_OppositeName, Strings.Directions_OppositeDescription, 132, 4, 255);
        _txtAbbreviation.Width = 60;

        var ok = new Button { Name = "_btnOk", Text = Strings.Common_OKButton, Location = new Point(192, 204), Size = new Size(95, 30), TabIndex = 6 };
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Name = "_btnCancel", Text = Strings.Common_CancelButton, DialogResult = DialogResult.Cancel, Location = new Point(293, 204), Size = new Size(95, 30), TabIndex = 7 };
        Controls.AddRange([ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;

        if (existing is not null)
        {
            _txtDirection.Text = existing.Direction;
            _txtAbbreviation.Text = existing.Abbreviation;
            _txtOpposite.Text = existing.OppositeDirection ?? string.Empty;
        }
    }

    public string Direction => _txtDirection.Text.Trim();
    public string Abbreviation => _txtAbbreviation.Text.Trim();
    public string? Opposite => _txtOpposite.Text.Trim() is { Length: > 0 } text ? text : null;

    private (Label, TextBox) Field(string name, string label, string accessibleName, string description, int top, int tabIndex, int maxLength)
    {
        var lbl = new Label { Name = "_lbl" + name, Text = label, AutoSize = true, Location = new Point(12, top), TabIndex = tabIndex };
        var box = new TextBox
        {
            Name = "_txt" + name, Location = new Point(12, top + 22), Size = new Size(376, 25), MaxLength = maxLength, TabIndex = tabIndex + 1,
            AccessibleName = accessibleName, AccessibleDescription = description,
        };
        box.Enter += (_, _) => box.SelectAll();
        Controls.AddRange([lbl, box]);
        return (lbl, box);
    }

    private void Accept()
    {
        if (Direction.Length == 0)
        {
            Fail(Strings.Directions_ErrDirectionEmpty, _txtDirection);
            return;
        }
        if (Abbreviation.Length != 1 || char.IsDigit(Abbreviation[0]))
        {
            Fail(Strings.Directions_ErrAbbreviation, _txtAbbreviation);
            return;
        }
        DialogResult = DialogResult.OK;
    }

    /// <summary>After a validation error the focus goes to the offending field.</summary>
    private void Fail(string message, Control field)
    {
        MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        field.Focus();
    }
}
