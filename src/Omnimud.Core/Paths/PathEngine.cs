using System.Text;

namespace Omnimud.Core.Paths;

public sealed class PathEngine : IPathEngine
{
    private readonly IDirectionDictionary _dictionary;

    public PathEngine(IDirectionDictionary dictionary)
    {
        _dictionary = dictionary;
    }

    public IReadOnlyList<string> Expand(string compactPath)
    {
        if (string.IsNullOrWhiteSpace(compactPath))
            return [];

        var result = new List<string>();
        var i = 0;

        while (i < compactPath.Length)
        {
            // Check for a repeat count
            var count = 0;
            while (i < compactPath.Length && char.IsDigit(compactPath[i]))
            {
                count = count * 10 + (compactPath[i] - '0');
                i++;
            }

            if (count == 0)
                count = 1;

            if (i >= compactPath.Length)
                break;

            // Read direction (single char abbreviation)
            var abbr = compactPath[i];
            i++;

            var entry = _dictionary.GetByAbbreviation(abbr);
            var direction = entry?.FullName ?? abbr.ToString();

            for (var j = 0; j < count; j++)
                result.Add(direction);
        }

        return result;
    }

    public string Collapse(IEnumerable<string> directions)
    {
        var sb = new StringBuilder();
        string? prev = null;
        var count = 0;

        foreach (var dir in directions)
        {
            var normalized = _dictionary.Normalize(dir);

            if (normalized == prev)
            {
                count++;
            }
            else
            {
                if (prev is not null)
                    AppendDirection(sb, prev, count);

                prev = normalized;
                count = 1;
            }
        }

        if (prev is not null)
            AppendDirection(sb, prev, count);

        return sb.ToString();
    }

    public IReadOnlyList<string>? Reverse(string compactPath)
    {
        var expanded = Expand(compactPath);
        var reversed = new List<string>(expanded.Count);

        for (var i = expanded.Count - 1; i >= 0; i--)
        {
            var opposite = _dictionary.GetOpposite(expanded[i]);
            if (opposite is null)
                return null; // Cannot reverse if we don't know the opposite

            reversed.Add(opposite);
        }

        return reversed;
    }

    public bool IsValid(string compactPath)
    {
        if (string.IsNullOrWhiteSpace(compactPath))
            return false;

        var i = 0;
        while (i < compactPath.Length)
        {
            // Skip digits
            while (i < compactPath.Length && char.IsDigit(compactPath[i]))
                i++;

            if (i >= compactPath.Length)
                return false; // Ends with a number

            if (!_dictionary.IsKnownDirection(compactPath[i].ToString()))
                return false;

            i++;
        }

        return true;
    }

    private void AppendDirection(StringBuilder sb, string direction, int count)
    {
        var entry = _dictionary.GetByName(direction);
        var abbr = entry?.Abbreviation.ToString() ?? direction;

        if (count > 1)
            sb.Append(count);

        sb.Append(abbr);
    }
}
