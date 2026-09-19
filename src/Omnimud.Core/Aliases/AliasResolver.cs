namespace Omnimud.Core.Aliases;

public sealed class AliasResolver : IAliasResolver
{
    private readonly Dictionary<string, AliasDefinition> _aliases;

    /// <summary>Case-insensitive lookup (what earlier callers expect).</summary>
    public AliasResolver() : this(caseSensitive: false)
    {
    }

    /// <summary>The session uses the original client's rule: exact, case-sensitive match.</summary>
    public AliasResolver(bool caseSensitive)
    {
        _aliases = new Dictionary<string, AliasDefinition>(
            caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
    }

    public string Resolve(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        var spaceIndex = input.IndexOf(' ');
        var firstWord = spaceIndex == -1 ? input : input[..spaceIndex];
        var rest = spaceIndex == -1 ? string.Empty : input[spaceIndex..];

        if (_aliases.TryGetValue(firstWord, out var alias) && alias.Enabled)
            return alias.Action + rest;

        return input;
    }

    public void Load(IEnumerable<AliasDefinition> aliases)
    {
        _aliases.Clear();
        foreach (var alias in aliases)
            _aliases[alias.Command] = alias;
    }

    public void Add(AliasDefinition alias)
    {
        _aliases[alias.Command] = alias;
    }

    public void Remove(string command)
    {
        _aliases.Remove(command);
    }

    public AliasDefinition? Get(string command)
    {
        return _aliases.TryGetValue(command, out var alias) ? alias : null;
    }

    public IReadOnlyList<AliasDefinition> GetAll()
    {
        return _aliases.Values.ToList();
    }
}
