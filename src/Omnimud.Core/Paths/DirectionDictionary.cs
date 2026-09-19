namespace Omnimud.Core.Paths;

public sealed class DirectionDictionary : IDirectionDictionary
{
    private readonly List<DirectionEntry> _entries = [];
    private readonly Dictionary<char, DirectionEntry> _byAbbr = [];
    private readonly Dictionary<string, DirectionEntry> _byName = new(StringComparer.OrdinalIgnoreCase);

    public void Load(IEnumerable<DirectionEntry> entries)
    {
        _entries.Clear();
        _byAbbr.Clear();
        _byName.Clear();

        foreach (var entry in entries)
        {
            _entries.Add(entry);
            _byAbbr[entry.Abbreviation] = entry;
            _byName[entry.FullName] = entry;
        }
    }

    public DirectionEntry? GetByAbbreviation(char abbr)
        => _byAbbr.GetValueOrDefault(abbr);

    public DirectionEntry? GetByName(string name)
        => _byName.GetValueOrDefault(name);

    public bool IsKnownDirection(string direction)
        => _byName.ContainsKey(direction) ||
           (direction.Length == 1 && _byAbbr.ContainsKey(direction[0]));

    public string? GetOpposite(string direction)
    {
        if (_byName.TryGetValue(direction, out var entry))
            return entry.Opposite;

        if (direction.Length == 1 && _byAbbr.TryGetValue(direction[0], out entry))
            return entry.Opposite;

        return null;
    }

    public string Normalize(string direction)
    {
        if (_byName.ContainsKey(direction))
            return direction;

        if (direction.Length == 1 && _byAbbr.TryGetValue(direction[0], out var entry))
            return entry.FullName;

        return direction;
    }

    public IReadOnlyList<DirectionEntry> GetAll() => _entries;
}
