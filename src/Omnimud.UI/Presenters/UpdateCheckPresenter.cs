using Omnimud.Core.Options;
using Omnimud.Core.Reports;
using Omnimud.Core.Updates;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;

namespace Omnimud.UI.Presenters;

/// <summary>The "there is a new version" dialog. The real one is a window; tests answer in its place.</summary>
public interface IUpdateNotice
{
    /// <summary>Shows version, title and notes. True when the user asked to open the download page
    /// (only offered when <see cref="UpdateAvailable.ReleasePage"/> is not null). It may wait for a good moment
    /// (the launcher being the active window) so that it never pops up over a game.</summary>
    Task<bool> ShowAsync(UpdateAvailable update);
}

/// <summary>
/// Update checks of the launcher. Rules (each one has its test):
/// <list type="bullet">
/// <item>Nothing happens on startup unless the GLOBAL option <see cref="OmnimudOptions.CheckUpdatesOnStartup"/> is on (off by default).</item>
/// <item>On startup only a new version is mentioned: up to date, no network, a server error... are silent.</item>
/// <item>A manual check always tells the result, whatever it is.</item>
/// <item>Nothing is downloaded or run: the most that happens is opening the page of the release in the browser, and only
/// when it is an https page of the project at github.com.</item>
/// </list>
/// </summary>
public sealed class UpdateCheckPresenter
{
    private readonly IUpdateChecker _checker;
    private readonly IOptionsService _options;
    private readonly IUserPrompts _prompts;
    private readonly IUpdateNotice _notice;
    private readonly IExternalLauncher _launcher;
    private int _checking;

    public UpdateCheckPresenter(IUpdateChecker checker, IOptionsService options, IUserPrompts prompts, IUpdateNotice notice, IExternalLauncher launcher)
    {
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(prompts);
        ArgumentNullException.ThrowIfNull(notice);
        ArgumentNullException.ThrowIfNull(launcher);
        _checker = checker;
        _options = options;
        _prompts = prompts;
        _notice = notice;
        _launcher = launcher;
    }

    /// <summary>The global option, as the menu box must show it.</summary>
    public async Task<bool> GetCheckOnStartupAsync(CancellationToken ct = default) =>
        (await _options.ResolveAsync(null, null, ct)).CheckUpdatesOnStartup;

    /// <summary>Writes the global option (the rest of the global block is kept as it was).</summary>
    public async Task SetCheckOnStartupAsync(bool value, CancellationToken ct = default)
    {
        var global = await _options.ResolveAsync(null, null, ct);
        if (global.CheckUpdatesOnStartup == value) return;
        await _options.SaveAsync(OptionScope.Global, null, global with { CheckUpdatesOnStartup = value }, ct);
    }

    /// <summary>Call once when the launcher is ready. Returns what was found, or null when the option is off (no request was made).</summary>
    public async Task<UpdateCheckResult?> CheckOnStartupAsync(CancellationToken ct = default)
    {
        bool enabled;
        try
        {
            enabled = await GetCheckOnStartupAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null; // options that cannot be read never turn into a request
        }
        if (!enabled) return null;

        var result = await RunAsync(ct);
        if (result is UpdateAvailable update) await AnnounceAsync(update);
        return result;
    }

    /// <summary>Tools → Check for updates now.</summary>
    public async Task<UpdateCheckResult?> CheckNowAsync(CancellationToken ct = default)
    {
        var result = await RunAsync(ct);
        switch (result)
        {
            case null:
                // Another check is running (the one of the startup): a manual check never ends in silence.
                _prompts.Info(Strings.Update_AlreadyChecking, Strings.Update_Title);
                break;
            case UpdateAvailable update:
                await AnnounceAsync(update);
                break;
            case UpToDate { NoReleasesYet: true } upToDate:
                _prompts.Info(string.Format(Strings.Update_NoReleasesYet, upToDate.Current), Strings.Update_Title);
                break;
            case UpToDate upToDate:
                _prompts.Info(string.Format(Strings.Update_UpToDate, upToDate.Current), Strings.Update_Title);
                break;
            case UpdateCheckFailed failed:
                _prompts.Warn(Describe(failed), Strings.Update_Title);
                break;
        }
        return result;
    }

    public static string Describe(UpdateCheckFailed failed)
    {
        var reason = failed.Reason switch
        {
            UpdateCheckFailure.Network => Strings.Update_FailNetwork,
            UpdateCheckFailure.Timeout => Strings.Update_FailTimeout,
            UpdateCheckFailure.RateLimited => Strings.Update_FailRateLimited,
            _ => Strings.Update_FailUnexpected
        };
        return string.IsNullOrWhiteSpace(failed.Detail) ? reason : $"{reason} ({failed.Detail})";
    }

    private async Task<UpdateCheckResult?> RunAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _checking, 1) == 1) return null;
        try
        {
            // Off the UI thread: resolving the system proxy may take a moment.
            return await Task.Run(() => _checker.CheckAsync(ct), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new UpdateCheckFailed(UpdateCheckFailure.UnexpectedResponse, ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _checking, 0);
        }
    }

    private async Task AnnounceAsync(UpdateAvailable update)
    {
        if (!await _notice.ShowAsync(update)) return;
        // Checked again at the last moment: only an https page of the project at github.com is ever opened.
        if (update.ReleasePage is not { } page || !GitHubReleasePage.IsTrusted(page)) return;
        if (!_launcher.Open(page))
            _prompts.Warn(string.Format(Strings.Update_CannotOpenPage, page.AbsoluteUri), Strings.Update_Title);
    }
}
