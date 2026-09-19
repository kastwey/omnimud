using System.Text.RegularExpressions;

namespace Omnimud.Core.Reports;

/// <summary>
/// Takes out of a diagnostic text what identifies the user: the path of the Windows profile (replaced by
/// <c>%USERPROFILE%</c>), any other <c>X:\Users\name</c> path, the Windows user name and the machine name where
/// they stand as words, and the terms the application knows to be private (MUD hosts, character names...).
/// Pure: what it knows about the machine comes through the constructor.
/// </summary>
public sealed partial class ReportSanitizer
{
    public const string ProfilePlaceholder = "%USERPROFILE%";
    public const string UserPlaceholder = "%USERNAME%";
    public const string MachinePlaceholder = "%COMPUTERNAME%";
    public const string RedactedPlaceholder = "[…]";

    private readonly string? _profilePath;
    private readonly string? _userName;
    private readonly string? _machineName;
    private readonly string[] _privateTerms;

    /// <param name="privateTerms">Words that must never leave: MUD hosts, character names, proxy host and user.
    /// Blank and one-letter terms are ignored; the longest are replaced first.</param>
    public ReportSanitizer(string? userName, string? profilePath, string? machineName = null, IEnumerable<string>? privateTerms = null)
    {
        _userName = Meaningful(userName);
        _machineName = Meaningful(machineName);
        _profilePath = string.IsNullOrWhiteSpace(profilePath) ? null : profilePath.Trim().TrimEnd('\\', '/');
        if (_profilePath is { Length: < 4 }) _profilePath = null; // "C:\" is not a profile
        _privateTerms = (privateTerms ?? [])
            .Select(Meaningful).Where(t => t is not null).Select(t => t!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(t => t.Length)
            .ToArray();
    }

    /// <summary>What this process knows about the machine it runs on.</summary>
    public static ReportSanitizer ForThisMachine(IEnumerable<string>? privateTerms = null) => new(
        Environment.UserName,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.MachineName,
        privateTerms);

    public string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        if (_profilePath is not null)
        {
            text = ReplaceIgnoringCase(text, _profilePath, ProfilePlaceholder);
            text = ReplaceIgnoringCase(text, _profilePath.Replace('\\', '/'), ProfilePlaceholder);
        }
        // Profiles of other accounts, or a profile that is not where Windows says (redirected folders, build machines).
        text = UsersFolder().Replace(text, ProfilePlaceholder);

        foreach (var term in _privateTerms)
            text = ReplaceWord(text, term, RedactedPlaceholder);
        if (_userName is not null) text = ReplaceWord(text, _userName, UserPlaceholder);
        if (_machineName is not null) text = ReplaceWord(text, _machineName, MachinePlaceholder);
        return text;
    }

    private static string? Meaningful(string? term)
    {
        term = term?.Trim();
        return term is { Length: >= 2 } ? term : null;
    }

    private static string ReplaceIgnoringCase(string text, string what, string with) =>
        text.Replace(what, with, StringComparison.OrdinalIgnoreCase);

    /// <summary>Replaces the term where it is not part of a longer word ("ana" inside "banana" stays).</summary>
    private static string ReplaceWord(string text, string term, string with)
    {
        var pattern = $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(term)}(?![\p{{L}}\p{{N}}_])";
        try
        {
            return Regex.Replace(text, pattern, with.Replace("$", "$$"), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        catch (RegexMatchTimeoutException)
        {
            return ReplaceIgnoringCase(text, term, with);
        }
    }

    [GeneratedRegex(@"[A-Za-z]:[\\/]+(Users|Usuarios|Documents and Settings)[\\/]+[^\\/\r\n:*?""<>|]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex UsersFolder();
}
