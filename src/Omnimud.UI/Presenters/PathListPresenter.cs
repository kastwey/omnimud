using Omnimud.Data.Entities;
using Omnimud.Data.Repositories;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;
using Omnimud.UI.Services.Accessibility;

namespace Omnimud.UI.Presenters;

public sealed class PathListPresenter(
    IPathRepository repository, int characterId, IUserPrompts prompts, IListSortStore? sortStore = null, IAnnouncer? announcer = null)
    : EntityListPresenter<PathEntity>(characterId, "paths", prompts, sortStore, announcer)
{
    public const string ColName = "name";
    public const string ColPath = "path";

    public override IReadOnlyList<SortColumn> SortColumns =>
    [
        new(ColName, Strings.PathList_ColName),
        new(ColPath, Strings.PathList_ColPath),
    ];

    protected override ListSort DefaultSort => new(ColName);

    protected override Task<IReadOnlyList<PathEntity>> FetchAsync(CancellationToken ct) => repository.GetByCharacterAsync(CharacterId, ct);
    protected override object KeyOf(PathEntity item) => item.Id;
    protected override string NameOf(PathEntity item) => item.Name;
    protected override IComparable? SortValue(PathEntity item, string column) => column == ColPath ? item.Path : item.Name;

    protected override async Task InsertAsync(PathEntity item, CancellationToken ct)
    {
        item.CharacterId = CharacterId;
        item.Id = await repository.AddAsync(item, ct);
    }

    protected override Task UpdateAsync(PathEntity item, CancellationToken ct) => repository.UpdateAsync(item, ct);
    protected override Task DeleteAsync(PathEntity item, CancellationToken ct) => repository.DeleteAsync(item.Id, ct);
    protected override string ConfirmRemoveMessage(PathEntity item) => string.Format(Strings.PathList_ConfirmRemove, item.Name);
    protected override string DuplicateMessage(PathEntity item) => string.Format(Strings.PathEdit_ErrDuplicateName, item.Name);
}
