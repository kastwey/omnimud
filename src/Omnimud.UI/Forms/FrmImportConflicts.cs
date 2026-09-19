using Omnimud.Core.Session;
using Omnimud.Data.Exchange;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Services.Accessibility;

namespace Omnimud.UI.Forms;

/// <summary>
/// Generic "these elements already exist" dialog of every import: a list with check boxes
/// (checked = overwrite, unchecked = skip) plus "overwrite all" and "skip all".
/// Logic in <see cref="ImportConflictsPresenter"/>.
/// </summary>
public sealed class FrmImportConflicts : Form
{
    private readonly ImportConflictsPresenter _presenter;
    private readonly CheckedListBox _list;
    private readonly Label _lblStatus;
    private readonly IAnnouncer _announcer;
    private bool _syncing;

    public FrmImportConflicts(ImportConflictsPresenter presenter, IAnnouncer? announcer = null)
    {
        _presenter = presenter;
        _announcer = announcer ?? new Announcer(new ControlUiaNotifier(this));
        FormKit.SetupDialog(this, nameof(FrmImportConflicts), Strings.ImportConflicts_Title, new Size(620, 470));

        var lblIntro = FormKit.Hint("_lblIntro", Strings.ImportConflicts_Intro, new Rectangle(12, 10, 596, 44), 0);
        var lblList = FormKit.Label("_lblList", Strings.ImportConflicts_ListLabel, 12, 56, 1);
        _list = new CheckedListBox
        {
            Name = "_list", AccessibleName = FormKit.NameFrom(lblList.Text), Bounds = new Rectangle(12, 82, 596, 290),
            TabIndex = 2, CheckOnClick = true, IntegralHeight = false, HorizontalScrollbar = true,
        };
        _list.ItemCheck += List_ItemCheck;

        var btnAll = FormKit.Button("_btnOverwriteAll", Strings.ImportConflicts_OverwriteAll, new Rectangle(12, 382, 170, 30), 3, () => SetAll(true));
        var btnNone = FormKit.Button("_btnSkipAll", Strings.ImportConflicts_SkipAll, new Rectangle(190, 382, 170, 30), 4, () => SetAll(false));
        // Read by screen readers as the state of the decisions.
        _lblStatus = FormKit.Hint("_lblStatus", string.Empty, new Rectangle(12, 420, 370, 40), 5);

        var btnOk = FormKit.Button("_btnOk", Strings.ImportConflicts_Import, new Rectangle(392, 426, 104, 30), 6);
        btnOk.DialogResult = DialogResult.OK;
        var btnCancel = FormKit.Button("_btnCancel", Strings.Common_CancelButton, new Rectangle(504, 426, 104, 30), 7);
        btnCancel.DialogResult = DialogResult.Cancel;

        Controls.AddRange([lblIntro, lblList, _list, btnAll, btnNone, _lblStatus, btnOk, btnCancel]);
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        Fill();
    }

    /// <summary>What the user decided for each conflicting element.</summary>
    public IReadOnlyDictionary<string, ImportDecision> Decisions => _presenter.Decisions;

    private void Fill()
    {
        _syncing = true;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var row in _presenter.Rows)
            _list.Items.Add(row, row.Overwrite);
        _list.EndUpdate();
        _syncing = false;

        // A list is never left without a selected element.
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        _lblStatus.Text = _presenter.Status;
    }

    private void List_ItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (_syncing) return;
        _presenter.SetOverwrite(e.Index, e.NewValue == CheckState.Checked);
        _lblStatus.Text = _presenter.Status;
    }

    internal void SetAll(bool overwrite)
    {
        _presenter.SetAll(overwrite);
        _syncing = true;
        for (var i = 0; i < _list.Items.Count; i++)
            _list.SetItemChecked(i, overwrite);
        _syncing = false;
        _lblStatus.Text = _presenter.Status;
        // The focus stays on the button: say what happened to the list.
        _announcer.Announce(_presenter.Status, AnnouncePriority.MostRecent);
    }
}

/// <summary>Shows <see cref="FrmImportConflicts"/> to resolve the conflicts of an import.</summary>
public sealed class ImportConflictsDialog(Func<IWin32Window?> owner) : IImportConflictResolver
{
    public ImportConflictsDialog() : this(() => Form.ActiveForm) { }

    public ImportConflictsDialog(IWin32Window owner) : this(() => owner) { }

    public IReadOnlyDictionary<string, ImportDecision>? Resolve(IReadOnlyList<ImportItem> items)
    {
        var presenter = new ImportConflictsPresenter(items);
        if (!presenter.HasConflicts) return presenter.Decisions;

        using var dialog = new FrmImportConflicts(presenter);
        return dialog.ShowDialog(owner()) == DialogResult.OK ? dialog.Decisions : null;
    }
}
