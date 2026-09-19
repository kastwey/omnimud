using System.Globalization;

namespace Omnimud.Core;

/// <summary>
/// Public door to the language of the messages produced by Core (its resource class is internal).
/// The application sets it once at startup, next to the language of the interface.
/// </summary>
public static class Localization
{
    /// <summary>Culture of Core's messages; null = follow <see cref="CultureInfo.CurrentUICulture"/>.</summary>
    public static CultureInfo? Culture => Resources.Strings.Culture;

    public static void SetCulture(CultureInfo? culture) => Resources.Strings.Culture = culture;
}
