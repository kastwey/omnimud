using Omnimud.Core.Reports;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;

namespace Omnimud.UI.Presenters;

public enum ReportField
{
    Description,
    Title,
    Preview
}

/// <summary>Outcome of a button of the report dialog.</summary>
/// <param name="Close">The report went out of Omnimud's hands (browser or mail client opened): the dialog can close.</param>
/// <param name="Focus">Where to put the focus when the dialog stays open.</param>
public sealed record ReportActionResult(bool Close, ReportField? Focus = null)
{
    public static ReportActionResult Done { get; } = new(true);
    public static ReportActionResult Stay(ReportField? focus = null) => new(false, focus);
}

/// <summary>
/// Logic of the report dialog. The user writes a description; the presenter composes the exact text that would be
/// sent (<see cref="Title"/> and <see cref="Preview"/>), which the user can read and edit. "Sending" is only ever
/// opening the browser or the mail program with that text, or copying it: nothing leaves by itself.
/// </summary>
public sealed class ReportPresenter
{
    private readonly ReportBuilder _builder;
    private readonly Func<DiagnosticInfo> _diagnostics;
    private readonly IReadOnlyDictionary<ReportChannel, IReportSender> _senders;
    private readonly IClipboardService _clipboard;
    private readonly IPersonalInfoStore _personalInfo;
    private readonly IUserPrompts _prompts;
    private readonly ExceptionInfo? _exception;

    private ReportKind _kind;
    private string _description = string.Empty;
    private bool _includeDiagnostics = true;
    private string _generatedTitle = string.Empty;
    private string _generatedPreview = string.Empty;

