using Omnimud.Core.Options;
using Omnimud.Core.Reports;
using Omnimud.Data.Repositories;
using Omnimud.UI.Forms;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Services;

/// <summary>
/// Dialogs and help that belong to the application as a whole, shared by the launcher and the game windows:
/// reports, personal information and the manual. Behind an interface so windows can be tested with a fake.
/// </summary>
public interface IAppDialogs
{
    /// <summary>Report dialog. With an <paramref name="exception"/> it is an error report about it.</summary>
    void ShowReport(IWin32Window? owner, ReportKind kind, Exception? exception = null);
    void ShowPersonalInfo(IWin32Window? owner);
    IHelpService Help { get; }
}

/// <summary>What the application knows and must never appear in a report: MUD hosts and names, character names, proxy host and user.</summary>
public interface IPrivateTermsProvider
{
    Task<IReadOnlyList<string>> GetAsync(CancellationToken ct = default);
}

public sealed class RepositoryPrivateTermsProvider(IMudRepository muds, ICharacterRepository characters, IOptionsService options) : IPrivateTermsProvider
{
    public async Task<IReadOnlyList<string>> GetAsync(CancellationToken ct = default)
    {
        var terms = new List<string>();
        foreach (var mud in await muds.GetAllAsync(ct).ConfigureAwait(false))
        {
            terms.Add(mud.Host);
            terms.Add(mud.Name);
            foreach (var character in await characters.GetByMudAsync(mud.Id, ct).ConfigureAwait(false))
                terms.Add(character.Name);
        }

        var global = await options.ResolveAsync(null, null, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(global.ProxyHost)) terms.Add(global.ProxyHost);
        if (!string.IsNullOrWhiteSpace(global.ProxyUsername)) terms.Add(global.ProxyUsername);
        return terms.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
    }
}

public sealed class AppDialogs : IAppDialogs
{
    private readonly IUserPrompts _prompts;
    private readonly IPersonalInfoStore _personalInfo;
    private readonly IPrivateTermsProvider? _privateTerms;
    private readonly IOptionsService? _options;
    private readonly IExternalLauncher _launcher;
    private readonly IClipboardService _clipboard;

    public AppDialogs(IUserPrompts prompts, IPersonalInfoStore personalInfo, IHelpService help, IExternalLauncher launcher,
        IClipboardService clipboard, IPrivateTermsProvider? privateTerms = null, IOptionsService? options = null)
    {
        _prompts = prompts;
        _personalInfo = personalInfo;
        Help = help;
        _launcher = launcher;
        _clipboard = clipboard;
        _privateTerms = privateTerms;
        _options = options;
    }

    /// <summary>For a window created without the container: works, but the personal information only lasts as long as the process.</summary>
    public static AppDialogs CreateBasic(IUserPrompts? prompts = null)
    {
        prompts ??= new WinFormsUserPrompts();
        return new AppDialogs(prompts, new MemoryPersonalInfoStore(), new HelpService(prompts), new ShellExternalLauncher(), new WinFormsClipboard());
    }

    public IHelpService Help { get; }

    public void ShowReport(IWin32Window? owner, ReportKind kind, Exception? exception = null)
    {
        using var dialog = new FrmReport(CreateReportPresenter(kind, exception), ShowPersonalInfo);
        dialog.ShowDialog(Usable(owner));
    }

    public void ShowPersonalInfo(IWin32Window? owner)
    {
        using var dialog = new FrmPersonalInfo(new PersonalInfoModel(_personalInfo), _prompts);
        dialog.ShowDialog(Usable(owner));
    }

    internal ReportPresenter CreateReportPresenter(ReportKind kind, Exception? exception)
    {
        var sanitizer = ReportSanitizer.ForThisMachine(LoadPrivateTerms());
        // Read once per dialog: the preview is composed again on every keystroke of the description.
        var diagnostics = new Lazy<DiagnosticInfo>(Diagnostics);
        return new ReportPresenter(kind, exception is null ? null : ExceptionInfo.From(exception), new ReportBuilder(sanitizer), () => diagnostics.Value,
            [new GitHubIssueReportSender(_launcher), new EmailReportSender(_launcher)], _clipboard, _personalInfo, _prompts);
    }

    /// <summary>A failure reading the database must not stop an error report (it may be the very error being reported).</summary>
    private IReadOnlyList<string> LoadPrivateTerms()
    {
        if (_privateTerms is null) return [];
        try
        {
            return Task.Run(() => _privateTerms.GetAsync()).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private DiagnosticInfo Diagnostics()
    {
        var screenReader = ScreenReaderMode.Automatic;
        try
        {
            if (_options is not null)
                screenReader = Task.Run(() => _options.ResolveAsync(null, null)).GetAwaiter().GetResult().ScreenReader;
        }
        catch (Exception)
        {
            // the default is as good as anything when the options cannot be read
        }
        var culture = Strings.Culture ?? System.Globalization.CultureInfo.CurrentUICulture;
        return DiagnosticInfo.Collect(AppInfo.Version, culture.Name, screenReader.ToString());
    }

    private static IWin32Window? Usable(IWin32Window? owner) => owner is Control { IsDisposed: true } ? null : owner;
}
