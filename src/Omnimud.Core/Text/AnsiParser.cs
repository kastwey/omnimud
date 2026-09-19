using System.Text;

namespace Omnimud.Core.Text;

public sealed class AnsiParser : IAnsiParser
{
    private const char Esc = '\x1b';

    /// <summary>Style in effect after the last <see cref="ParseLine"/> call.</summary>
    public AnsiStyle CurrentStyle { get; private set; } = AnsiStyle.Default;

    /// <summary>Stateless: every call starts with the default style.</summary>
    public IReadOnlyList<StyledSegment> Parse(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        var style = AnsiStyle.Default;
        return ParseCore(text, ref style, null);
    }

    /// <summary>
    /// Stateful: the style left by the previous line carries over to this one, which is how
    /// MUDs colour several lines with a single sequence. One parser instance per session.
    /// </summary>
    public AnsiLine ParseLine(string line)
    {
        if (string.IsNullOrEmpty(line))
            return new AnsiLine([], string.Empty);

        var style = CurrentStyle;
        var plain = new StringBuilder(line.Length);
        var segments = ParseCore(line, ref style, plain);
        CurrentStyle = style;
        return new AnsiLine(segments, plain.ToString());
    }

    /// <summary>Back to the default style (new connection).</summary>
    public void Reset() => CurrentStyle = AnsiStyle.Default;

    /// <summary>Removes every escape sequence (SGR or not) and returns the plain text.</summary>
    public static string Strip(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf(Esc) < 0)
            return text ?? string.Empty;

        var style = AnsiStyle.Default;
        var plain = new StringBuilder(text.Length);
        ParseCore(text, ref style, plain);
        return plain.ToString();
    }

    private static List<StyledSegment> ParseCore(string text, ref AnsiStyle style, StringBuilder? plain)
    {
        var segments = new List<StyledSegment>();
        var run = new StringBuilder();
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];
            if (c != Esc)
            {
                run.Append(c);
                i++;
                continue;
            }

            // Escape sequence: close the current run, then consume the sequence.
            if (i + 1 >= text.Length)
                break; // lone ESC at the end: dropped

            var kind = text[i + 1];
            if (kind == '[')
            {
                // CSI: parameters 0x30-0x3F, intermediates 0x20-0x2F, final 0x40-0x7E.
                var j = i + 2;
                while (j < text.Length && text[j] is >= '\x30' and <= '\x3f') j++;
                var paramsEnd = j;
                while (j < text.Length && text[j] is >= '\x20' and <= '\x2f') j++;
                if (j >= text.Length)
                    break; // truncated sequence: dropped

                var final = text[j];
                if (final == 'm' && paramsEnd == j)
                {
                    var codes = text[(i + 2)..paramsEnd];
                    if (codes.Length == 0 || codes[0] is not ('<' or '=' or '>' or '?'))
                    {
                        Flush(segments, run, style, plain);
                        style = ApplySgrCodes(codes, style);
                    }
                }
                // Any other CSI sequence (cursor movement, erase...) is discarded.
                i = j + 1;
            }
            else if (kind == ']')
            {
                // OSC: runs until BEL or ESC \.
                var j = i + 2;
                while (j < text.Length && text[j] != '\a' && !(text[j] == Esc && j + 1 < text.Length && text[j + 1] == '\\')) j++;
                i = j >= text.Length ? text.Length : (text[j] == '\a' ? j + 1 : j + 2);
            }
            else
            {
                // Two-character escape (ESC 7, ESC c...).
                i += 2;
            }
        }

        Flush(segments, run, style, plain);
        return segments;
    }

    private static void Flush(List<StyledSegment> segments, StringBuilder run, AnsiStyle style, StringBuilder? plain)
    {
        if (run.Length == 0) return;
        var text = run.ToString();
        run.Clear();
        plain?.Append(text);

        // Merge with the previous segment when the style did not really change.
        if (segments.Count > 0 && segments[^1].Style == style)
            segments[^1] = new StyledSegment(segments[^1].Text + text, style);
        else
            segments.Add(new StyledSegment(text, style));
    }

    private static AnsiStyle ApplySgrCodes(string codesStr, AnsiStyle current)
    {
        if (string.IsNullOrEmpty(codesStr))
        {
            // ESC[m is equivalent to ESC[0m (reset)
            return AnsiStyle.Default;
        }

        var fg = current.Foreground;
        var bg = current.Background;
        var bold = current.Bold;
        var dim = current.Dim;
        var italic = current.Italic;
        var underline = current.Underline;
        var blink = current.Blink;
        var inverse = current.Inverse;
        var hidden = current.Hidden;
        var strikethrough = current.Strikethrough;

        var codes = codesStr.Split([';', ':'], StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < codes.Length; i++)
        {
            if (!int.TryParse(codes[i], out var code))
                continue;

            switch (code)
            {
                case 0: // Reset all
                    return AnsiStyle.Default;
                case 1: bold = true; break;
                case 2: dim = true; break;
                case 3: italic = true; break;
                case 4: underline = true; break;
                case 5: blink = true; break;
                case 7: inverse = true; break;
                case 8: hidden = true; break;
                case 9: strikethrough = true; break;
                case 22: bold = false; dim = false; break;
                case 23: italic = false; break;
                case 24: underline = false; break;
                case 25: blink = false; break;
                case 27: inverse = false; break;
                case 28: hidden = false; break;
                case 29: strikethrough = false; break;

                // Foreground colors 30-37
                case >= 30 and <= 37:
                    fg = new AnsiColor(AnsiColorType.Standard16, (byte)(code - 30));
                    break;
                case 38: // Extended foreground
                    fg = ParseExtendedColor(codes, ref i);
                    break;
                case 39: // Default foreground
                    fg = AnsiColor.Default;
                    break;

                // Background colors 40-47
                case >= 40 and <= 47:
                    bg = new AnsiColor(AnsiColorType.Standard16, (byte)(code - 40));
                    break;
                case 48: // Extended background
                    bg = ParseExtendedColor(codes, ref i);
                    break;
                case 49: // Default background
                    bg = AnsiColor.DefaultBackground;
                    break;

                // Bright foreground 90-97
                case >= 90 and <= 97:
                    fg = new AnsiColor(AnsiColorType.Standard16, (byte)(code - 90 + 8));
                    break;

                // Bright background 100-107
                case >= 100 and <= 107:
                    bg = new AnsiColor(AnsiColorType.Standard16, (byte)(code - 100 + 8));
                    break;
            }
        }

        return new AnsiStyle(fg, bg, bold, dim, italic, underline, blink, inverse, hidden, strikethrough);
    }

    private static AnsiColor ParseExtendedColor(string[] codes, ref int i)
    {
        if (i + 1 >= codes.Length)
            return AnsiColor.Default;

        if (!int.TryParse(codes[i + 1], out var mode))
            return AnsiColor.Default;

        if (mode == 5 && i + 2 < codes.Length) // 256-color
        {
            i += 2;
            if (int.TryParse(codes[i], out var colorIndex) && colorIndex is >= 0 and <= 255)
                return new AnsiColor(AnsiColorType.Extended256, (byte)colorIndex);
        }
        else if (mode == 2 && i + 4 < codes.Length) // TrueColor
        {
            i += 2;
            if (int.TryParse(codes[i], out var r) &&
                int.TryParse(codes[i + 1], out var g) &&
                int.TryParse(codes[i + 2], out var b))
            {
                i += 2;
                return new AnsiColor(AnsiColorType.TrueColor, 0, (byte)r, (byte)g, (byte)b);
            }
        }

        return AnsiColor.Default;
    }
}
