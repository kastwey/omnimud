using System.Text.RegularExpressions;
using Omnimud.Core.Options;
using Omnimud.Core.Security;
using Omnimud.Data.Exchange;
using Omnimud.Data.Options;
using Omnimud.Data.Repositories;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;

namespace Omnimud.UI.Forms;

/// <summary>
/// Options of one scope (global, a MUD or a character). Six tabs and ONE set of buttons outside them.
/// No logic here: <see cref="OptionsEditorModel"/> loads, validates, converts and saves; this class only
/// moves values between the model and the controls.
/// </summary>
public sealed partial class FrmOptions : Form
{
    // Order of the items of each combo. Every value of the enum must be here (there is a test for it).
    internal static readonly ScreenReaderMode[] ScreenReaderItems =
        [ScreenReaderMode.Automatic, ScreenReaderMode.Jaws, ScreenReaderMode.Nvda, ScreenReaderMode.None];
    internal static readonly CursorBehavior[] CursorItems =
        [CursorBehavior.GoToEnd, CursorBehavior.Keep, CursorBehavior.FollowIfAtEnd];
    internal static readonly LogMode[] LogItems = [LogMode.None, LogMode.PerDay, LogMode.PerSession];
    internal static readonly ProxyMode[] ProxyItems = [ProxyMode.Disabled, ProxyMode.Automatic, ProxyMode.Manual];
    internal static readonly ProxyProtocol[] ProtocolItems = [ProxyProtocol.Socks5, ProxyProtocol.HttpConnect];
    internal static readonly string[] LanguageItems = [LanguageService.Automatic, LanguageService.Spanish, LanguageService.English];

    private readonly OptionsEditorModel _model;
    private readonly IUserPrompts _prompts;
    private readonly IFontPrompt _fontPrompt;
    private readonly Dictionary<OptionsField, Control> _fieldControls = [];
    private readonly List<TableLayoutPanel> _pages = [];
    private Task? _initialization;
    private bool _binding;
    private bool _busy;
    private Font? _previewFont;
    private readonly List<Font> _retiredFonts = [];

    private CheckBox? _chkInherit;
    private TabControl _tabs = null!;
    private Button _btnOk = null!, _btnCancel = null!, _btnExport = null!, _btnImport = null!;

    // General
    private ComboBox _cboScreenReader = null!, _cboCursorReceived = null!, _cboCursorMessages = null!, _cboLanguage = null!;
    private NumericUpDown _nudHistorySize = null!, _nudMaxLines = null!, _nudPromptFlush = null!;
    private CheckBox _chkConfirmExit = null!, _chkTrySave = null!, _chkTelnet = null!, _chkAnnounceMudText = null!,
        _chkAnnounceMessages = null!, _chkFlashWindow = null!, _chkCheckUpdates = null!;
    // Logs
    private ComboBox _cboLogType = null!;
    private TextBox _txtLogDirectory = null!;
    private Button _btnBrowse = null!;
    // Sounds
    private CheckBox _chkEnableSounds = null!, _chkEnableMusic = null!, _chkSoundsBackground = null!,
        _chkMusicBackground = null!, _chkDownloadSounds = null!, _chkAllowHttp = null!;
    private NumericUpDown _nudVolume = null!;
    // Connection
    private ComboBox _cboProxy = null!, _cboProxyProtocol = null!;
    private TextBox _txtProxyHost = null!, _txtProxyUser = null!, _txtProxyPassword = null!;
    private NumericUpDown _nudProxyPort = null!;
    private CheckBox _chkProxyForMud = null!, _chkClearProxyPassword = null!;
    // The stored proxy password, protected and opaque: carried from ShowFields to ReadFields, never shown.
    private string? _storedProxyPassword;
    // Appearance
    private ComboBox _cboFont = null!;
    private NumericUpDown _nudFontSize = null!;
    private Button _btnChangeFont = null!;
    private TextBox _txtPreview = null!;
    // Special characters
    private CheckBox _chkUseConcat = null!, _chkUseRepeat = null!;
    private TextBox _txtConcatChar = null!, _txtRepeatChar = null!;

