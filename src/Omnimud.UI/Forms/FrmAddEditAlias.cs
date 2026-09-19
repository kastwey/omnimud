using Omnimud.Data.Entities;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;

namespace Omnimud.UI.Forms;

/// <summary>Add/edit one alias. Rules are in <see cref="AliasEditorModel"/>.</summary>
public sealed class FrmAddEditAlias : Form
{
    private readonly AliasEditorModel _model;
    private readonly IUserPrompts _prompts;
    private readonly TextBox _txtCommand;
    private readonly TextBox _txtAction;
    private readonly CheckBox _chkEnabled;

    /// <summary>Compatible with the previous dialog: no duplicate check (the caller handles it).</summary>
    public FrmAddEditAlias(AliasEntity? existing = null)
        : this(new AliasEditorModel(existing, existing is null or { Id: 0 }), new WinFormsUserPrompts())
    {
    }

    public FrmAddEditAlias(AliasEditorModel model, IUserPrompts prompts)
    {
        _model = model;
        _prompts = prompts;

        EditorDialogs.Prepare(this, nameof(FrmAddEditAlias), model.IsNew ? Strings.AliasEdit_AddTitle : Strings.AliasEdit_EditTitle, new Size(480, 210));

        var lblCommand = EditorDialogs.Label("_lblCommand", Strings.AliasEdit_CommandLabel, 12, 12, 0);
        _txtCommand = EditorDialogs.TextBox("_txtCommand", Strings.AliasEdit_CommandName, 12, 34, 456, 1, 255);
        var lblAction = EditorDialogs.Label("_lblAction", Strings.AliasEdit_ActionLabel, 12, 68, 2);
        _txtAction = EditorDialogs.TextBox("_txtAction", Strings.AliasEdit_ActionName, 12, 90, 456, 3, 500);
        _chkEnabled = new CheckBox { Name = "_chkEnabled", Text = Strings.AliasEdit_Enabled, AutoSize = true, Location = new Point(12, 126), TabIndex = 4 };

        Controls.AddRange([lblCommand, _txtCommand, lblAction, _txtAction, _chkEnabled]);
        EditorDialogs.Buttons(this, 5, () => { if (TryAccept()) DialogResult = DialogResult.OK; });

        _txtCommand.Text = model.Command;
        _txtAction.Text = model.Action;
        _chkEnabled.Checked = model.Enabled;
    }

    public string AliasCommand => _txtCommand.Text.Trim();
    public string AliasAction => _txtAction.Text.Trim();
    public bool AliasEnabled => _chkEnabled.Checked;

    /// <summary>Validates; on error shows the message and focuses the field. True = the dialog may close with OK.</summary>
    internal bool TryAccept()
    {
        _model.Command = _txtCommand.Text;
        _model.Action = _txtAction.Text;
        _model.Enabled = _chkEnabled.Checked;

        return EditorDialogs.Accept(_prompts, Text, _model.Validate(), _model.Warnings,
            issue => EditorDialogs.FocusField(this, issue.Field == AliasEditorModel.FieldAction ? _txtAction : _txtCommand));
    }
}
