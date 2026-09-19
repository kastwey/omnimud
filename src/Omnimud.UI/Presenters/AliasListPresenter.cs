using Omnimud.Data.Entities;
using Omnimud.Data.Repositories;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;
using Omnimud.UI.Services.Accessibility;

namespace Omnimud.UI.Presenters;

public sealed class AliasListPresenter(
    IAliasRepository repository, int characterId, IUserPrompts prompts, IListSortStore? sortStore = null, IAnnouncer? announcer = null)
    : EntityListPresenter<AliasEntity>(characterId, "aliases", prompts, sortStore, announcer)
{
    public const string ColCommand = "command";
    public const string ColAction = "action";
    public const string ColEnabled = "enabled";

    public override IReadOnlyList<SortColumn> SortColumns =>
    [
        new(ColCommand, Strings.AliasList_ColCommand),
        new(ColAction, Strings.AliasList_ColAction),
        new(ColEnabled, Strings.Lst_ColEnabled),
    ];

    protected override ListSort DefaultSort => new(ColCommand);

    protected override Task<IReadOnlyList<AliasEntity>> FetchAsync(CancellationToken ct) => repository.GetByCharacterAsync(CharacterId, ct);
    protected override object KeyOf(AliasEntity item) => item.Id;
    protected override string NameOf(AliasEntity item) => item.Command;

    protected override IComparable? SortValue(AliasEntity item, string column) => column switch
    {
        ColAction => item.Action,
        ColEnabled => item.Enabled ? 0 : 1,
        _ => item.Command,
    };

    protected override async Task InsertAsync(AliasEntity item, CancellationToken ct)
    {
        item.CharacterId = CharacterId;
        item.Id = await repository.AddAsync(item, ct);
    }

    protected override Task UpdateAsync(AliasEntity item, CancellationToken ct) => repository.UpdateAsync(item, ct);
    protected override Task DeleteAsync(AliasEntity item, CancellationToken ct) => repository.DeleteAsync(item.Id, ct);
    protected override string ConfirmRemoveMessage(AliasEntity item) => string.Format(Strings.AliasList_ConfirmRemove, item.Command);
    protected override string DuplicateMessage(AliasEntity item) => string.Format(Strings.AliasEdit_ErrDuplicate, item.Command);

    /// <summary>Enables or disables the selected alias and says so: the focus does not show it.</summary>
    public async Task<bool> ToggleSelectedAsync(CancellationToken ct = default)
    {
        if (Selected is not { } alias) return false;

        alias.Enabled = !alias.Enabled;
        await repository.UpdateAsync(alias, ct);
        DataChanged = true;
        var message = string.Format(alias.Enabled ? Strings.AliasList_Enabled : Strings.AliasList_Disabled, alias.Command);
        SetStatus(message, announce: false);
        await ReloadAsync(alias.Id, SelectedIndex, ct);
        Announce(message);
        return true;
    }
}
