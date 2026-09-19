using System.Globalization;
using System.Text;
using Omnimud.Core.Text;

namespace Omnimud.Core.Actions;

/// <summary>
/// Cleans the texts of the actions menu, which come from the MUD: no ANSI, no control characters,
/// no line breaks (a command with a line break would smuggle a second command), no characters that
/// reorder the text on screen, and a maximum length.
/// </summary>
public static class ActionMenuText
{
    private const char Ellipsis = '…';

    /// <summary>One line of at most <see cref="ActionMenuLimits.MaxLabelLength"/> characters; a longer one ends with "…".</summary>
    public static string CleanLabel(string? text)
    {
        var clean = Clean(text);
        if (clean.Length <= ActionMenuLimits.MaxLabelLength) return clean;
        return Cut(clean, ActionMenuLimits.MaxLabelLength - 1).TrimEnd() + Ellipsis;
    }

    /// <summary>One line of at most <see cref="ActionMenuLimits.MaxCommandLength"/> characters.</summary>
    public static string CleanCommand(string? text)
        => Cut(Clean(text), ActionMenuLimits.MaxCommandLength).TrimEnd();

    private static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        text = AnsiParser.Strip(text);
        var builder = new StringBuilder(text.Length);
        var lastWasReplacement = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsSurrogate(c))
            {
                // Only well-formed pairs survive.
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    builder.Append(c).Append(text[++i]);
                    lastWasReplacement = false;
                }
                continue;
            }

            if (IsForbidden(c))
            {
                // Forbidden characters become one space, so "a\r\nb" still reads "a b" (and "a \n b" too).
                if (builder.Length > 0 && builder[^1] != ' ') builder.Append(' ');
                lastWasReplacement = true;
                continue;
            }

            if (c == ' ' && lastWasReplacement) continue;

            builder.Append(c);
            lastWasReplacement = false;
        }

        return builder.ToString().Trim();
    }

    private static bool IsForbidden(char c)
    {
        if (char.IsControl(c)) return true;
        return CharUnicodeInfo.GetUnicodeCategory(c) switch
        {
            UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator => true,
            // Bidirectional embeddings, overrides and isolates: they can make a label read as something else.
            UnicodeCategory.Format => c is (>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069') or '\u200E' or '\u200F' or '\uFEFF',
            _ => false,
        };
    }

    /// <summary>The first <paramref name="length"/> characters, never half a surrogate pair.</summary>
    private static string Cut(string text, int length)
    {
        if (text.Length <= length) return text;
        if (length > 0 && char.IsHighSurrogate(text[length - 1])) length--;
        return text[..length];
    }
}
