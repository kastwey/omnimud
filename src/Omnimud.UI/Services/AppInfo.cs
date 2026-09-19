using System.Reflection;
using Omnimud.Core.Reports;
using Omnimud.Core.Updates;

namespace Omnimud.UI.Services;

/// <summary>The version of the running application. It comes from <c>Directory.Build.props</c> (Version) through the assembly attributes.</summary>
public static class AppInfo
{
    /// <summary>"2.0.0", or "2.1.0-beta.1" for a preliminary build; never the commit hash the SDK appends after '+'.</summary>
    public static string Version { get; } = ReadVersion(typeof(AppInfo).Assembly);

    public static SemanticVersion SemanticVersion { get; } = SemanticVersion.Parse(Version) ?? new SemanticVersion(0);

    internal static string ReadVersion(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return CleanVersion(informational) ?? assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    internal static string? CleanVersion(string? informational)
    {
        if (string.IsNullOrWhiteSpace(informational)) return null;
        var plus = informational.IndexOf('+');
        var text = (plus >= 0 ? informational[..plus] : informational).Trim();
        return SemanticVersion.TryParse(text, out _) ? text : null;
    }
}

/// <summary>Browser and mail client through the Windows shell. Only https and mailto ever get there.</summary>
public sealed class ShellExternalLauncher : IExternalLauncher
{
    public bool Open(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!address.IsAbsoluteUri) return false;
        if (address.Scheme != Uri.UriSchemeHttps && address.Scheme != Uri.UriSchemeMailto) return false;
        return UrlOpener.Open(address);
    }
}

/// <summary>The clipboard behind an interface, so presenters can be tested (and tests never touch the real one).</summary>
public interface IClipboardService
{
    /// <summary>False when the clipboard could not be written (another program holds it).</summary>
    bool SetText(string text);
}

public sealed class WinFormsClipboard : IClipboardService
{
    public bool SetText(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        try
        {
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                Clipboard.SetText(text);
                return true;
            }

            // The clipboard is COM: from any other thread, do it on a short-lived STA one.
            var done = false;
            var thread = new Thread(() =>
            {
                try { Clipboard.SetText(text); done = true; }
                catch (Exception) { done = false; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join(TimeSpan.FromSeconds(5));
            return done;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or ThreadStateException)
        {
            return false;
        }
    }
}
