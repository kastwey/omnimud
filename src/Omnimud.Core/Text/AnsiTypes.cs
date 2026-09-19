namespace Omnimud.Core.Text;

public readonly record struct StyledSegment(string Text, AnsiStyle Style);

public readonly record struct AnsiStyle(
    AnsiColor Foreground,
    AnsiColor Background,
    bool Bold = false,
    bool Dim = false,
    bool Italic = false,
    bool Underline = false,
    bool Blink = false,
    bool Inverse = false,
    bool Hidden = false,
    bool Strikethrough = false)
{
    public static readonly AnsiStyle Default = new(AnsiColor.Default, AnsiColor.DefaultBackground);
}

public readonly record struct AnsiColor(AnsiColorType Type, byte Value = 0, byte R = 0, byte G = 0, byte B = 0)
{
    public static readonly AnsiColor Default = new(AnsiColorType.Default);
    public static readonly AnsiColor DefaultBackground = new(AnsiColorType.Default);

    // Standard 16 colors
    public static readonly AnsiColor Black = new(AnsiColorType.Standard16, 0);
    public static readonly AnsiColor Red = new(AnsiColorType.Standard16, 1);
    public static readonly AnsiColor Green = new(AnsiColorType.Standard16, 2);
    public static readonly AnsiColor Yellow = new(AnsiColorType.Standard16, 3);
    public static readonly AnsiColor Blue = new(AnsiColorType.Standard16, 4);
    public static readonly AnsiColor Magenta = new(AnsiColorType.Standard16, 5);
    public static readonly AnsiColor Cyan = new(AnsiColorType.Standard16, 6);
    public static readonly AnsiColor White = new(AnsiColorType.Standard16, 7);

    // Bright variants (8-15)
    public static readonly AnsiColor BrightBlack = new(AnsiColorType.Standard16, 8);
    public static readonly AnsiColor BrightRed = new(AnsiColorType.Standard16, 9);
    public static readonly AnsiColor BrightGreen = new(AnsiColorType.Standard16, 10);
    public static readonly AnsiColor BrightYellow = new(AnsiColorType.Standard16, 11);
    public static readonly AnsiColor BrightBlue = new(AnsiColorType.Standard16, 12);
    public static readonly AnsiColor BrightMagenta = new(AnsiColorType.Standard16, 13);
    public static readonly AnsiColor BrightCyan = new(AnsiColorType.Standard16, 14);
    public static readonly AnsiColor BrightWhite = new(AnsiColorType.Standard16, 15);
}

public enum AnsiColorType
{
    Default,
    Standard16,
    Extended256,
    TrueColor
}

/// <summary>One parsed line: what to paint and its text without escape sequences.</summary>
public readonly record struct AnsiLine(IReadOnlyList<StyledSegment> Segments, string PlainText);
