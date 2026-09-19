namespace Omnimud.Core.Aliases;

public interface IAliasResolver
{
    /// <summary>
    /// Resolves aliases in the input. If the first word matches an alias,
    /// it is replaced with the alias action.
    /// Returns the original input if no alias matches.
    /// </summary>
    string Resolve(string input);

    void Load(IEnumerable<AliasDefinition> aliases);
    void Add(AliasDefinition alias);
    void Remove(string command);
    AliasDefinition? Get(string command);
    IReadOnlyList<AliasDefinition> GetAll();
}
