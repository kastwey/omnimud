using System.Globalization;

namespace Omnimud.Core.Updates;

/// <summary>
/// A version as releases are tagged: "2.1.0", "v2.1", "2.1.0-beta.1", "2.0.0+abc123". Parsing is tolerant
/// (prefix "v", one to four numeric parts, build metadata ignored); comparing follows Semantic Versioning:
/// numbers compare as NUMBERS (1.10 is newer than 1.9 — the original client compared them as text and got it
/// wrong) and a pre-release is older than the release it precedes.
/// </summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    private const int MaxLength = 100;
    private readonly string[] _preRelease;

    private SemanticVersion(int major, int minor, int patch, int revision, string[] preRelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Revision = revision;
        _preRelease = preRelease;
    }

    public SemanticVersion(int major, int minor = 0, int patch = 0) : this(major, minor, patch, 0, [])
    {
        ArgumentOutOfRangeException.ThrowIfNegative(major);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        ArgumentOutOfRangeException.ThrowIfNegative(patch);
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    /// <summary>Fourth part of a .NET style version ("2.0.0.1"); 0 when absent.</summary>
    public int Revision { get; }
    /// <summary>"beta.1" for "2.1.0-beta.1"; empty for a final version.</summary>
    public string PreRelease => string.Join('.', _preRelease);
    public bool IsPreRelease => _preRelease.Length > 0;

    public static SemanticVersion? Parse(string? text) => TryParse(text, out var version) ? version : null;

    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var span = text.Trim();
        if (span.Length > MaxLength) return false;
        if (span[0] is 'v' or 'V') span = span[1..];

        // Build metadata never takes part in a comparison.
        var plus = span.IndexOf('+');
        if (plus >= 0) span = span[..plus];

        string[] preRelease = [];
        var dash = span.IndexOf('-');
        if (dash >= 0)
        {
            preRelease = span[(dash + 1)..].Split('.');
            if (preRelease.Any(p => p.Length == 0 || !p.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))) return false;
            span = span[..dash];
        }

        var parts = span.Split('.');
        if (parts.Length is < 1 or > 4) return false;
        var numbers = new int[4];
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length is 0 or > 9 || !part.All(char.IsAsciiDigit)) return false;
            numbers[i] = int.Parse(part, NumberStyles.None, CultureInfo.InvariantCulture);
        }

        version = new SemanticVersion(numbers[0], numbers[1], numbers[2], numbers[3], preRelease);
        return true;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;
        var result = Major.CompareTo(other.Major);
        if (result == 0) result = Minor.CompareTo(other.Minor);
        if (result == 0) result = Patch.CompareTo(other.Patch);
        if (result == 0) result = Revision.CompareTo(other.Revision);
        if (result != 0) return result;

        // 2.1.0-beta < 2.1.0
        if (!IsPreRelease || !other.IsPreRelease)
            return IsPreRelease ? -1 : other.IsPreRelease ? 1 : 0;

        for (var i = 0; i < Math.Min(_preRelease.Length, other._preRelease.Length); i++)
        {
            result = CompareIdentifiers(_preRelease[i], other._preRelease[i]);
            if (result != 0) return result;
        }
        return _preRelease.Length.CompareTo(other._preRelease.Length);
    }

    /// <summary>Numeric identifiers compare as numbers and sort before alphanumeric ones (beta.2 &lt; beta.10 &lt; beta.x).</summary>
    private static int CompareIdentifiers(string a, string b)
    {
        var aNumeric = a.Length <= 18 && a.All(char.IsAsciiDigit);
        var bNumeric = b.Length <= 18 && b.All(char.IsAsciiDigit);
        if (aNumeric && bNumeric)
            return long.Parse(a, CultureInfo.InvariantCulture).CompareTo(long.Parse(b, CultureInfo.InvariantCulture));
        if (aNumeric != bNumeric) return aNumeric ? -1 : 1;
        return Math.Sign(string.Compare(a, b, StringComparison.OrdinalIgnoreCase));
    }

    public bool Equals(SemanticVersion? other) => other is not null && CompareTo(other) == 0;
    public override bool Equals(object? obj) => Equals(obj as SemanticVersion);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Revision, PreRelease.ToUpperInvariant());

    public static bool operator >(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) < 0;
    public static bool operator >=(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) >= 0;
    public static bool operator <=(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) <= 0;

    /// <summary>"2.1.0" or "2.1.0-beta.1"; the fourth part only when it is not zero.</summary>
    public override string ToString()
    {
        var text = Revision == 0 ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}.{Revision}";
        return IsPreRelease ? text + "-" + PreRelease : text;
    }
}
