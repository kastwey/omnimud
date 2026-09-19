using System.Globalization;
using System.Reflection;

namespace Omnimud.Core.Options;

/// <summary>
/// <see cref="OmnimudOptions"/> ↔ key/value strings. Key = property name; value = invariant-culture
/// text (bool "true"/"false", enums by name, char as the character itself, null string as "").
/// Reading is tolerant: unknown keys are ignored and missing, corrupt or out-of-range values fall
/// back to the default of that option, so a damaged row can never stop the application.
/// </summary>
public static class OptionsSerializer
{
    private static readonly PropertyInfo[] Properties = typeof(OmnimudOptions)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.SetMethod is not null && p.GetIndexParameters().Length == 0)
        .ToArray();

    /// <summary>Keys written by earlier v2 builds under another name → current key. Only read, never written.</summary>
    public static IReadOnlyDictionary<string, string> LegacyKeys { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SoundEnabled"] = nameof(OmnimudOptions.EnableSounds),
            ["FlashOnMessage"] = nameof(OmnimudOptions.FlashWindow)
        };

    /// <summary>Inclusive valid ranges; a value outside them is treated as corrupt.</summary>
    private static readonly Dictionary<string, (double Min, double Max)> Ranges = new(StringComparer.Ordinal)
    {
        [nameof(OmnimudOptions.HistorySize)] = (1, 10_000),
        [nameof(OmnimudOptions.MaxLines)] = (100, 1_000_000),
        [nameof(OmnimudOptions.PromptFlushMilliseconds)] = (0, 60_000),
        [nameof(OmnimudOptions.Volume)] = (0, 100),
        [nameof(OmnimudOptions.ProxyPort)] = (0, 65_535),
        [nameof(OmnimudOptions.FontSize)] = (4, 200)
    };

    /// <summary>
    /// Keys that never leave this installation: they are stored in the database but left out of exported
    /// files, and an import keeps whatever the target already had.
    /// </summary>
    public static IReadOnlySet<string> SecretKeys { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { nameof(OmnimudOptions.ProxyPasswordProtected) };

    /// <summary>Every key that <see cref="Serialize"/> writes.</summary>
    public static IReadOnlyList<string> Keys { get; } = Properties.Select(p => p.Name).ToArray();

    public static IReadOnlyDictionary<string, string> Serialize(OmnimudOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var result = new Dictionary<string, string>(Properties.Length, StringComparer.Ordinal);
        foreach (var property in Properties)
            result[property.Name] = Format(property.GetValue(options));
        return result;
    }

    /// <summary><see cref="Serialize"/> without the <see cref="SecretKeys"/>: what goes into an exported file.</summary>
    public static IReadOnlyDictionary<string, string> SerializeForExport(OmnimudOptions options) =>
        WithoutSecrets(Serialize(options));

    /// <summary>A copy of <paramref name="values"/> without the <see cref="SecretKeys"/>.</summary>
    public static Dictionary<string, string> WithoutSecrets(IEnumerable<KeyValuePair<string, string>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            if (key is not null && !SecretKeys.Contains(key))
                result[key] = value;
        }
        return result;
    }

    public static OmnimudOptions Deserialize(IEnumerable<KeyValuePair<string, string>>? values)
    {
        var options = new OmnimudOptions();
        if (values is null) return options;

        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var legacy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in values)
        {
            if (key is null || value is null) continue;
            if (LegacyKeys.TryGetValue(key, out var currentKey)) legacy[currentKey] = value;
            else lookup[key] = value;
        }

        // A current key always wins over its legacy spelling.
        foreach (var (key, value) in legacy)
            lookup.TryAdd(key, value);

        foreach (var property in Properties)
        {
            if (!lookup.TryGetValue(property.Name, out var text)) continue;
            if (TryParse(property, text, out var parsed))
                property.SetValue(options, parsed);
        }
        return options;
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        bool b => b ? "true" : "false",
        char c => c.ToString(),
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        Enum e => e.ToString(),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static bool TryParse(PropertyInfo property, string text, out object? value)
    {
        value = null;
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        if (type == typeof(string))
        {
            // Null and "" share the stored form; which one comes back depends on the option's default.
            value = text.Length == 0 && property.GetValue(OmnimudOptions.Default) is null ? null : text;
            return true;
        }

        if (type == typeof(bool))
        {
            var trimmed = text.Trim();
            if (bool.TryParse(trimmed, out var b)) { value = b; return true; }
            if (trimmed == "1") { value = true; return true; }
            if (trimmed == "0") { value = false; return true; }
            return false;
        }

        if (type == typeof(char))
        {
            if (text.Length != 1 || char.IsControl(text[0])) return false;
            value = text[0];
            return true;
        }

        if (type == typeof(int))
        {
            if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return false;
            if (!InRange(property.Name, i)) return false;
            value = i;
            return true;
        }

        if (type == typeof(float))
        {
            if (!TryParseDouble(text, out var d) || !InRange(property.Name, d)) return false;
            value = (float)d;
            return true;
        }

        if (type == typeof(double))
        {
            if (!TryParseDouble(text, out var d) || !InRange(property.Name, d)) return false;
            value = d;
            return true;
        }

        if (type.IsEnum)
        {
            if (!Enum.TryParse(type, text.Trim(), ignoreCase: true, out var e) || e is null || !Enum.IsDefined(type, e)) return false;
            value = e;
            return true;
        }

        return false;
    }

    private static bool TryParseDouble(string text, out double value)
    {
        const NumberStyles style = NumberStyles.Float;
        var trimmed = text.Trim();
        if (double.TryParse(trimmed, style, CultureInfo.InvariantCulture, out value) && double.IsFinite(value))
            return true;

        // Values written with a decimal comma by a culture-sensitive writer (the original client did that).
        return trimmed.Contains(',') && !trimmed.Contains('.')
            && double.TryParse(trimmed.Replace(',', '.'), style, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value);
    }

    private static bool InRange(string key, double value) =>
        !Ranges.TryGetValue(key, out var range) || (value >= range.Min && value <= range.Max);
}
