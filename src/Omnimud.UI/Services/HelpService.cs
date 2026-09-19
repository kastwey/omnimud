using System.Diagnostics;
using System.Globalization;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Services;

/// <summary>The help shipped next to the executable, in <c>docs\</c>: the user manual (one HTML file per language) and the Lua reference.</summary>
public interface IHelpService
{
    /// <summary>Opens the manual in the language of the interface (or in the other one when that file is missing). Tells the user when there is none.</summary>
    void OpenManual();
    void OpenLuaReference();
}

public sealed class HelpService : IHelpService
{
    public const string DocsFolder = "docs";
    public const string LuaReferenceFile = "API_LUA.md";
    /// <summary>Languages the manual exists in; the first one is the fallback of last resort.</summary>
    public static IReadOnlyList<string> ManualLanguages { get; } = ["en", "es"];

    private readonly string _baseDirectory;
    private readonly IUserPrompts _prompts;
    private readonly Func<string, bool> _openFile;

    public HelpService(IUserPrompts prompts) : this(AppContext.BaseDirectory, prompts, OpenWithShell) { }

    /// <param name="openFile">Opens a file with its program; false when it could not. Tests pass a recorder: nothing is opened.</param>
    public HelpService(string baseDirectory, IUserPrompts prompts, Func<string, bool> openFile)
    {
        _baseDirectory = baseDirectory;
        _prompts = prompts;
        _openFile = openFile;
    }

    public static string ManualFileName(string language) => $"manual.{language}.html";

    /// <summary>
    /// The manual for a culture: its own language when the file exists, else any other language that does.
    /// Null when no manual is installed.
    /// </summary>
    public static string? ResolveManualPath(string baseDirectory, CultureInfo? culture, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        var wanted = (culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName;
        var order = ManualLanguages.Where(l => l.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            .Concat(ManualLanguages.Where(l => !l.Equals(wanted, StringComparison.OrdinalIgnoreCase)));
        return order.Select(l => Path.Combine(baseDirectory, DocsFolder, ManualFileName(l))).FirstOrDefault(exists);
    }

    public void OpenManual()
    {
        var path = ResolveManualPath(_baseDirectory, Strings.Culture);
        Open(path, Path.Combine(_baseDirectory, DocsFolder, ManualFileName(ManualLanguages[0])));
    }

    public void OpenLuaReference()
    {
        var path = Path.Combine(_baseDirectory, DocsFolder, LuaReferenceFile);
        Open(File.Exists(path) ? path : null, path);
    }

    private void Open(string? path, string expected)
    {
        if (path is null)
        {
            _prompts.Info(string.Format(Strings.Help_NotFound, expected), Strings.App_Title);
            return;
        }
        if (!_openFile(path))
            _prompts.Warn(string.Format(Strings.Help_CannotOpen, path), Strings.App_Title);
    }

    /// <summary>The program Windows has for the file; a text file nobody claims (.md) opens in Notepad.</summary>
    private static bool OpenWithShell(string path)
    {
        if (UrlOpener.OpenFile(path)) return true;
        if (!Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            using var process = Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { path }, UseShellExecute = false });
            return process is not null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
