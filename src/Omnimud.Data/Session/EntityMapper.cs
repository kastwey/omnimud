using Omnimud.Core.Aliases;
using Omnimud.Core.Paths;
using Omnimud.Core.Session;
using Omnimud.Core.Triggers;
using Omnimud.Data.Entities;

namespace Omnimud.Data.Session;

/// <summary>Entity ↔ Core type conversions. Unknown enum numbers stored in the database map to the default member.</summary>
public static class EntityMapper
{
    public static TriggerDefinition ToDefinition(TriggerEntity entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Pattern = entity.Pattern,
        PatternType = ToEnum(entity.PatternType, PatternType.Literal),
        Action = entity.Action ?? string.Empty,
        ActionType = ToEnum(entity.ActionType, TriggerActionType.SendCommand),
        Sound = entity.Sound,
        Enabled = entity.Enabled,
        CaseSensitive = entity.CaseSensitive,
        Priority = entity.Priority,
        Multiline = entity.Multiline,
        GagLine = entity.GagLine
    };

    public static TriggerEntity ToEntity(TriggerDefinition definition, int characterId, int sortOrder = 0) => new()
    {
        Id = definition.Id,
        CharacterId = characterId,
        Name = definition.Name,
        Pattern = definition.Pattern,
        PatternType = (int)definition.PatternType,
        Action = definition.Action,
        ActionType = (int)definition.ActionType,
        Sound = definition.Sound,
        Enabled = definition.Enabled,
        CaseSensitive = definition.CaseSensitive,
        Priority = definition.Priority,
        Multiline = definition.Multiline,
        GagLine = definition.GagLine,
        SortOrder = sortOrder
    };

    public static AliasDefinition ToDefinition(AliasEntity entity) => new(entity.Command, entity.Action, entity.Enabled);

    public static AliasEntity ToEntity(AliasDefinition definition, int characterId) => new()
    {
        CharacterId = characterId,
        Command = definition.Command,
        Action = definition.Action,
        Enabled = definition.Enabled
    };

    public static PathDefinition ToDefinition(PathEntity entity) => new(entity.Name, entity.Path);

    public static PathEntity ToEntity(PathDefinition definition, int characterId) => new()
    {
        CharacterId = characterId,
        Name = definition.Name,
        Path = definition.Path
    };

    /// <summary>Null when the stored abbreviation is not exactly one character (cannot happen through the repository).</summary>
    public static DirectionEntry? ToEntry(DirectionEntity entity) =>
        entity.Abbreviation is { Length: 1 } abbreviation
            ? new DirectionEntry(entity.Direction, abbreviation[0], string.IsNullOrEmpty(entity.OppositeDirection) ? null : entity.OppositeDirection)
            : null;

    public static DirectionEntity ToEntity(DirectionEntry entry, int mudId) => new()
    {
        MudId = mudId,
        Direction = entry.FullName,
        Abbreviation = entry.Abbreviation.ToString(),
        OppositeDirection = entry.Opposite
    };

    public static MessageRule ToRule(MessageRuleEntity entity) =>
        new(entity.Pattern, entity.Template, entity.CaseSensitive, string.IsNullOrEmpty(entity.Channel) ? null : entity.Channel);

    private static TEnum ToEnum<TEnum>(int value, TEnum fallback) where TEnum : struct, Enum
    {
        var candidate = (TEnum)Enum.ToObject(typeof(TEnum), value);
        return Enum.IsDefined(candidate) ? candidate : fallback;
    }
}
