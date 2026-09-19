using Omnimud.Data.Entities;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Presenters;

/// <summary>Rules of the alias editor. Aliases are matched exactly and case-sensitively, so duplicates are too.</summary>
public sealed class AliasEditorModel
{
    public const string FieldCommand = "command";
    public const string FieldAction = "action";

    private readonly AliasEntity? _existing;
    private readonly IReadOnlyList<AliasEntity> _others;

    public AliasEditorModel(AliasEntity? existing, bool isNew, IReadOnlyList<AliasEntity>? all = null)
    {
        _existing = existing;
        IsNew = isNew;
        _others = (all ?? []).Where(a => isNew || existing is null || a.Id != existing.Id).ToList();
        Command = existing?.Command ?? string.Empty;
        Action = existing?.Action ?? string.Empty;
        Enabled = existing?.Enabled ?? true;
    }

    public bool IsNew { get; }
    public string Command { get; set; }
    public string Action { get; set; }
    public bool Enabled { get; set; }

    public EditorIssue? Validate()
    {
        var command = Command.Trim();
        var action = Action.Trim();

        if (command.Length == 0) return new(FieldCommand, Strings.AliasEdit_ErrCommandEmpty);
        if (command.Any(char.IsWhiteSpace)) return new(FieldCommand, Strings.AliasEdit_ErrCommandSpaces);
        if (action.Length == 0) return new(FieldAction, Strings.AliasEdit_ErrActionEmpty);
        if (command == action) return new(FieldAction, Strings.AliasEdit_ErrSameAsAction);
        if (_others.Any(a => a.Command == command)) return new(FieldCommand, string.Format(Strings.AliasEdit_ErrDuplicate, command));
        return null;
    }

    /// <summary>Questions the user must answer Yes to before saving.</summary>
    public IReadOnlyList<string> Warnings()
    {
        var action = Action.Trim();
        var same = _others.FirstOrDefault(a => a.Action == action);
        return same is null ? [] : [string.Format(Strings.AliasEdit_WarnSameAction, same.Command)];
    }

    public AliasEntity ToEntity() => new()
    {
        Id = IsNew ? 0 : _existing?.Id ?? 0,
        CharacterId = _existing?.CharacterId ?? 0,
        Command = Command.Trim(),
        Action = Action.Trim(),
        Enabled = Enabled,
    };
}
