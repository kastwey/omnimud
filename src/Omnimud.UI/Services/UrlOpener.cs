using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Omnimud.UI.Services;

/// <summary>
/// Opens links found in MUD text. That text is untrusted, so only well-formed http, https, ftp
/// and mailto targets are ever handed to the shell; anything else is refused.
/// </summary>
public static partial class UrlOpener
{
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        Uri.UriSchemeHttp, Uri.UriSchemeHttps, Uri.UriSchemeFtp, Uri.UriSchemeMailto,
    };

    /// <summary>Returns the safe URI for a word of text, or null if it is not a link.</summary>
    public static Uri? Parse(string? word)
    {
        if (string.IsNullOrWhiteSpace(word)) return null;
        word = word.Trim().TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '>', '\'', '"');
        if (word.Length is 0 or > 2048) return null;

        string candidate;
        if (word.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            candidate = "http://" + word;
        else if (word.Contains("://", StringComparison.Ordinal) || word.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            candidate = word;
        else if (EmailAddress().IsMatch(word))
            candidate = "mailto:" + word;
        else
            return null;

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)) return null;
        if (!AllowedSchemes.Contains(uri.Scheme)) return null;
        if (uri.Scheme != Uri.UriSchemeMailto && string.IsNullOrEmpty(uri.Host)) return null;
        return uri;
    }

    public static bool Open(Uri uri)
    {
        if (!AllowedSchemes.Contains(uri.Scheme)) return false;
        return Start(uri.AbsoluteUri);
    }

    /// <summary>Opens a local file shipped with the application (help).</summary>
    public static bool OpenFile(string path) => File.Exists(path) && Start(path);

    private static bool Start(string target)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}$")]
    private static partial Regex EmailAddress();
}
