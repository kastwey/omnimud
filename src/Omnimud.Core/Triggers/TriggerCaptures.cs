using System.Globalization;
using System.Text.RegularExpressions;

namespace Omnimud.Core.Triggers;

/// <summary>Puts what a trigger captured into the text of its action.</summary>
public static partial class TriggerCaptures
{
    /// <summary>
    /// Substitutes %1, %2... in the action with the captured values, from the highest index
    /// down so that %1 never eats the prefix of %10.
    /// </summary>
    public static string Substitute(string action, IReadOnlyList<string> captures)
    {
        if (captures.Count == 0 || !action.Contains('%'))
            return action;

        // One pass, longest index first: "%10" is capture 10 when there are ten captures and
        // capture 1 followed by "0" otherwise. Captured text is never substituted again.
        return PlaceholderRegex().Replace(action, m =>
        {
            var digits = m.Groups[1].Value;
            for (var length = Math.Min(digits.Length, 3); length >= 1; length--)
            {
                var index = int.Parse(digits.AsSpan(0, length), CultureInfo.InvariantCulture);
                if (index >= 1 && index <= captures.Count)
                    return captures[index - 1] + digits[length..];
            }
            return m.Value;
        });
    }

    [GeneratedRegex(@"%(\d+)")]
    private static partial Regex PlaceholderRegex();
}
