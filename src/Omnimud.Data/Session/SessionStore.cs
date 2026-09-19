using Omnimud.Core.Aliases;
using Omnimud.Core.Paths;
using Omnimud.Core.Session;
using Omnimud.Core.Triggers;
using Omnimud.Data.Entities;
using Omnimud.Data.Repositories;

namespace Omnimud.Data.Session;

/// <summary>Adapter from the repositories to what a session needs, in Core types.</summary>
public sealed class SessionStore : ISessionStore
{
    private readonly IMudRepository _muds;
    private readonly ICharacterRepository _characters;
    private readonly IAliasRepository _aliases;
    private readonly ITriggerRepository _triggers;
    private readonly IPathRepository _paths;
    private readonly IDirectionRepository _directions;
    private readonly IMovementRepository _movements;
    private readonly IMessageRuleRepository _messageRules;

    public SessionStore(
        IMudRepository muds,
        ICharacterRepository characters,
        IAliasRepository aliases,
        ITriggerRepository triggers,
        IPathRepository paths,
        IDirectionRepository directions,
        IMovementRepository movements,
        IMessageRuleRepository messageRules)
    {
        _muds = muds;
        _characters = characters;
        _aliases = aliases;
        _triggers = triggers;
        _paths = paths;
        _directions = directions;
        _movements = movements;
        _messageRules = messageRules;
    }

    /// <summary>Only enabled aliases: the resolver never has to look at the flag.</summary>
    public async Task<IReadOnlyList<AliasDefinition>> GetAliasesAsync(int characterId, CancellationToken ct = default)
    {
        var entities = await _aliases.GetByCharacterAsync(characterId, ct).ConfigureAwait(false);
        return entities.Where(a => a.Enabled).Select(EntityMapper.ToDefinition).ToList();
    }

    public async Task<bool> AddAliasAsync(int characterId, string command, string action, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        try
        {
            await _aliases.AddAsync(new AliasEntity { CharacterId = characterId, Command = command, Action = action, Enabled = true }, ct).ConfigureAwait(false);
            return true;
        }
        catch (DuplicateEntityException)
        {
            return false;
        }
    }

    public Task<bool> RemoveAliasAsync(int characterId, string command, CancellationToken ct = default) =>
        _aliases.RemoveByCommandAsync(characterId, command, ct);

    /// <summary>Every trigger, disabled ones included ("+trigger N" can enable them), in creation order.</summary>
    public async Task<IReadOnlyList<TriggerDefinition>> GetTriggersAsync(int characterId, CancellationToken ct = default)
    {
        var entities = await _triggers.GetByCharacterAsync(characterId, ct).ConfigureAwait(false);
        return entities.Select(EntityMapper.ToDefinition).ToList();
    }

    public Task SetTriggerEnabledAsync(string triggerId, bool enabled, CancellationToken ct = default) =>
        _triggers.SetEnabledAsync(triggerId, enabled, ct);

    public async Task<IReadOnlyList<PathDefinition>> GetPathsAsync(int characterId, CancellationToken ct = default)
    {
        var entities = await _paths.GetByCharacterAsync(characterId, ct).ConfigureAwait(false);
        return entities.Select(EntityMapper.ToDefinition).ToList();
    }

    public async Task<IReadOnlyList<DirectionEntry>> GetDirectionsAsync(int mudId, CancellationToken ct = default)
    {
        var entities = await _directions.GetByMudAsync(mudId, ct).ConfigureAwait(false);
        return entities.Select(EntityMapper.ToEntry).OfType<DirectionEntry>().ToList();
    }

    public async Task<IReadOnlyDictionary<int, string>> GetMovementsAsync(int? mudId, int? characterId, CancellationToken ct = default)
    {
        IReadOnlyList<MovementEntity> entities = [];
        if (characterId is not null)
            entities = await _movements.GetByCharacterAsync(characterId.Value, ct).ConfigureAwait(false);

        if (entities.Count == 0)
        {
            mudId ??= await GetMudIdOfAsync(characterId, ct).ConfigureAwait(false);
            if (mudId is not null)
                entities = await _movements.GetByMudAsync(mudId.Value, ct).ConfigureAwait(false);
        }

        return entities.ToDictionary(m => m.KeyCode, m => m.Command);
    }

    public async Task<bool> GetMovementModeAsync(int? mudId, int? characterId, CancellationToken ct = default)
    {
        if (characterId is not null)
        {
            var character = await _characters.GetByIdAsync(characterId.Value, ct).ConfigureAwait(false);
            if (character is not null) return character.MovementMode;
        }

        if (mudId is not null)
        {
            var mud = await _muds.GetByIdAsync(mudId.Value, ct).ConfigureAwait(false);
            if (mud is not null) return mud.MovementMode;
        }

        return false;
    }

    public async Task SetMovementModeAsync(int? mudId, int? characterId, bool enabled, CancellationToken ct = default)
    {
        if (characterId is not null)
            await _characters.SetMovementModeAsync(characterId.Value, enabled, ct).ConfigureAwait(false);
        else if (mudId is not null)
            await _muds.SetMovementModeAsync(mudId.Value, enabled, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MessageRule>> GetMessageRulesAsync(int mudId, CancellationToken ct = default)
    {
        var mud = await _muds.GetByIdAsync(mudId, ct).ConfigureAwait(false);
        if (mud?.MessageRuleSetId is not { } ruleSetId) return [];

        var rules = await _messageRules.GetRulesAsync(ruleSetId, ct).ConfigureAwait(false);
        return rules.Where(r => r.Enabled).Select(EntityMapper.ToRule).ToList();
    }

    public async Task<string?> GetMessageRuleScriptAsync(int mudId, CancellationToken ct = default)
    {
        var mud = await _muds.GetByIdAsync(mudId, ct).ConfigureAwait(false);
        if (mud?.MessageRuleSetId is not { } ruleSetId) return null;

        var set = await _messageRules.GetRuleSetByIdAsync(ruleSetId, ct).ConfigureAwait(false);
        return set is { IsScript: true } ? set.Script : null;
    }

    private async Task<int?> GetMudIdOfAsync(int? characterId, CancellationToken ct)
    {
        if (characterId is null) return null;
        var character = await _characters.GetByIdAsync(characterId.Value, ct).ConfigureAwait(false);
        return character?.MudId;
    }
}
