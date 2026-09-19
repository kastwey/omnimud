namespace Omnimud.Core.Paths;

public interface IPathEngine
{
    /// <summary>Expands a compact path (e.g. "3s2e") into individual commands.</summary>
    IReadOnlyList<string> Expand(string compactPath);

    /// <summary>Collapses an expanded path into compact form.</summary>
    string Collapse(IEnumerable<string> directions);

    /// <summary>Reverses a path using the direction dictionary.</summary>
    IReadOnlyList<string>? Reverse(string compactPath);

    /// <summary>Validates that all directions in the path are known.</summary>
    bool IsValid(string compactPath);
}

public interface IDirectionDictionary
{
    void Load(IEnumerable<DirectionEntry> entries);
    DirectionEntry? GetByAbbreviation(char abbr);
    DirectionEntry? GetByName(string name);
    bool IsKnownDirection(string direction);
    string? GetOpposite(string direction);
    string Normalize(string direction);
    IReadOnlyList<DirectionEntry> GetAll();
}
