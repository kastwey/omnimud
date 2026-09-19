using Omnimud.Core.Options;
using Omnimud.Data.Repositories;

namespace Omnimud.Data.Options;

/// <summary>
/// Options with the original client's inheritance BY BLOCK: a scope that stores any row owns a
/// complete set; a scope without rows inherits the whole set of the level above
/// (character → MUD → global → defaults). Nothing is cached, so every call sees what is stored.
/// </summary>
public sealed class OptionsService : IOptionsService
{
    private readonly IOptionRepository _repository;

    public OptionsService(IOptionRepository repository)
    {
        _repository = repository;
    }

    public event EventHandler<OptionsChangedEventArgs>? Changed;

    public async Task<OmnimudOptions> ResolveAsync(int? mudId, int? characterId, CancellationToken ct = default)
    {
        if (characterId is not null)
        {
            var own = await LoadAsync(OptionScope.Character, characterId, ct).ConfigureAwait(false);
            if (own is not null) return own;
        }

        if (mudId is not null)
        {
            var own = await LoadAsync(OptionScope.Mud, mudId, ct).ConfigureAwait(false);
            if (own is not null) return own;
        }

        return await LoadAsync(OptionScope.Global, null, ct).ConfigureAwait(false) ?? new OmnimudOptions();
    }

    /// <summary>The scope's own set, or null when it inherits. Useful for the options dialog.</summary>
    public Task<OmnimudOptions?> GetOwnAsync(OptionScope scope, int? scopeId, CancellationToken ct = default)
    {
        scopeId = NormalizeScopeId(scope, scopeId);
        return LoadAsync(scope, scopeId, ct);
    }

    public Task<bool> HasOwnOptionsAsync(OptionScope scope, int? scopeId, CancellationToken ct = default)
    {
        scopeId = NormalizeScopeId(scope, scopeId);
        return _repository.HasAnyAsync((int)scope, scopeId, ct);
    }

    public async Task SaveAsync(OptionScope scope, int? scopeId, OmnimudOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        scopeId = NormalizeScopeId(scope, scopeId);
        // Replacing the whole block also drops legacy keys (SoundEnabled, FlashOnMessage), whose
        // values have already been folded into the new ones when the set was loaded.
        await _repository.ReplaceAllAsync((int)scope, scopeId, OptionsSerializer.Serialize(options), ct).ConfigureAwait(false);
        NotifyChanged(scope, scopeId);
    }

    /// <summary>For Global there is nothing to inherit from: its rows are removed, which means built-in defaults.</summary>
    public async Task ResetToInheritedAsync(OptionScope scope, int? scopeId, CancellationToken ct = default)
    {
        scopeId = NormalizeScopeId(scope, scopeId);
        await _repository.DeleteAllByScopeAsync((int)scope, scopeId, ct).ConfigureAwait(false);
        NotifyChanged(scope, scopeId);
    }

    /// <summary>Raises <see cref="Changed"/> for writers that bypass this service (import, legacy dialogs).</summary>
    public void NotifyChanged(OptionScope scope, int? scopeId) =>
        Changed?.Invoke(this, new OptionsChangedEventArgs(scope, scope == OptionScope.Global ? null : scopeId));

    private async Task<OmnimudOptions?> LoadAsync(OptionScope scope, int? scopeId, CancellationToken ct)
    {
        var rows = await _repository.GetByScope((int)scope, scopeId, ct).ConfigureAwait(false);
        return rows.Count == 0
            ? null
            : OptionsSerializer.Deserialize(rows.Select(r => new KeyValuePair<string, string>(r.Key, r.Value)));
    }

    private static int? NormalizeScopeId(OptionScope scope, int? scopeId)
    {
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));
        if (scope == OptionScope.Global) return null;
        return scopeId ?? throw new ArgumentException($"The {scope} scope needs the id of the {scope}.", nameof(scopeId));
    }
}
