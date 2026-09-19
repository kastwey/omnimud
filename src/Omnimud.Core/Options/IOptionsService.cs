namespace Omnimud.Core.Options;

/// <summary>
/// Reads and writes options with the original client's inheritance model: a scope either has
/// its own complete set of options or inherits the whole set from the level above
/// (character → MUD → global → defaults).
/// </summary>
public interface IOptionsService
{
    /// <summary>Effective options for a session: character if it has its own, else MUD, else global, else defaults.</summary>
    Task<OmnimudOptions> ResolveAsync(int? mudId, int? characterId, CancellationToken ct = default);

    /// <summary>True when that scope stores its own options instead of inheriting.</summary>
    Task<bool> HasOwnOptionsAsync(OptionScope scope, int? scopeId, CancellationToken ct = default);

    /// <summary>Stores a complete set for the scope; from then on it stops inheriting.</summary>
    Task SaveAsync(OptionScope scope, int? scopeId, OmnimudOptions options, CancellationToken ct = default);

    /// <summary>Removes the scope's own options so it inherits again. Not valid for Global.</summary>
    Task ResetToInheritedAsync(OptionScope scope, int? scopeId, CancellationToken ct = default);

    /// <summary>Raised after SaveAsync/ResetToInheritedAsync so open sessions can re-resolve.</summary>
    event EventHandler<OptionsChangedEventArgs>? Changed;
}

public sealed class OptionsChangedEventArgs(OptionScope scope, int? scopeId) : EventArgs
{
    public OptionScope Scope { get; } = scope;
    public int? ScopeId { get; } = scopeId;
}
