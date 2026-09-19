using System.Globalization;

namespace Omnimud.Core.Session;

/// <summary>
/// The keys that send a command while movement mode (F2) is on. The numeric value is the stable
/// code stored in the database (Movements.KeyCode) and exchanged in .omnimud files: never renumber.
/// 0-9 are the numeric keypad digits; 10-17 are for keyboards without a keypad.
/// </summary>
public enum MovementKey
{
    NumPad0 = 0,
    NumPad1 = 1,
    NumPad2 = 2,
    NumPad3 = 3,
    NumPad4 = 4,
    NumPad5 = 5,
    NumPad6 = 6,
    NumPad7 = 7,
    NumPad8 = 8,
    NumPad9 = 9,
    ArrowUp = 10,
    ArrowDown = 11,
    ArrowLeft = 12,
    ArrowRight = 13,
    PageUp = 14,
    PageDown = 15,
    Home = 16,
    End = 17,
}

/// <summary>
/// Key codes, default commands and the "configured ?? default" rule, in one place so the session,
/// the dialog and the repository agree.
///
/// A key is resolved like this: the configured command if the owner (character, else MUD) has a
/// row for the key — an EMPTY configured command means "this key does nothing" — otherwise the
/// default command for the language of the user interface, if the key has one.
/// </summary>
public static class MovementKeys
{
    public const int MinCode = 0;
    public const int MaxCode = 17;
    public const int Count = MaxCode - MinCode + 1;

    /// <summary>Arrows, Page Up/Down, Home, End: in the order they are shown to the user.</summary>
    public static IReadOnlyList<int> NavigationCodes { get; } = [10, 11, 12, 13, 14, 15, 16, 17];

    /// <summary>Numeric keypad 0-9.</summary>
    public static IReadOnlyList<int> NumPadCodes { get; } = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];

    /// <summary>Every code, navigation keys first (they are what a laptop user needs).</summary>
    public static IReadOnlyList<int> AllCodes { get; } = [.. NavigationCodes, .. NumPadCodes];

    private static readonly IReadOnlyDictionary<int, string> English = new Dictionary<int, string>
    {
        [(int)MovementKey.ArrowUp] = "north",
        [(int)MovementKey.ArrowDown] = "south",
        [(int)MovementKey.ArrowLeft] = "west",
        [(int)MovementKey.ArrowRight] = "east",
        [(int)MovementKey.PageUp] = "up",
        [(int)MovementKey.PageDown] = "down",
        [8] = "north",
        [2] = "south",
        [4] = "west",
        [6] = "east",
        [7] = "northwest",
        [9] = "northeast",
        [1] = "southwest",
        [3] = "southeast",
    };

    private static readonly IReadOnlyDictionary<int, string> Spanish = new Dictionary<int, string>
    {
        [(int)MovementKey.ArrowUp] = "norte",
        [(int)MovementKey.ArrowDown] = "sur",
        [(int)MovementKey.ArrowLeft] = "oeste",
        [(int)MovementKey.ArrowRight] = "este",
        [(int)MovementKey.PageUp] = "arriba",
        [(int)MovementKey.PageDown] = "abajo",
        [8] = "norte",
        [2] = "sur",
        [4] = "oeste",
        [6] = "este",
        [7] = "noroeste",
        [9] = "noreste",
        [1] = "sudoeste",
        [3] = "sudeste",
    };

    public static bool IsValid(int code) => code is >= MinCode and <= MaxCode;

    public static bool IsNumPad(int code) => code is >= 0 and <= 9;

    /// <summary>Default commands for a language: Spanish for "es", English for anything else.
    /// Home, End, keypad 5 and keypad 0 have none.</summary>
    public static IReadOnlyDictionary<int, string> DefaultCommands(CultureInfo? culture = null) =>
        (culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName == "es" ? Spanish : English;

    /// <summary>True when the key has a default command (the same keys in every language).</summary>
    public static bool HasDefault(int code) => English.ContainsKey(code);

    /// <summary>
    /// configured ?? default for every key, keeping only the keys that end up with a command.
    /// A configured empty (or blank) command switches the key off even when it has a default.
    /// </summary>
    public static IReadOnlyDictionary<int, string> Effective(
        IReadOnlyDictionary<int, string> configured, IReadOnlyDictionary<int, string> defaults)
    {
        var result = new Dictionary<int, string>();
        for (var code = MinCode; code <= MaxCode; code++)
        {
            var command = configured.TryGetValue(code, out var own) ? own
                : defaults.TryGetValue(code, out var byDefault) ? byDefault
                : null;
            if (!string.IsNullOrWhiteSpace(command)) result[code] = command;
        }
        return result;
    }

    /// <summary>Name of the key for the user ("Up arrow", "Numpad 8"), without mnemonic.</summary>
    public static string DisplayName(int code, CultureInfo? culture = null)
    {
        if (!IsValid(code)) throw new ArgumentOutOfRangeException(nameof(code), code, "Movement key codes go from 0 to 17.");
        var spanish = (culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName == "es";
        return (MovementKey)code switch
        {
            MovementKey.ArrowUp => spanish ? "Flecha arriba" : "Up arrow",
            MovementKey.ArrowDown => spanish ? "Flecha abajo" : "Down arrow",
            MovementKey.ArrowLeft => spanish ? "Flecha izquierda" : "Left arrow",
            MovementKey.ArrowRight => spanish ? "Flecha derecha" : "Right arrow",
            MovementKey.PageUp => spanish ? "Re Pág" : "Page Up",
            MovementKey.PageDown => spanish ? "Av Pág" : "Page Down",
            MovementKey.Home => spanish ? "Inicio" : "Home",
            MovementKey.End => spanish ? "Fin" : "End",
            _ => (spanish ? "Teclado numérico " : "Numpad ") + code.ToString(CultureInfo.InvariantCulture),
        };
    }
}
