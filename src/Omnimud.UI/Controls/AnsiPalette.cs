using Omnimud.Core.Text;

namespace Omnimud.UI.Controls;

/// <summary>Turns an ANSI style into concrete colors and font style.</summary>
internal static class AnsiPalette
{
    public static (Color Fore, Color Back, FontStyle Font) Resolve(AnsiStyle style, Color defaultFore, Color defaultBack, bool highContrast)
    {
        var font = FontStyle.Regular;
        if (style.Bold) font |= FontStyle.Bold;
        if (style.Italic) font |= FontStyle.Italic;
        if (style.Underline) font |= FontStyle.Underline;
        if (style.Strikethrough) font |= FontStyle.Strikeout;

        // In high contrast the user's system colors win: ANSI colors could make text unreadable.
        if (highContrast)
            return style.Inverse ? (defaultBack, defaultFore, font) : (defaultFore, defaultBack, font);

        var fore = ToColor(style.Foreground, defaultFore);
        var back = ToColor(style.Background, defaultBack);

        if (style.Inverse) (fore, back) = (back, fore);
        if (style.Dim) fore = Blend(fore, back, 0.5);
        if (style.Hidden) fore = back;

        return (fore, back, font);
    }

    private static Color ToColor(AnsiColor color, Color fallback) => color.Type switch
    {
        AnsiColorType.Standard16 => Standard16(color.Value, fallback),
        AnsiColorType.Extended256 => Extended256(color.Value, fallback),
        AnsiColorType.TrueColor => Color.FromArgb(color.R, color.G, color.B),
        _ => fallback,
    };

    private static Color Standard16(byte index, Color fallback) => index switch
    {
        0 => Color.FromArgb(0, 0, 0),
        1 => Color.FromArgb(170, 0, 0),
        2 => Color.FromArgb(0, 170, 0),
        3 => Color.FromArgb(170, 85, 0),
        4 => Color.FromArgb(0, 0, 170),
        5 => Color.FromArgb(170, 0, 170),
        6 => Color.FromArgb(0, 170, 170),
        7 => Color.FromArgb(170, 170, 170),
        8 => Color.FromArgb(85, 85, 85),
        9 => Color.FromArgb(255, 85, 85),
        10 => Color.FromArgb(85, 255, 85),
        11 => Color.FromArgb(255, 255, 85),
        12 => Color.FromArgb(85, 85, 255),
        13 => Color.FromArgb(255, 85, 255),
        14 => Color.FromArgb(85, 255, 255),
        15 => Color.FromArgb(255, 255, 255),
        _ => fallback,
    };

    private static Color Extended256(byte index, Color fallback)
    {
        if (index < 16) return Standard16(index, fallback);

        if (index < 232)
        {
            // 6x6x6 cube with the xterm levels 0, 95, 135, 175, 215, 255.
            int adjusted = index - 16;
            return Color.FromArgb(CubeLevel(adjusted / 36), CubeLevel(adjusted % 36 / 6), CubeLevel(adjusted % 6));
        }

        int gray = (index - 232) * 10 + 8;
        return Color.FromArgb(gray, gray, gray);
    }

    private static int CubeLevel(int step) => step == 0 ? 0 : 55 + step * 40;

    private static Color Blend(Color a, Color b, double amountOfB) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * amountOfB),
        (int)(a.G + (b.G - a.G) * amountOfB),
        (int)(a.B + (b.B - a.B) * amountOfB));
}
