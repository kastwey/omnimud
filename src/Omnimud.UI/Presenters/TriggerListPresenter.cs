using Omnimud.Data.Entities;
using Omnimud.Data.Repositories;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;
using Omnimud.UI.Services.Accessibility;

namespace Omnimud.UI.Presenters;

public sealed class TriggerListPresenter(
    ITriggerRepository repository, int characterId, IUserPrompts prompts, IListSortStore? sortStore = null, IAnnouncer? announcer = null)
    : EntityListPresenter<TriggerEntity>(characterId, "triggers", prompts, sortStore, announcer)
{
    /// <summary>Evaluation order among triggers of equal priority, and the N of "-trigger N".</summary>
    public const string ColOrder = "order";
    public const string ColName = "name";
    public const string ColPattern = "pattern";
    public const string ColType = "type";
    public const string ColAction = "action";
    public const string ColPriority = "priority";
    public const string ColEnabled = "enabled";

    public override IReadOnlyList<SortColumn> SortColumns =>
    [
        new(ColOrder, Strings.TrigList_SortOrder),
        new(ColName, Strings.TrigList_ColName),
        new(ColPattern, Strings.TrigList_ColPattern),
        new(ColType, Strings.TrigList_ColType),
        new(ColAction, Strings.TrigList_ColAction),
        new(ColPriority, Strings.TrigList_ColPriority),
        new(ColEnabled, Strings.Lst_ColEnabled),
    ];

    protected override ListSort DefaultSort => new(ColOrder);

    /// <summary>Moving only makes sense while the list shows the evaluation order.</summary>
    public bool CanReorder => Sort is { Column: ColOrder, Descending: false };

    protected override Task<IReadOnlyList<TriggerEntity>> FetchAsync(CancellationToken ct) => repository.GetByCharacterAsync(CharacterId, ct);
    protected override object KeyOf(TriggerEntity item) => item.Id;
    protected override string NameOf(TriggerEntity item) => item.Name;

    protected override IComparable? SortValue(TriggerEntity item, string column) => column switch
    {
        ColName => item.Name,
        ColPattern => item.Pattern,
        ColType => TriggerEditorModel.PatternTypeName(item.PatternType),
        ColAction => TriggerEditorModel.ActionTypeName(item.ActionType),
        ColPriority => -item.Priority, // ascending = evaluated first = highest priority
        ColEnabled => item.Enabled ? 0 : 1,
        _ => item.SortOrder,
    };

    protected override Task InsertAsync(TriggerEntity item, CancellationToken ct)
    {
        item.CharacterId = CharacterId;
        if (string.IsNullOrEmpty(item.Id)) item.Id = Guid.NewGuid().ToString();
        item.SortOrder = 0; // append
        item.CreatedAt = item.UpdatedAt = DateTime.UtcNow;
        return repository.AddAsync(item, ct);
    }

    protected override Task UpdateAsync(TriggerEntity item, CancellationToken ct)
    {
        item.UpdatedAt = DateTime.UtcNow;
        return repository.UpdateAsync(item, ct);
    }

    protected override Task DeleteAsync(TriggerEntity item, CancellationToken ct) => repository.DeleteAsync(item.Id, ct);
    protected override string ConfirmRemoveMessage(TriggerEntity item) => string.Format(Strings.TrigList_ConfirmRemove, item.Name);
    protected override string DuplicateMessage(TriggerEntity item) => string.Format(Strings.TrigEdit_ErrDuplicateName, item.Name);

    /// <summary>Enables or disables the selected trigger and says so ("Trigger X enabled").</summary>
    public async Task<bool> ToggleSelectedAsync(CancellationToken ct = default)
    {
        if (Selected is not { } trigger) return false;

        var enabled = !trigger.Enabled;
        await repository.SetEnabledAsync(trigger.Id, enabled, ct);
        DataChanged = true;
        var message = string.Format(enabled ? Strings.TrigList_Enabled : Strings.TrigList_Disabled, trigger.Name);
        SetStatus(message, announce: false);
        await ReloadAsync(trigger.Id, SelectedIndex, ct);
        Announce(message);
        return true;
    }

    /// <summary>Moves the selected trigger one place up (-1) or down (+1) in the evaluation order.</summary>
    public async Task<bool> MoveSelectedAsync(int delta, CancellationToken ct = default)
    {
        if (Selected is not { } trigger || delta == 0) return false;

        if (!CanReorder)
        {
            SetStatus(Strings.TrigList_MoveNeedsOrder);
            OnChanged();
            return false;
        }

        var target = SelectedIndex + Math.Sign(delta);
        if (target < 0 || target >= Items.Count)
        {
            SetStatus(string.Format(target < 0 ? Strings.TrigList_AlreadyFirst : Strings.TrigList_AlreadyLast, trigger.Name));
            OnChanged();
            return false;
        }

        var ids = Items.Select(t => t.Id).ToList();
        (ids[SelectedIndex], ids[target]) = (ids[target], ids[SelectedIndex]);
        await repository.ReorderAsync(CharacterId, ids, ct);
        DataChanged = true;

        var message = string.Format(Strings.TrigList_Moved, trigger.Name, target + 1, ids.Count);
        SetStatus(message, announce: false);
        await ReloadAsync(trigger.Id, target, ct);
        Announce(message);
        return true;
    }
}