    /// <param name="scopeId">Id of the MUD or character; null for Global.</param>
    /// <param name="scopeName">Name of the MUD or character, shown in the title.</param>
    /// <param name="parentMudId">Character scope only: the MUD of the character (what it inherits from).</param>
    /// <param name="exchange">Used to read and write the exported .omnimud files; optional.</param>
    /// <param name="credentials">Protects the proxy password; without it the dialog cannot take a new one.</param>
    public FrmOptions(IOptionsService service, OptionScope scope, int? scopeId, string scopeName, IUserPrompts prompts,
        int? parentMudId = null, IExchangeService? exchange = null, IFontPrompt? fontPrompt = null,
        IProxyCredentialStore? credentials = null)
        : this(new OptionsEditorModel(service, scope, scopeId, scopeName, parentMudId, new ExchangeOptionsFileStore(exchange),
                credentials: credentials),
            prompts, fontPrompt)
    {
    }

    /// <summary>Compatible with the previous dialog: global options straight over the repository.</summary>
    public FrmOptions(IOptionRepository optionRepo)
        : this(new OptionsEditorModel(new OptionsService(optionRepo), OptionScope.Global, null, null), null, null)
    {
    }

    public FrmOptions(OptionsEditorModel model, IUserPrompts? prompts, IFontPrompt? fontPrompt = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        _prompts = prompts ?? new WinFormsUserPrompts(this);
        _fontPrompt = fontPrompt ?? new WinFormsFontPrompt(this);
        BuildForm();
        ShowFields(_model.Fields);
    }

    // ═══════════════════════════════ Model ↔ controls ═══════════════════════════════

    /// <summary>Loads the model and fills the controls. Runs once; OnLoad calls it if nobody did before.</summary>
    internal Task InitializeAsync() => _initialization ??= InitializeCoreAsync();

