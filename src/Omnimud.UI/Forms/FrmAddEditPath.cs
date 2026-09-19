using Omnimud.Core.Session;
using Omnimud.Data.Entities;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;
using Omnimud.UI.Services.Accessibility;

namespace Omnimud.UI.Forms;

/// <summary>Add/edit one path. Rules are in <see cref="PathEditorModel"/>.</summary>
public sealed class FrmAddEditPath : Form
{
    private readonly PathEditorModel _model;
    private readonly IUserPrompts _prompts;
    private readonly IAnnouncer _announcer;
    private readonly TextBox _txtName;
    private readonly TextBox _txtPath;
    private readonly TextBox _txtExpansion;

    /// <summary>
    /// Compatible with the previous dialog (also used for a freshly recorded path, which arrives
    /// with Id 0): the path is not checked against any direction dictionary.
    /// </summary>
    public FrmAddEditPath(PathEntity? existing = null)
        : this(new PathEditorModel(existing, existing is null or { Id: 0 }), new WinFormsUserPrompts(), null)
    {
    }

    public FrmAddEditPath(PathEditorModel model, IUserPrompts prompts, IAnnouncer? announcer = null)
    {
        _model = model;
        _prompts = prompts;

        var title = model.IsNew ? Strings.PathEdit_AddTitle : string.Format(Strings.PathEdit_EditTitle, model.Name);
        EditorDialogs.Prepare(this, nameof(FrmAddEditPath), title, new Size(520, 300));

        var lblName = EditorDialogs.Label("_lblName", Strings.PathEdit_NameLabel, 12, 12, 0);
        _txtName = EditorDialogs.TextBox("_txtName", Strings.PathEdit_NameName, 12, 34, 496, 1, 200);
        var lblPath = EditorDialogs.Label("_lblPath", Strings.PathEdit_PathLabel, 12, 68, 2);
        _txtPath = EditorDialogs.TextBox("_txtPath", Strings.PathEdit_PathName, 12, 90, 496, 3);
        _txtPath.Leave += (_, _) => ShowExpansion(announce: false); // the focus is moving: the reader is busy with the next control

        var btnExpand = new Button { Name = "_btnExpand", Text = Strings.PathEdit_ShowExpansion, Location = new Point(12, 124), Size = new Size(160, 30), TabIndex = 4 };
        btnExpand.Click += (_, _) => ShowExpansion(announce: true);
        if (announcer is null)
        {
            var own = new FormAnnouncer();
            own.Attach(btnExpand);
            announcer = own;
        }
        _announcer = announcer;

        // A read-only box rather than a bare label: it can be tabbed to and read at leisure.
        var lblExpansion = EditorDialogs.Label("_lblExpansion", Strings.PathEdit_ExpansionLabel, 12, 162, 5);
        _txtExpansion = new TextBox
        {
            Name = "_txtExpansion", AccessibleName = Strings.PathEdit_ExpansionName, Location = new Point(12, 184), Size = new Size(496, 60),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, TabIndex = 6,
        };

        Controls.AddRange([lblName, _txtName, lblPath, _txtPath, btnExpand, lblExpansion, _txtExpansion]);
        EditorDialogs.Buttons(this, 7, () => { if (TryAccept()) DialogResult = DialogResult.OK; });

        _txtName.Text = model.Name;
        _txtPath.Text = model.Path;
        if (model.Path.Length > 0) ShowExpansion(announce: false);
    }

    public string PathName => _txtName.Text.Trim();

    /// <summary>The path as it must be stored (collapsed when there is a dictionary).</summary>
    public string PathValue
    {
        get
        {
            Pull();
            return _model.CollapsedPath;
        }
    }

    internal string ExpansionText => _txtExpansion.Text;

    private void Pull()
    {
        _model.Name = _txtName.Text;
        _model.Path = _txtPath.Text;
    }

    internal void ShowExpansion(bool announce)
    {
        Pull();
        _txtExpansion.Text = _model.DescribeExpansion();
        if (announce && _txtExpansion.Text.Length > 0) _announcer.Announce(_txtExpansion.Text, AnnouncePriority.MostRecent);
    }

    /// <summary>Validates; on error shows the message and focuses the field. True = the dialog may close with OK.</summary>
    internal bool TryAccept()
    {
        Pull();
        return EditorDialogs.Accept(_prompts, Text, _model.Validate(), _model.Warnings,
            issue => EditorDialogs.FocusField(this, issue.Field == PathEditorModel.FieldName ? _txtName : _txtPath));
    }
}
