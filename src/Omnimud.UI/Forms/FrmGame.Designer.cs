using Omnimud.UI.Controls;

namespace Omnimud.UI.Forms;

partial class FrmGame
{
    private System.ComponentModel.IContainer components = null;

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _menu = new MenuStrip();
        _layout = new TableLayoutPanel();
        _lblMessages = new Label();
        _rtbMessages = new AnsiTerminalBox();
        _lblOutput = new Label();
        _terminal = new AnsiTerminalBox();
        _lblInput = new Label();
        _txtInput = new TextBox();
        _buttons = new FlowLayoutPanel();
        _btnAction = new Button();
        _btnReconnect = new Button();
        _status = new StatusStrip();
        _stlConnection = new ToolStripStatusLabel();
        _stlTime = new ToolStripStatusLabel();
        _stlMessages = new ToolStripStatusLabel();
        _tmrSecond = new System.Windows.Forms.Timer(components);
        _tmrMessageStatus = new System.Windows.Forms.Timer(components);
        _layout.SuspendLayout();
        _buttons.SuspendLayout();
        _status.SuspendLayout();
        SuspendLayout();
        //
        // _menu
        //
        _menu.Name = "_menu";
        _menu.TabIndex = 100;
        //
        // _layout — visual order: Messages, Received, Text to send, buttons.
        // Tab order is independent: Text to send → Received → Messages → buttons.
        //
        _layout.ColumnCount = 1;
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _layout.Controls.Add(_lblMessages, 0, 0);
        _layout.Controls.Add(_rtbMessages, 0, 1);
        _layout.Controls.Add(_lblOutput, 0, 2);
        _layout.Controls.Add(_terminal, 0, 3);
        _layout.Controls.Add(_lblInput, 0, 4);
        _layout.Controls.Add(_txtInput, 0, 5);
        _layout.Controls.Add(_buttons, 0, 6);
        _layout.Dock = DockStyle.Fill;
        _layout.Name = "_layout";
        _layout.Padding = new Padding(8, 4, 8, 4);
        _layout.RowCount = 7;
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 30F));
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 70F));
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76F));
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layout.TabIndex = 0;
        //
        // _lblInput
        //
        _lblInput.AutoSize = true;
        _lblInput.Margin = new Padding(3, 6, 3, 2);
        _lblInput.Name = "_lblInput";
        _lblInput.TabIndex = 0;
        //
        // _txtInput
        //
        _txtInput.AcceptsReturn = false;
        _txtInput.Dock = DockStyle.Fill;
        _txtInput.Multiline = true;
        _txtInput.Name = "_txtInput";
        _txtInput.ScrollBars = ScrollBars.Vertical;
        _txtInput.TabIndex = 1;
        _txtInput.KeyDown += TxtInput_KeyDown;
        _txtInput.TextChanged += TxtInput_TextChanged;
        //
        // _lblOutput
        //
        _lblOutput.AutoSize = true;
        _lblOutput.Margin = new Padding(3, 6, 3, 2);
        _lblOutput.Name = "_lblOutput";
        _lblOutput.TabIndex = 2;
        //
        // _terminal
        //
        _terminal.Dock = DockStyle.Fill;
        _terminal.Name = "_terminal";
        _terminal.TabIndex = 3;
        _terminal.KeyDown += ReadOnlyBox_KeyDown;
        _terminal.KeyPress += ReadOnlyBox_KeyPress;
        _terminal.LinkClicked += ReadOnlyBox_LinkClicked;
        //
        // _lblMessages
        //
        _lblMessages.AutoSize = true;
        _lblMessages.Margin = new Padding(3, 2, 3, 2);
        _lblMessages.Name = "_lblMessages";
        _lblMessages.TabIndex = 4;
        //
        // _rtbMessages
        //
        _rtbMessages.Dock = DockStyle.Fill;
        _rtbMessages.Name = "_rtbMessages";
        _rtbMessages.TabIndex = 5;
        _rtbMessages.KeyDown += ReadOnlyBox_KeyDown;
        _rtbMessages.KeyPress += ReadOnlyBox_KeyPress;
        _rtbMessages.LinkClicked += ReadOnlyBox_LinkClicked;
        _rtbMessages.SelectionChanged += RtbMessages_SelectionChanged;
        //
        // _buttons
        //
        _buttons.AutoSize = true;
        _buttons.Controls.Add(_btnAction);
        _buttons.Controls.Add(_btnReconnect);
        _buttons.Dock = DockStyle.Fill;
        _buttons.Name = "_buttons";
        _buttons.TabIndex = 6;
        //
        // _btnAction
        //
        _btnAction.AutoSize = true;
        _btnAction.MinimumSize = new Size(110, 30);
        _btnAction.Name = "_btnAction";
        _btnAction.TabIndex = 0;
        _btnAction.Click += BtnAction_Click;
        //
        // _btnReconnect
        //
        _btnReconnect.AutoSize = true;
        _btnReconnect.MinimumSize = new Size(110, 30);
        _btnReconnect.Name = "_btnReconnect";
        _btnReconnect.TabIndex = 1;
        _btnReconnect.Visible = false;
        _btnReconnect.Click += BtnReconnect_Click;
        //
        // _status
        //
        _status.Items.AddRange(new ToolStripItem[] { _stlConnection, _stlTime, _stlMessages });
        _status.Name = "_status";
        _status.ShowItemToolTips = false;
        _status.TabIndex = 101;
        _stlConnection.Name = "_stlConnection";
        _stlConnection.Spring = true;
        _stlConnection.TextAlign = ContentAlignment.MiddleLeft;
        _stlTime.Name = "_stlTime";
        _stlTime.BorderSides = ToolStripStatusLabelBorderSides.Left;
        _stlMessages.Name = "_stlMessages";
        _stlMessages.BorderSides = ToolStripStatusLabelBorderSides.Left;
        //
        // timers
        //
        _tmrSecond.Interval = 1000;
        _tmrSecond.Tick += TmrSecond_Tick;
        _tmrMessageStatus.Interval = 2000;
        _tmrMessageStatus.Tick += TmrMessageStatus_Tick;
        //
        // FrmGame
        //
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(900, 680);
        Controls.Add(_layout);
        Controls.Add(_status);
        Controls.Add(_menu);
        Font = new Font("Segoe UI", 9.75F);
        KeyPreview = true;
        MainMenuStrip = _menu;
        MinimumSize = new Size(520, 420);
        Name = "FrmGame";
        StartPosition = FormStartPosition.CenterScreen;
        _layout.ResumeLayout(false);
        _layout.PerformLayout();
        _buttons.ResumeLayout(false);
        _buttons.PerformLayout();
        _status.ResumeLayout(false);
        _status.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    private MenuStrip _menu;
    private TableLayoutPanel _layout;
    private Label _lblMessages;
    private AnsiTerminalBox _rtbMessages;
    private Label _lblOutput;
    private AnsiTerminalBox _terminal;
    private Label _lblInput;
    private TextBox _txtInput;
    private FlowLayoutPanel _buttons;
    private Button _btnAction;
    private Button _btnReconnect;
    private StatusStrip _status;
    private ToolStripStatusLabel _stlConnection;
    private ToolStripStatusLabel _stlTime;
    private ToolStripStatusLabel _stlMessages;
    private System.Windows.Forms.Timer _tmrSecond;
    private System.Windows.Forms.Timer _tmrMessageStatus;
}
