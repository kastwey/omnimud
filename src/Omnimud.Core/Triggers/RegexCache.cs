using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Omnimud.Core.Triggers;

/// <summary>
/// Triggers are evaluated against every line, so their regexes are built once. Every regex
/// carries a match timeout: a user pattern must never be able to hang the session.
/// </summary>
internal static class RegexCache
{
    private const int MaxEntries = 2000;
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly ConcurrentDictionary<(string Pattern, RegexOptions Options), Regex?> Cache = new();

    /// <summary>Null when the pattern is not a valid regex.</summary>
    public static Regex? Get(string pattern, RegexOptions options)
    {
        if (Cache.TryGetValue((pattern, options), out var cached))
            return cached;

        Regex? regex;
        try
        {
            regex = new Regex(pattern, options | RegexOptions.CultureInvariant, MatchTimeout);
        }
        catch (ArgumentException)
        {
            regex = null;
        }

        if (Cache.Count >= MaxEntries)
            Cache.Clear();
        Cache[(pattern, options)] = regex;
        return regex;
    }
}
