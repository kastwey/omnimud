namespace Omnimud.UI.Forms;

partial class FrmFind
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _lblFind = new Label();
        _txtFind = new TextBox();
        _chkMatchCase = new CheckBox();
        _grpDirection = new GroupBox();
        _rbDown = new RadioButton();
        _rbUp = new RadioButton();
        _btnFind = new Button();
        _btnCancel = new Button();
        _grpDirection.SuspendLayout();
        SuspendLayout();
        //
        // _lblFind
        //
        _lblFind.AutoSize = true;
        _lblFind.Location = new Point(12, 15);
        _lblFind.Name = "_lblFind";
        _lblFind.TabIndex = 0;
        //
        // _txtFind
        //
        _txtFind.Location = new Point(12, 36);
        _txtFind.MaxLength = 255;
        _txtFind.Name = "_txtFind";
        _txtFind.Size = new Size(360, 25);
        _txtFind.TabIndex = 1;
        _txtFind.TextChanged += TxtFind_TextChanged;
        //
        // _chkMatchCase
        //
        _chkMatchCase.AutoSize = true;
        _chkMatchCase.Location = new Point(12, 72);
        _chkMatchCase.Name = "_chkMatchCase";
        _chkMatchCase.TabIndex = 2;
        //
        // _grpDirection
        //
        _grpDirection.Controls.Add(_rbDown);
        _grpDirection.Controls.Add(_rbUp);
        _grpDirection.Location = new Point(12, 104);
        _grpDirection.Name = "_grpDirection";
        _grpDirection.Size = new Size(360, 58);
        _grpDirection.TabIndex = 3;
        _grpDirection.TabStop = false;
        //
        // _rbDown
        //
        _rbDown.AutoSize = true;
        _rbDown.Location = new Point(14, 24);
        _rbDown.Name = "_rbDown";
        _rbDown.TabIndex = 0;
        _rbDown.TabStop = true;
        //
        // _rbUp
        //
        _rbUp.AutoSize = true;
        _rbUp.Location = new Point(180, 24);
        _rbUp.Name = "_rbUp";
        _rbUp.TabIndex = 1;
        //
        // _btnFind
        //
        _btnFind.DialogResult = DialogResult.OK;
        _btnFind.Location = new Point(176, 176);
        _btnFind.Name = "_btnFind";
        _btnFind.Size = new Size(95, 30);
        _btnFind.TabIndex = 4;
        //
        // _btnCancel
        //
        _btnCancel.DialogResult = DialogResult.Cancel;
        _btnCancel.Location = new Point(277, 176);
        _btnCancel.Name = "_btnCancel";
        _btnCancel.Size = new Size(95, 30);
        _btnCancel.TabIndex = 5;
        //
        // FrmFind
        //
        AcceptButton = _btnFind;
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = _btnCancel;
        ClientSize = new Size(384, 218);
        Controls.Add(_lblFind);
        Controls.Add(_txtFind);
        Controls.Add(_chkMatchCase);
        Controls.Add(_grpDirection);
        Controls.Add(_btnFind);
        Controls.Add(_btnCancel);
        Font = new Font("Segoe UI", 9.75F);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "FrmFind";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        _grpDirection.ResumeLayout(false);
        _grpDirection.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    private Label _lblFind;
    private TextBox _txtFind;
    private CheckBox _chkMatchCase;
    private GroupBox _grpDirection;
    private RadioButton _rbDown;
    private RadioButton _rbUp;
    private Button _btnFind;
    private Button _btnCancel;
}
