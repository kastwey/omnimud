using System.Text;
using System.Text.RegularExpressions;

namespace Omnimud.Core.Triggers;

/// <summary>
/// Provides sscanf-like pattern matching with %s, %d, %w placeholders.
/// </summary>
public static class SscanfMatcher
{
    /// <summary>
    /// Matches text against a sscanf-like pattern.
    /// Returns captured values or null if no match.
    /// </summary>
    /// <remarks>
    /// Supported format specifiers:
    /// - %s: any characters (non-greedy)
    /// - %d: digits only
    /// - %w: word characters (non-whitespace)
    /// - %Ns, %Nd, %Nw: N characters, N digits, N words (the original client's numbered forms)
    /// - %%: literal percent sign
    /// </remarks>
    public static IReadOnlyList<string>? Match(string text, string pattern, bool caseSensitive)
    {
        var regexPattern = ConvertToRegex(pattern);
        if (regexPattern is null)
            return null;

        try
        {
            var options = RegexOptions.None;
            if (!caseSensitive)
                options |= RegexOptions.IgnoreCase;

            var regex = RegexCache.Get(regexPattern, options);
            if (regex is null)
                return null;

            var match = regex.Match(text);

            if (!match.Success)
                return null;

            var captures = new List<string>();
            for (var i = 1; i < match.Groups.Count; i++)
                captures.Add(match.Groups[i].Value);

            return captures;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    private static string? ConvertToRegex(string pattern)
    {
        var sb = new StringBuilder();
        var i = 0;

        // A leading ^ and a trailing $ are anchors, as in the original client's patterns
        // ("^%s ha muerto.", "^%w te mira$").
        var anchorEnd = false;
        if (pattern.StartsWith('^'))
        {
            sb.Append('^');
            i = 1;
        }
        if (pattern.Length > i && pattern.EndsWith('$'))
        {
            anchorEnd = true;
            pattern = pattern[..^1];
        }

        while (i < pattern.Length)
        {
            if (pattern[i] == '%' && i + 1 < pattern.Length)
            {
                var next = pattern[i + 1];
                switch (next)
                {
                    case 's':
                        sb.Append("(.+?)");
                        i += 2;
                        break;
                    case 'd':
                        sb.Append(@"(\d+)");
                        i += 2;
                        break;
                    case 'w':
                        sb.Append(@"(\S+)");
                        i += 2;
                        break;
                    case '%':
                        sb.Append('%');
                        i += 2;
                        break;
                    default:
                        // Numbered format, %Ns %Nd %Nw, with N of one or more digits.
                        var digits = 0;
                        while (i + 1 + digits < pattern.Length && char.IsDigit(pattern[i + 1 + digits]))
                            digits++;
                        if (digits > 0 && i + 1 + digits < pattern.Length && int.TryParse(pattern.AsSpan(i + 1, digits), out var length) && length > 0)
                        {
                            var spec = pattern[i + 1 + digits];
                            switch (spec)
                            {
                                case 's':
                                    sb.Append($"(.{{{length}}})");
                                    i += 2 + digits;
                                    break;
                                case 'd':
                                    sb.Append($@"(\d{{{length}}})");
                                    i += 2 + digits;
                                    break;
                                case 'w':
                                    // N words, as in the original client: words separated by a space, a dot or a comma,
                                    // captured together in one group ("%2w" on "Gandalf el Gris llega" gives "Gandalf el").
                                    sb.Append($@"([^\s.,]+(?:[\s.,]+[^\s.,]+){{{length - 1}}})");
                                    i += 2 + digits;
                                    break;
                                default:
                                    sb.Append(Regex.Escape(pattern[i].ToString()));
                                    i++;
                                    break;
                            }
                        }
                        else
                        {
                            sb.Append(Regex.Escape(pattern[i].ToString()));
                            i++;
                        }
                        break;
                }
            }
            else
            {
                sb.Append(Regex.Escape(pattern[i].ToString()));
                i++;
            }
        }

        if (anchorEnd)
            sb.Append('$');

        return sb.ToString();
    }
}
