using System.Globalization;
using Omnimud.Core.Options;

namespace Omnimud.UI.Services;

/// <summary>What Windows says about the languages of the user. Behind an interface so every branch can be tested.</summary>
public interface ISystemLanguages
{
    /// <summary>Display language of Windows for this user.</summary>
    CultureInfo UiCulture { get; }

    /// <summary>Languages the user has installed (keyboard layouts and input methods).</summary>
    IReadOnlyList<CultureInfo> InstalledLanguages { get; }
}

/// <summary>The real thing. The cultures are read when the object is created, before anything changes them.</summary>
public sealed class WindowsSystemLanguages : ISystemLanguages
{
    public WindowsSystemLanguages()
    {
        UiCulture = CultureInfo.CurrentUICulture;
        var installed = new List<CultureInfo>();
        try
        {
            foreach (InputLanguage language in InputLanguage.InstalledInputLanguages)
                installed.Add(language.Culture);
        }
        catch (Exception)
        {
            // A broken keyboard-layout registry must not stop the application: no installed languages known.
        }
        InstalledLanguages = installed;
    }

    public CultureInfo UiCulture { get; }
    public IReadOnlyList<CultureInfo> InstalledLanguages { get; }
}

/// <summary>
/// Decides the language of the interface from the global option <see cref="OmnimudOptions.Language"/>
/// and applies it to the whole process. Must run before the first window is created.
/// </summary>
public sealed class LanguageService
{
    public const string Automatic = "";
    public const string Spanish = "es";
    public const string English = "en";

    private readonly ISystemLanguages _system;

    public LanguageService() : this(new WindowsSystemLanguages()) { }

    public LanguageService(ISystemLanguages system) => _system = system;

    /// <summary>
    /// "" (or anything unknown) = automatic: Spanish when Windows is in Spanish, or when Spanish is among the
    /// installed languages (an English Windows used by a Spanish speaker); English otherwise.
    /// </summary>
    public CultureInfo Resolve(string? languageOption)
    {
        var option = languageOption?.Trim() ?? string.Empty;
        if (option.Length > 0)
        {
            if (IsLanguage(option, Spanish)) return CultureInfo.GetCultureInfo(Spanish);
            if (IsLanguage(option, English)) return CultureInfo.GetCultureInfo(English);
        }

        if (IsSpanish(_system.UiCulture) || _system.InstalledLanguages.Any(IsSpanish))
            return CultureInfo.GetCultureInfo(Spanish);
        return CultureInfo.GetCultureInfo(English);
    }

    /// <summary>Resolves and applies: threads created from now on, this thread, and the string resources of UI and Core.</summary>
    public CultureInfo Apply(string? languageOption)
    {
        var culture = Resolve(languageOption);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        Resources.Strings.Culture = culture;
        Core.Localization.SetCulture(culture);
        return culture;
    }

    /// <summary>Reads the GLOBAL language option (the language is application-wide) and applies it.
    /// If the options cannot be read the automatic language is used: the language must never stop the startup.
    /// Synchronous on purpose: the culture of the CALLING thread (the UI thread) is one of the things to set.</summary>
    public CultureInfo ApplyFromOptions(IOptionsService options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string language;
        try
        {
            language = Task.Run(() => options.ResolveAsync(null, null)).GetAwaiter().GetResult().Language;
        }
        catch (Exception)
        {
            language = Automatic;
        }
        return Apply(language);
    }

    private static bool IsLanguage(string option, string language) =>
        option.Equals(language, StringComparison.OrdinalIgnoreCase)
        || option.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase)
        || option.StartsWith(language + "_", StringComparison.OrdinalIgnoreCase);

    private static bool IsSpanish(CultureInfo? culture) =>
        culture is not null && culture.TwoLetterISOLanguageName.Equals(Spanish, StringComparison.OrdinalIgnoreCase);
}