    public ReportPresenter(ReportKind kind, ExceptionInfo? exception, ReportBuilder builder, Func<DiagnosticInfo> diagnostics,
        IEnumerable<IReportSender> senders, IClipboardService clipboard, IPersonalInfoStore personalInfo, IUserPrompts prompts)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(senders);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(personalInfo);
        ArgumentNullException.ThrowIfNull(prompts);
        _kind = exception is not null ? ReportKind.Error : kind;
        _exception = exception;
        _builder = builder;
        _diagnostics = diagnostics;
        _senders = senders.ToDictionary(s => s.Channel);
        _clipboard = clipboard;
        _personalInfo = personalInfo;
        _prompts = prompts;
        Regenerate();
    }

    /// <summary>Raised when <see cref="Title"/> and <see cref="Preview"/> were composed again and the view must show them.</summary>
    public event Action? PreviewChanged;

    public bool HasException => _exception is not null;

    public ReportKind Kind
    {
        get => _kind;
        set { if (_kind != value) { _kind = value; InputsChanged(); } }
    }

    public string Description
    {
        get => _description;
        set { value ??= string.Empty; if (_description != value) { _description = value; InputsChanged(); } }
    }

    public bool IncludeDiagnostics
    {
        get => _includeDiagnostics;
        set { if (_includeDiagnostics != value) { _includeDiagnostics = value; InputsChanged(); } }
    }

    /// <summary>Title of the issue or subject of the message, as it will go. The view writes the user's edits back here.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Body, as it will go. The view writes the user's edits back here.</summary>
    public string Preview { get; set; } = string.Empty;

    /// <summary>The user changed title or preview by hand: they are not overwritten behind his back any more.</summary>
    public bool EditedByHand => !SameText(Title, _generatedTitle) || !SameText(Preview, _generatedPreview);

    /// <summary>Description, kind or the diagnostics box changed AFTER the user edited the preview by hand.</summary>
    public bool PreviewIsStale { get; private set; }

    public string DialogTitle => _kind == ReportKind.Error ? Strings.ReportDlg_TitleError : Strings.ReportDlg_TitleSuggestion;

    /// <summary>Composes title and preview again from the description, discarding edits made by hand.</summary>
    public void Regenerate()
    {
        var report = _builder.Build(new ReportRequest(_kind, _description)
        {
            Diagnostics = _includeDiagnostics ? _diagnostics() : null,
            Exception = _exception
        });
        Title = _generatedTitle = report.Title;
        Preview = _generatedPreview = report.Body;
        PreviewIsStale = false;
        PreviewChanged?.Invoke();
    }

    public ReportActionResult OpenInGitHub() => Send(ReportChannel.GitHubIssue, signature: null);

    public async Task<ReportActionResult> SendByEmailAsync(CancellationToken ct = default)
    {
        if (Check() is { } problem) return problem;

        string? signature = null;
        var info = await _personalInfo.LoadAsync(ct);
        // The signature is never added silently: the user is told exactly what it is and may say no.
        if (!info.IsEmpty && _prompts.Confirm(string.Format(Strings.ReportDlg_ConfirmSignature, info.Signature), DialogTitle))
            signature = info.Signature;
        return Send(ReportChannel.Email, signature, alreadyChecked: true);
    }

    public ReportActionResult CopyToClipboard()
    {
        if (Check() is { } problem) return problem;
        if (_clipboard.SetText(FullText(null)))
            _prompts.Info(Strings.ReportDlg_Copied, DialogTitle);
        else
            _prompts.Warn(Strings.ReportDlg_ErrClipboard, DialogTitle);
        return ReportActionResult.Stay();
    }

    /// <summary>Title and body as one text, for the clipboard.</summary>
    public string FullText(string? signature)
    {
        var text = Title.Trim() + Environment.NewLine + Environment.NewLine + Preview.Trim();
        return string.IsNullOrWhiteSpace(signature) ? text : text + Environment.NewLine + Environment.NewLine + "-- " + Environment.NewLine + signature.Trim();
    }

    private ReportActionResult Send(ReportChannel channel, string? signature, bool alreadyChecked = false)
    {
        if (!alreadyChecked && Check() is { } problem) return problem;
        if (!_senders.TryGetValue(channel, out var sender))
            throw new InvalidOperationException($"No sender for {channel}.");

        var composed = sender.Compose(new Report(_kind, Title.Trim(), Preview.Trim()), signature);
        if (composed.Truncated)
        {
            // Said BEFORE the browser or the mail program takes the focus away.
            var copied = _clipboard.SetText(FullText(signature));
            _prompts.Info(copied ? Strings.ReportDlg_TruncatedCopied : Strings.ReportDlg_TruncatedNotCopied, DialogTitle);
        }

        if (sender.Open(composed)) return ReportActionResult.Done;

        // Nothing could be opened (no browser, no mail program): the report is not lost.
        var onClipboard = _clipboard.SetText(FullText(signature));
        _prompts.Warn(channel == ReportChannel.Email
            ? string.Format(onClipboard ? Strings.ReportDlg_ErrOpenEmailCopied : Strings.ReportDlg_ErrOpenEmail, ReportDestinations.AuthorEmail)
            : string.Format(onClipboard ? Strings.ReportDlg_ErrOpenBrowserCopied : Strings.ReportDlg_ErrOpenBrowser, ReportDestinations.NewIssue.AbsoluteUri), DialogTitle);
        return ReportActionResult.Stay();
    }

    /// <summary>Null = ready to go.</summary>
    private ReportActionResult? Check()
    {
        if (!HasException && string.IsNullOrWhiteSpace(_description) && !EditedByHand)
        {
            _prompts.Warn(_kind == ReportKind.Error ? Strings.ReportDlg_ErrNoDescriptionError : Strings.ReportDlg_ErrNoDescriptionSuggestion, DialogTitle);
            return ReportActionResult.Stay(ReportField.Description);
        }
        if (string.IsNullOrWhiteSpace(Title))
        {
            _prompts.Warn(Strings.ReportDlg_ErrNoTitle, DialogTitle);
            return ReportActionResult.Stay(ReportField.Title);
        }
        if (string.IsNullOrWhiteSpace(Preview))
        {
            _prompts.Warn(Strings.ReportDlg_ErrNoPreview, DialogTitle);
            return ReportActionResult.Stay(ReportField.Preview);
        }
        if (PreviewIsStale && _prompts.Confirm(Strings.ReportDlg_ConfirmRegenerate, DialogTitle))
        {
            Regenerate();
            return ReportActionResult.Stay(ReportField.Preview);
        }
        return null;
    }

    private void InputsChanged()
    {
        if (EditedByHand) PreviewIsStale = true;
        else Regenerate();
    }

    private static bool SameText(string a, string b) =>
        string.Equals(a.ReplaceLineEndings("\n").Trim(), b.ReplaceLineEndings("\n").Trim(), StringComparison.Ordinal);
}