    private async Task InitializeCoreAsync()
    {
        await _model.LoadAsync();
        if (IsDisposed) return;
        _binding = true;
        try
        {
            if (_chkInherit is not null) _chkInherit.Checked = _model.UseInherited;
        }
        finally
        {
            _binding = false;
        }
        ShowFields(_model.Fields);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ErrorReporter.Run(this, InitializeAsync);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_chkInherit is not null) _chkInherit.Select();
        else _tabs.Select();
    }

    internal void ShowFields(OptionsFields f)
    {
        _binding = true;
        try
        {
            SelectValue(_cboScreenReader, ScreenReaderItems, f.ScreenReader);
            SelectValue(_cboCursorReceived, CursorItems, f.CursorOnReceived);
            SelectValue(_cboCursorMessages, CursorItems, f.CursorOnMessages);
            SetNumber(_nudHistorySize, f.HistorySize);
            _chkConfirmExit.Checked = f.ConfirmBeforeExit;
            _chkTrySave.Checked = f.TrySaveBeforeExit;
            _chkTelnet.Checked = f.TelnetNegotiation;
            var language = Array.FindIndex(LanguageItems, l => l.Equals(f.Language?.Trim(), StringComparison.OrdinalIgnoreCase));
            _cboLanguage.SelectedIndex = Math.Max(language, 0);
            _chkAnnounceMudText.Checked = f.AnnounceMudText;
            _chkAnnounceMessages.Checked = f.AnnounceMessages;
            _chkFlashWindow.Checked = f.FlashWindow;
            SetNumber(_nudMaxLines, f.MaxLines);
            SetNumber(_nudPromptFlush, f.PromptFlushMilliseconds);
            _chkCheckUpdates.Checked = f.CheckUpdatesOnStartup;

            SelectValue(_cboLogType, LogItems, f.LogType);
            _txtLogDirectory.Text = f.LogDirectory;

            _chkEnableSounds.Checked = f.EnableSounds;
            _chkEnableMusic.Checked = f.EnableMusic;
            _chkSoundsBackground.Checked = f.PlaySoundsInBackground;
            _chkMusicBackground.Checked = f.PlayMusicInBackground;
            _chkDownloadSounds.Checked = f.DownloadSounds;
            _chkAllowHttp.Checked = f.AllowHttpDownloads;
            SetNumber(_nudVolume, f.Volume);

            SelectValue(_cboProxy, ProxyItems, f.ProxyType);
            _txtProxyHost.Text = f.ProxyHost;
            SetNumber(_nudProxyPort, f.ProxyPort);
            _chkProxyForMud.Checked = f.UseProxyForMud;
            SelectValue(_cboProxyProtocol, ProtocolItems, f.ProxyProtocol);
            _txtProxyUser.Text = f.ProxyUsername;
            _txtProxyPassword.Text = f.NewProxyPassword;
            _chkClearProxyPassword.Checked = f.ClearProxyPassword;
            _storedProxyPassword = f.ProxyPasswordProtected;

            SelectFont(f.FontFamily);
            SetNumber(_nudFontSize, (decimal)Math.Round(f.FontSize, 2));

            _chkUseConcat.Checked = f.UseConcatChar;
            _txtConcatChar.Text = f.ConcatChar;
            _chkUseRepeat.Checked = f.UseRepeatChar;
            _txtRepeatChar.Text = f.RepeatChar;
        }
        finally
        {
            _binding = false;
        }
        UpdateEnabledState();
        UpdatePreview();
    }

    internal OptionsFields ReadFields() => new()
    {
        ScreenReader = Selected(_cboScreenReader, ScreenReaderItems),
        CursorOnReceived = Selected(_cboCursorReceived, CursorItems),
        CursorOnMessages = Selected(_cboCursorMessages, CursorItems),
        HistorySize = (int)_nudHistorySize.Value,
        ConfirmBeforeExit = _chkConfirmExit.Checked,
        TrySaveBeforeExit = _chkTrySave.Checked,
        TelnetNegotiation = _chkTelnet.Checked,
        Language = Selected(_cboLanguage, LanguageItems),
        AnnounceMudText = _chkAnnounceMudText.Checked,
        AnnounceMessages = _chkAnnounceMessages.Checked,
        FlashWindow = _chkFlashWindow.Checked,
        MaxLines = (int)_nudMaxLines.Value,
        PromptFlushMilliseconds = (int)_nudPromptFlush.Value,
        CheckUpdatesOnStartup = _chkCheckUpdates.Checked,
        LogType = Selected(_cboLogType, LogItems),
        LogDirectory = _txtLogDirectory.Text,
        EnableSounds = _chkEnableSounds.Checked,
        EnableMusic = _chkEnableMusic.Checked,
        PlaySoundsInBackground = _chkSoundsBackground.Checked,
        PlayMusicInBackground = _chkMusicBackground.Checked,
        DownloadSounds = _chkDownloadSounds.Checked,
        AllowHttpDownloads = _chkAllowHttp.Checked,
        Volume = (int)_nudVolume.Value,
        ProxyType = Selected(_cboProxy, ProxyItems),
        ProxyHost = _txtProxyHost.Text,
        ProxyPort = (int)_nudProxyPort.Value,
        UseProxyForMud = _chkProxyForMud.Checked,
        ProxyProtocol = Selected(_cboProxyProtocol, ProtocolItems),
        ProxyUsername = _txtProxyUser.Text,
        NewProxyPassword = _txtProxyPassword.Text,
        ClearProxyPassword = _chkClearProxyPassword.Checked,
        ProxyPasswordProtected = _storedProxyPassword,
        FontFamily = _cboFont.SelectedItem as string ?? string.Empty,
        FontSize = (float)_nudFontSize.Value,
        UseConcatChar = _chkUseConcat.Checked,
        ConcatChar = _txtConcatChar.Text,
        UseRepeatChar = _chkUseRepeat.Checked,
        RepeatChar = _txtRepeatChar.Text
    };

    /// <summary>Inheriting disables every page; otherwise each dependent control follows its master.</summary>
    private void UpdateEnabledState()
    {
        if (_binding) return;
        var editable = !(_model.CanInherit && _model.UseInherited);
        foreach (var page in _pages)
            page.Enabled = editable;

        var f = ReadFields();
        _cboLanguage.Enabled = _model.CanEditLanguage;
        _chkCheckUpdates.Enabled = _model.CanEditApplicationOptions;
        _txtLogDirectory.Enabled = _btnBrowse.Enabled = f.LogDirectoryEnabled;
        _chkSoundsBackground.Enabled = f.SoundsBackgroundEnabled;
        _chkMusicBackground.Enabled = f.MusicBackgroundEnabled;
        _chkAllowHttp.Enabled = f.AllowHttpEnabled;
        _txtProxyHost.Enabled = _nudProxyPort.Enabled = _cboProxyProtocol.Enabled = f.ProxyDetailsEnabled;
        _txtProxyUser.Enabled = _txtProxyPassword.Enabled = f.ProxyCredentialsEnabled;
        _chkClearProxyPassword.Enabled = f.ClearProxyPasswordEnabled;
        _chkProxyForMud.Enabled = f.ProxyForMudEnabled;
        _txtConcatChar.Enabled = f.ConcatCharEnabled;
        _txtRepeatChar.Enabled = f.RepeatCharEnabled;
    }

    private void UpdatePreview()
    {
        if (_binding) return;
        var family = _cboFont.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(family)) return;
        try
        {
            var size = (float)_nudFontSize.Value;
            if (_previewFont is not null && _previewFont.Size == size
                && string.Equals(_previewFont.OriginalFontName, family, StringComparison.OrdinalIgnoreCase))
                return;

            var previous = _previewFont;
            _previewFont = new Font(family, size);
            _txtPreview.Font = _previewFont;
            // The old font is released only once the box no longer uses it, and never while its window is being created.
            if (previous is not null && !ReferenceEquals(previous, _txtPreview.Font))
                _retiredFonts.Add(previous);
        }
        catch (ArgumentException)
        {
            // A family that cannot be instantiated (some fonts lack the regular style): keep the last preview.
        }
    }

    // ═══════════════════════════════ Actions ═══════════════════════════════

    private void OnInheritChanged(object? sender, EventArgs e)
    {
        if (_binding || _chkInherit is null) return;
        ShowFields(_model.SetUseInherited(_chkInherit.Checked, ReadFields()));
    }

    internal async Task AcceptAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            await InitializeAsync();
            var result = await _model.AcceptAsync(ReadFields(), _prompts);
            if (IsDisposed) return;
            if (result.Saved)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
            else if (result.FocusField is { } field)
            {
                FocusField(field);
            }
        }
        finally
        {
            _busy = false;
        }
    }

    internal async Task ExportAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            await InitializeAsync();
            if (await _model.ExportAsync(ReadFields(), _prompts) is { } field && !IsDisposed)
                FocusField(field);
        }
        finally
        {
            _busy = false;
        }
    }

    internal async Task ImportAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            await InitializeAsync();
            var imported = await _model.ImportAsync(ReadFields(), _prompts);
            if (imported is null || IsDisposed) return;

            _binding = true;
            try
            {
                if (_chkInherit is not null) _chkInherit.Checked = _model.UseInherited;
            }
            finally
            {
                _binding = false;
            }
            ShowFields(imported);
        }
        finally
        {
            _busy = false;
        }
    }

    private void BrowseLogDirectory()
    {
        var folder = _prompts.PickFolder(Strings.Options_BrowseTitle, _txtLogDirectory.Text.Trim());
        if (folder is null) return;
        _txtLogDirectory.Text = folder;
        _txtLogDirectory.Select();
        _txtLogDirectory.SelectAll();
    }

    private void ChangeFont()
    {
        var current = new FontChoice(_cboFont.SelectedItem as string ?? string.Empty, (float)_nudFontSize.Value);
        if (_fontPrompt.PickFont(current) is not { } choice) return;
        SelectFont(choice.Family);
        SetNumber(_nudFontSize, (decimal)Math.Round(choice.Size, 2));
        UpdatePreview();
    }

    /// <summary>Shows the tab of the field and gives it the focus, with its text selected.</summary>
    internal void FocusField(OptionsField field)
    {
        if (!_fieldControls.TryGetValue(field, out var control)) return;
        for (Control? parent = control.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is TabPage page)
            {
                _tabs.SelectedTab = page;
                break;
            }
        }
        control.Select();
        if (control is TextBoxBase box) box.SelectAll();
        else if (control is NumericUpDown number) number.Select(0, number.Text.Length);
    }

    // ═══════════════════════════════ Construction ═══════════════════════════════

    private void BuildForm()
    {
        SuspendLayout();
        Name = "FrmOptions";
        Text = _model.Title;
        Font = new Font("Segoe UI", 9.75F);
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(720, 540);
        MinimumSize = new Size(560, 440);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var root = new TableLayoutPanel
        {
            Name = "_root",
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        if (_model.CanInherit)
        {
            _chkInherit = new CheckBox
            {
                Name = "_chkInherit",
                Text = _model.InheritText,
                AutoSize = true,
                TabIndex = 0,
                Margin = new Padding(3, 3, 3, 8),
                Checked = _model.UseInherited
            };
            _chkInherit.CheckedChanged += OnInheritChanged;
            root.Controls.Add(_chkInherit, 0, 0);
        }

        _tabs = new TabControl { Name = "_tabs", Dock = DockStyle.Fill, TabIndex = 1, AccessibleName = Strings.Options_Tabs };
        _tabs.TabPages.Add(BuildGeneralPage());
        _tabs.TabPages.Add(BuildLogsPage());
        _tabs.TabPages.Add(BuildSoundsPage());
        _tabs.TabPages.Add(BuildConnectionPage());
        _tabs.TabPages.Add(BuildAppearancePage());
        _tabs.TabPages.Add(BuildSpecialCharsPage());
        root.Controls.Add(_tabs, 0, 1);

        // Visual and tab order: OK, Cancel, Export, Import, aligned to the right.
        var buttons = new FlowLayoutPanel
        {
            Name = "_buttons",
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            TabIndex = 2,
            Margin = new Padding(0, 8, 0, 0)
        };
        _btnOk = NewButton("_btnOk", Strings.Common_OKButton, 0);
        _btnCancel = NewButton("_btnCancel", Strings.Common_CancelButton, 1);
        _btnCancel.DialogResult = DialogResult.Cancel;
        _btnExport = NewButton("_btnExport", Strings.Options_Export, 2);
        _btnImport = NewButton("_btnImport", Strings.Options_Import, 3);
        buttons.Controls.AddRange([_btnImport, _btnExport, _btnCancel, _btnOk]);
        root.Controls.Add(buttons, 0, 2);

        _btnOk.Click += (_, _) => ErrorReporter.Run(this, AcceptAsync);
        _btnCancel.Click += (_, _) => Close();
        _btnExport.Click += (_, _) => ErrorReporter.Run(this, ExportAsync);
        _btnImport.Click += (_, _) => ErrorReporter.Run(this, ImportAsync);

        Controls.Add(root);
        AcceptButton = _btnOk;
        CancelButton = _btnCancel;
        ResumeLayout(performLayout: true);
    }

    private TabPage BuildGeneralPage()
    {
        var (page, table) = NewPage("_tabGeneral", Strings.Options_TabGeneral);
        _cboScreenReader = AddCombo(table, "ScreenReader", Strings.Options_ScreenReader, OptionsField.ScreenReader,
            Strings.Options_SRAutomatic, Strings.Options_SRJaws, Strings.Options_SRNvda, Strings.Options_SRNone);
        _cboCursorReceived = AddCombo(table, "CursorReceived", Strings.Options_CursorReceived, OptionsField.CursorOnReceived,
            Strings.Options_CursorGoToEnd, Strings.Options_CursorKeep, Strings.Options_CursorFollow);
        _cboCursorMessages = AddCombo(table, "CursorMessages", Strings.Options_CursorMessages, OptionsField.CursorOnMessages,
            Strings.Options_CursorGoToEnd, Strings.Options_CursorKeep, Strings.Options_CursorFollow);
        _nudHistorySize = AddNumber(table, "HistorySize", Strings.Options_HistorySize, OptionsField.HistorySize, 1, 10_000, 10);
        _cboLanguage = AddCombo(table, "Language", Strings.Options_Language, OptionsField.Language,
            Strings.Options_LangAuto, Strings.Options_LangSpanish, Strings.Options_LangEnglish);
        _nudMaxLines = AddNumber(table, "MaxLines", Strings.Options_MaxLines, OptionsField.MaxLines, 100, 1_000_000, 1000);
        _nudPromptFlush = AddNumber(table, "PromptFlush", Strings.Options_PromptFlush, OptionsField.PromptFlushMilliseconds, 0, 60_000, 50);
        _chkConfirmExit = AddCheck(table, "_chkConfirmExit", Strings.Options_ConfirmExit, OptionsField.ConfirmBeforeExit);
        _chkTrySave = AddCheck(table, "_chkTrySave", Strings.Options_TrySave, OptionsField.TrySaveBeforeExit);
        _chkTelnet = AddCheck(table, "_chkTelnet", Strings.Options_Telnet, OptionsField.TelnetNegotiation);
        _chkAnnounceMudText = AddCheck(table, "_chkAnnounceMudText", Strings.Options_AnnounceMudText, OptionsField.AnnounceMudText);
        _chkAnnounceMessages = AddCheck(table, "_chkAnnounceMessages", Strings.Options_AnnounceMessages, OptionsField.AnnounceMessages);
        _chkFlashWindow = AddCheck(table, "_chkFlashWindow", Strings.Options_FlashWindow, OptionsField.FlashWindow);
        // Application-wide, like the language: only enabled in the global options. Same switch as Tools > "Check for updates on startup" of the launcher.
        _chkCheckUpdates = AddCheck(table, "_chkCheckUpdates", Strings.Options_CheckUpdates, OptionsField.CheckUpdatesOnStartup);
        return page;
    }

    private TabPage BuildLogsPage()
    {
        var (page, table) = NewPage("_tabLogs", Strings.Options_TabLogs);
        _cboLogType = AddCombo(table, "LogType", Strings.Options_LogType, OptionsField.LogType,
            Strings.Options_LogNone, Strings.Options_LogPerDay, Strings.Options_LogPerSession);
        _cboLogType.SelectedIndexChanged += (_, _) => UpdateEnabledState();
        _txtLogDirectory = AddText(table, "LogDirectory", Strings.Options_LogDirectory, OptionsField.LogDirectory, 260);
        _btnBrowse = AddButton(table, "_btnBrowse", Strings.Options_Browse);
        _btnBrowse.Click += (_, _) => BrowseLogDirectory();
        return page;
    }

    private TabPage BuildSoundsPage()
    {
        var (page, table) = NewPage("_tabSounds", Strings.Options_TabSounds);
        _chkEnableSounds = AddCheck(table, "_chkEnableSounds", Strings.Options_EnableSounds, OptionsField.EnableSounds);
        _chkSoundsBackground = AddCheck(table, "_chkSoundsBackground", Strings.Options_SoundsBackground, OptionsField.PlaySoundsInBackground);
        _chkEnableMusic = AddCheck(table, "_chkEnableMusic", Strings.Options_EnableMusic, OptionsField.EnableMusic);
        _chkMusicBackground = AddCheck(table, "_chkMusicBackground", Strings.Options_MusicBackground, OptionsField.PlayMusicInBackground);
        _chkDownloadSounds = AddCheck(table, "_chkDownloadSounds", Strings.Options_DownloadSounds, OptionsField.DownloadSounds);
        _chkAllowHttp = AddCheck(table, "_chkAllowHttp", Strings.Options_AllowHttp, OptionsField.AllowHttpDownloads);
        _nudVolume = AddNumber(table, "Volume", Strings.Options_Volume, OptionsField.Volume, 0, 100, 5);
        _chkEnableSounds.CheckedChanged += (_, _) => UpdateEnabledState();
        _chkEnableMusic.CheckedChanged += (_, _) => UpdateEnabledState();
        _chkDownloadSounds.CheckedChanged += (_, _) => UpdateEnabledState();
        return page;
    }

    private TabPage BuildConnectionPage()
    {
        var (page, table) = NewPage("_tabConnection", Strings.Options_TabConnection);
        _cboProxy = AddCombo(table, "Proxy", Strings.Options_Proxy, OptionsField.ProxyType,
            Strings.Options_ProxyDisabled, Strings.Options_ProxyAutomatic, Strings.Options_ProxyManual);
        _cboProxy.SelectedIndexChanged += (_, _) => UpdateEnabledState();
        _txtProxyHost = AddText(table, "ProxyHost", Strings.Options_ProxyHost, OptionsField.ProxyHost, 255);
        _nudProxyPort = AddNumber(table, "ProxyPort", Strings.Options_ProxyPort, OptionsField.ProxyPort, 0, 65_535, 1);
        _cboProxyProtocol = AddCombo(table, "ProxyProtocol", Strings.Options_ProxyProtocol, OptionsField.ProxyProtocol,
            Strings.Options_ProtocolSocks5, Strings.Options_ProtocolHttp);
        _txtProxyUser = AddText(table, "ProxyUser", Strings.Options_ProxyUsername, OptionsField.ProxyUsername, 255);
        // Always starts empty: the stored password is never shown, not even as dots. Empty = keep it.
        _txtProxyPassword = AddText(table, "ProxyPassword", Strings.Options_ProxyPassword, OptionsField.ProxyPasswordProtected, 255);
        _txtProxyPassword.UseSystemPasswordChar = true;
        _chkClearProxyPassword = AddCheck(table, "_chkClearProxyPassword", Strings.Options_ClearProxyPassword, OptionsField.ClearProxyPassword);
        _chkProxyForMud = AddCheck(table, "_chkProxyForMud", Strings.Options_ProxyForMud, OptionsField.UseProxyForMud);
        return page;
    }

    private TabPage BuildAppearancePage()
    {
        var (page, table) = NewPage("_tabAppearance", Strings.Options_TabAppearance);
        _cboFont = AddCombo(table, "Font", Strings.Options_Font, OptionsField.FontFamily, InstalledFontNames());
        _nudFontSize = AddNumber(table, "FontSize", Strings.Options_FontSizePoints, OptionsField.FontSize, 4, 200, 1);
        _nudFontSize.DecimalPlaces = 2; // the font dialog produces sizes such as 9.75
        _btnChangeFont = AddButton(table, "_btnChangeFont", Strings.Options_ChangeFont);
        _btnChangeFont.Click += (_, _) => ChangeFont();

        // A plain TextBox on purpose: RichTextBox misbehaves with screen readers in modern .NET.
        _txtPreview = AddText(table, "Preview", Strings.Options_Preview, field: null, maxLength: 0);
        _txtPreview.Multiline = true;
        _txtPreview.ReadOnly = true;
        _txtPreview.ScrollBars = ScrollBars.Vertical;
        _txtPreview.Height = 110;
        _txtPreview.Text = Strings.Options_PreviewText;

        _cboFont.SelectedIndexChanged += (_, _) => UpdatePreview();
        _nudFontSize.ValueChanged += (_, _) => UpdatePreview();
        return page;
    }

    private TabPage BuildSpecialCharsPage()
    {
        var (page, table) = NewPage("_tabSpecialChars", Strings.Options_TabSpecialChars);
        _chkUseConcat = AddCheck(table, "_chkUseConcat", Strings.Options_UseConcat, OptionsField.UseConcatChar);
        _txtConcatChar = AddText(table, "ConcatChar", Strings.Options_ConcatChar, OptionsField.ConcatChar, 1);
        _chkUseRepeat = AddCheck(table, "_chkUseRepeat", Strings.Options_UseRepeat, OptionsField.UseRepeatChar);
        _txtRepeatChar = AddText(table, "RepeatChar", Strings.Options_RepeatChar, OptionsField.RepeatChar, 1);
        _txtConcatChar.Anchor = _txtRepeatChar.Anchor = AnchorStyles.Left;
        _txtConcatChar.Width = _txtRepeatChar.Width = 50;
        _chkUseConcat.CheckedChanged += (_, _) => UpdateEnabledState();
        _chkUseRepeat.CheckedChanged += (_, _) => UpdateEnabledState();
        return page;
    }

    // ═══════════════════════════════ Building blocks ═══════════════════════════════

    private (TabPage Page, TableLayoutPanel Table) NewPage(string name, string title)
    {
        var page = new TabPage(title) { Name = name, UseVisualStyleBackColor = true };
        var table = new TableLayoutPanel
        {
            Name = name + "Table",
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 2,
            Padding = new Padding(8),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.Controls.Add(table);
        _pages.Add(table);
        return (page, table);
    }

    /// <summary>Label with mnemonic (TabIndex n) immediately before its control (n+1), both children of the table.</summary>
    private T AddLabeled<T>(TableLayoutPanel table, string name, string labelText, OptionsField? field, T control) where T : Control
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var index = table.Controls.Count;
        var label = new Label
        {
            Name = "_lbl" + name,
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 6, 8, 6),
            TabIndex = index
        };
        control.TabIndex = index + 1;
        control.AccessibleName = PlainText(labelText);
        table.Controls.Add(label, 0, row);
        table.Controls.Add(control, 1, row);
        if (field is { } f) _fieldControls[f] = control;
        return control;
    }

    private ComboBox AddCombo(TableLayoutPanel table, string name, string labelText, OptionsField field, params string[] items)
    {
        var combo = new ComboBox
        {
            Name = "_cbo" + name,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
        };
        combo.Items.AddRange(items);
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        return AddLabeled(table, name, labelText, field, combo);
    }

    private NumericUpDown AddNumber(TableLayoutPanel table, string name, string labelText, OptionsField field,
        int minimum, int maximum, int increment) =>
        AddLabeled(table, name, labelText, field, new NumericUpDown
        {
            Name = "_nud" + name,
            Minimum = minimum,
            Maximum = maximum,
            Increment = increment,
            Width = 110,
            Anchor = AnchorStyles.Left,
        });

    private TextBox AddText(TableLayoutPanel table, string name, string labelText, OptionsField? field, int maxLength)
    {
        var box = new TextBox { Name = "_txt" + name, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        if (maxLength > 0) box.MaxLength = maxLength;
        box.Enter += (_, _) => { if (!box.Multiline) box.SelectAll(); };
        return AddLabeled(table, name, labelText, field, box);
    }

    private CheckBox AddCheck(TableLayoutPanel table, string name, string text, OptionsField field)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        // No mnemonic: the form has more controls than letters; the letters go to the labelled boxes.
        var check = new CheckBox { Name = name, Text = text, AutoSize = true, TabIndex = table.Controls.Count, UseMnemonic = false };
        table.Controls.Add(check, 0, row);
        table.SetColumnSpan(check, 2);
        _fieldControls[field] = check;
        return check;
    }

    private static Button AddButton(TableLayoutPanel table, string name, string text)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var button = new Button
        {
            Name = name,
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(110, 30),
            Anchor = AnchorStyles.Left,
            TabIndex = table.Controls.Count,
            UseVisualStyleBackColor = true
        };
        table.Controls.Add(button, 1, row);
        return button;
    }

    private static Button NewButton(string name, string text, int tabIndex) => new()
    {
        Name = name,
        Text = text,
        AutoSize = true,
        MinimumSize = new Size(100, 32),
        TabIndex = tabIndex,
        UseVisualStyleBackColor = true
    };

    private static string[] InstalledFontNames()
    {
        try
        {
            return System.Drawing.FontFamily.Families.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        catch (Exception)
        {
            return [OmnimudOptions.Default.FontFamily];
        }
    }

    /// <summary>A stored font that is not installed is still listed, so opening the dialog never changes it silently.</summary>
    private void SelectFont(string? family)
    {
        family = family?.Trim();
        if (string.IsNullOrEmpty(family))
        {
            _cboFont.SelectedIndex = -1;
            return;
        }
        var index = _cboFont.FindStringExact(family);
        if (index < 0) index = _cboFont.Items.Add(family);
        _cboFont.SelectedIndex = index;
    }

    private static void SelectValue<T>(ComboBox combo, T[] values, T value) =>
        combo.SelectedIndex = Math.Max(Array.IndexOf(values, value), 0);

    private static T Selected<T>(ComboBox combo, T[] values) =>
        combo.SelectedIndex >= 0 && combo.SelectedIndex < values.Length ? values[combo.SelectedIndex] : values[0];

    private static void SetNumber(NumericUpDown box, decimal value) =>
        box.Value = Math.Clamp(value, box.Minimum, box.Maximum);

    /// <summary>"&amp;Log folder:" → "Log folder"; "Proxy user name (&amp;J):" → "Proxy user name" (a mnemonic letter that
    /// is not in the text, the usual way out when a dialog has run out of letters, is not part of the name).</summary>
    internal static string PlainText(string text)
    {
        text = Regex.Replace(text, @"\s*\(&[^&)]\)", string.Empty);
        var plain = text.Replace("&&", "").Replace("&", string.Empty).Replace("", "&").Trim();
        return plain.TrimEnd(':').TrimEnd();
    }
}
