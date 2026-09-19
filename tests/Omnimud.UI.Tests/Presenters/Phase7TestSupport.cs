using Omnimud.Core.Reports;
using Omnimud.Core.Updates;
using Omnimud.UI.Presenters;
using Omnimud.UI.Services;

namespace Omnimud.UI.Tests.Presenters;

/// <summary>Records what would have been opened. Nothing is ever handed to the shell in tests.</summary>
internal sealed class RecordingLauncher(bool succeeds = true) : IExternalLauncher
{
    public List<Uri> Opened { get; } = [];
    public bool Succeeds { get; set; } = succeeds;

    public bool Open(Uri address)
    {
        Opened.Add(address);
        return Succeeds;
    }
}

/// <summary>Stands in for the real clipboard, which tests must not touch (it belongs to whoever runs them).</summary>
internal sealed class FakeClipboard(bool works = true) : IClipboardService
{
    public List<string> Texts { get; } = [];
    public string? Text => Texts.LastOrDefault();

    public bool SetText(string text)
    {
        if (!works) return false;
        Texts.Add(text);
        return true;
    }
}

internal sealed class ScriptedUpdateChecker(Func<UpdateCheckResult> result) : IUpdateChecker
{
    public int Calls;

    public ScriptedUpdateChecker(UpdateCheckResult result) : this(() => result) { }

    public Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        Interlocked.Increment(ref Calls);
        return Task.FromResult(result());
    }
}

internal sealed class ScriptedUpdateNotice(bool openPage) : IUpdateNotice
{
    public List<UpdateAvailable> Shown { get; } = [];

    public Task<bool> ShowAsync(UpdateAvailable update)
    {
        Shown.Add(update);
        return Task.FromResult(openPage);
    }
}

internal sealed class FakeHelp : IHelpService
{
    public int Manual, Lua;
    public void OpenManual() => Manual++;
    public void OpenLuaReference() => Lua++;
}

/// <summary>Application dialogs that open nothing and remember what was asked.</summary>
internal sealed class FakeAppDialogs : IAppDialogs
{
    public List<(ReportKind Kind, Exception? Exception)> Reports { get; } = [];
    public int PersonalInfo;
    public FakeHelp FakeHelp { get; } = new();
    public IHelpService Help => FakeHelp;

    public void ShowReport(IWin32Window? owner, ReportKind kind, Exception? exception = null) => Reports.Add((kind, exception));
    public void ShowPersonalInfo(IWin32Window? owner) => PersonalInfo++;
}
