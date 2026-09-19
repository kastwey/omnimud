using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;

namespace Omnimud.UI.Forms;

/// <summary>
/// Add or edit one message rule (regex → message template), with a "Test" area that shows the
/// message the rule would produce for a sample line. Logic in <see cref="MessageRuleEditorModel"/>.
/// </summary>
public sealed class FrmAddEditMessageRule : Form
{
    private readonly MessageRuleEditorModel _model;
    private readonly IUserPrompts _prompts;

    private readonly TextBox _txtPattern;
    private readonly TextBox _txtTemplate;
    private readonly TextBox _txtChannel;
    private readonly CheckBox _chkCase;
    private readonly CheckBox _chkEnabled;
    private readonly TextBox _txtSample;
    private readonly TextBox _txtResult;
    private readonly Button _btnOk;
    private readonly Button _btnTest;

    public FrmAddEditMessageRule(MessageRuleEditorModel model, IUserPrompts prompts)
    {
        _model = model;
        _prompts = prompts;

        const int labelX = 12, fieldX = 240, fieldW = 380, row = 34;
        FormKit.SetupDialog(this, nameof(FrmAddEditMessageRule), model.Title, new Size(640, 420));

        var tab = 0;
        var y = 12;

        var lblPattern = FormKit.Label("_lblPattern", Strings.MsgRule_PatternLabel, labelX, y, tab++);
        _txtPattern = FormKit.TextBox("_txtPattern", lblPattern, new Rectangle(fieldX, y, fieldW, 25), tab++);
        y += row;

        var lblTemplate = FormKit.Label("_lblTemplate", Strings.MsgRule_TemplateLabel, labelX, y, tab++);
        _txtTemplate = FormKit.TextBox("_txtTemplate", lblTemplate, new Rectangle(fieldX, y, fieldW, 25), tab++);
        y += row - 6;
        var lblTemplateHint = FormKit.Hint("_lblTemplateHint", Strings.MsgRule_TemplateHint, new Rectangle(fieldX, y, fieldW, 40), tab++);
        y += 46;

        var lblChannel = FormKit.Label("_lblChannel", Strings.MsgRule_ChannelLabel, labelX, y, tab++);
        _txtChannel = FormKit.TextBox("_txtChannel", lblChannel, new Rectangle(fieldX, y, 200, 25), tab++);
        y += row;

        _chkCase = FormKit.CheckBox("_chkCase", Strings.MsgRule_CaseSensitive, fieldX, y, tab++);
        y += row - 4;
        _chkEnabled = FormKit.CheckBox("_chkEnabled", Strings.MsgRule_Enabled, fieldX, y, tab++);
        y += row + 4;

        var lblSample = FormKit.Label("_lblSample", Strings.MsgRule_SampleLabel, labelX, y, tab++);
        _txtSample = FormKit.TextBox("_txtSample", lblSample, new Rectangle(fieldX, y, fieldW - 110, 25), tab++);
        _btnTest = FormKit.Button("_btnTest", Strings.MsgRule_TestButton, new Rectangle(fieldX + fieldW - 104, y - 2, 104, 29), tab++, () => RunTest());
        y += row;

        var lblResult = FormKit.Label("_lblResult", Strings.MsgRule_ResultLabel, labelX, y, tab++);
        _txtResult = FormKit.TextBox("_txtResult", lblResult, new Rectangle(fieldX, y, fieldW, 64), tab++);
        _txtResult.Multiline = true;
        _txtResult.ReadOnly = true;
        _txtResult.ScrollBars = ScrollBars.Vertical;
        y += 76;

        _btnOk = FormKit.Button("_btnOk", Strings.Common_OKButton, new Rectangle(fieldX + fieldW - 216, y, 104, 30), tab++, () => Accept());
        var btnCancel = FormKit.Button("_btnCancel", Strings.Common_CancelButton, new Rectangle(fieldX + fieldW - 104, y, 104, 30), tab);
        btnCancel.DialogResult = DialogResult.Cancel;
        ClientSize = new Size(640, y + 42);

        Controls.AddRange([
            lblPattern, _txtPattern, lblTemplate, _txtTemplate, lblTemplateHint, lblChannel, _txtChannel, _chkCase, _chkEnabled,
            lblSample, _txtSample, _btnTest, lblResult, _txtResult, _btnOk, btnCancel]);
        AcceptButton = _btnOk;
        CancelButton = btnCancel;

        // Enter in the sample box tests instead of closing the dialog.
        _txtSample.Enter += (_, _) => AcceptButton = _btnTest;
        _txtSample.Leave += (_, _) => AcceptButton = _btnOk;

        _txtPattern.Text = model.Pattern;
        _txtTemplate.Text = model.Template;
        _txtChannel.Text = model.Channel;
        _chkCase.Checked = model.CaseSensitive;
        _chkEnabled.Checked = model.Enabled;
        foreach (var box in new[] { _txtPattern, _txtTemplate, _txtChannel })
            box.SelectAll();
    }

    private void ControlsToModel()
    {
        _model.Pattern = _txtPattern.Text;
        _model.Template = _txtTemplate.Text;
        _model.Channel = _txtChannel.Text;
        _model.CaseSensitive = _chkCase.Checked;
        _model.Enabled = _chkEnabled.Checked;
    }

    internal bool Accept()
    {
        ControlsToModel();
        if (_model.Validate() is { } error)
        {
            _prompts.Warn(error.Message, Text);
            FormKit.FocusField(this, error.Field == MessageRuleField.Template ? _txtTemplate : _txtPattern);
            return false;
        }

        DialogResult = DialogResult.OK;
        return true;
    }

    /// <summary>Shows the outcome in the result box and moves the focus there, so the screen reader reads it.</summary>
    internal void RunTest()
    {
        ControlsToModel();
        _txtResult.Text = _model.Test(_txtSample.Text).Text;
        FormKit.FocusField(this, _txtResult);
    }
}
