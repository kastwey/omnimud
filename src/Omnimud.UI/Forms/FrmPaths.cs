using Omnimud.Core.Paths;
using Omnimud.Data.Entities;
using Omnimud.Data.Exchange;
using Omnimud.Data.Repositories;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;
using Omnimud.UI.Services.Accessibility;

namespace Omnimud.UI.Forms;

/// <summary>Paths of a character (F7).</summary>
public sealed class FrmPaths : EntityListForm<PathEntity>
{
    private readonly IAnnouncer _announcer;
    private readonly IDirectionRepository? _directionRepository;
    private readonly int? _mudId;
    private IReadOnlyList<DirectionEntry>? _directions;

    /// <summary>Reduced: real message boxes, paths are not checked against the MUD's directions, no import/export.</summary>
    public FrmPaths(IPathRepository paths, int characterId, string characterName)
        : this(paths, characterId, characterName, new WinFormsUserPrompts(), new FormAnnouncer())
    {
    }

    /// <param name="directions">With <paramref name="mudId"/>, paths are validated against the MUD's direction dictionary.</param>
    /// <param name="sortStore">Null = the order is remembered only while the program runs.</param>
    /// <param name="exchange">Null = Import and Export are disabled.</param>
    /// <param name="characters">With <paramref name="muds"/>, enables "From another character".</param>
    /// <param name="conflicts">Null = the shared conflicts dialog (FrmImportConflicts).</param>
    public FrmPaths(IPathRepository paths, int characterId, string characterName, IUserPrompts prompts, IAnnouncer announcer,
        IDirectionRepository? directions = null, int? mudId = null, IListSortStore? sortStore = null, IExchangeService? exchange = null,
        ICharacterRepository? characters = null, IMudRepository? muds = null, IImportConflictResolver? conflicts = null)
        : base(new PathListPresenter(paths, characterId, prompts, sortStore, announcer), prompts, announcer,
            ListFormServices.Exchange(exchange, prompts, conflicts, ExchangeParts.Paths, characterId, characterName),
            ListFormServices.OtherCharacters(characters, muds, characterId))
    {
        _announcer = announcer;
        _directionRepository = directions;
        _mudId = mudId;

        Name = nameof(FrmPaths);
        Build(new ListFormTexts(string.Format(Strings.PathList_Title, characterName), Strings.PathList_Label, Strings.PathList_Name),
            [(Strings.PathList_ColName, 200), (Strings.PathList_ColPath, 400)],
            hasToggle: false, hasMove: false);
    }

    internal override async Task InitializeAsync()
    {
        if (_directionRepository is not null && _mudId is { } mudId)
            _directions = await PathEditorModel.LoadDirectionsAsync(_directionRepository, mudId);
        await base.InitializeAsync();
    }

    protected override string[] Cells(PathEntity item) => [item.Name, item.Path];

    protected override string? SortKeyOfColumn(int columnIndex) => columnIndex switch
    {
        0 => PathListPresenter.ColName,
        1 => PathListPresenter.ColPath,
        _ => null,
    };

    protected override PathEntity? ShowEditor(PathEntity? current, bool isNew, IReadOnlyList<PathEntity> all)
    {
        var model = new PathEditorModel(current, isNew, all, _directions);
        using var dialog = new FrmAddEditPath(model, Prompts, _announcer);
        return dialog.ShowDialog(this) == DialogResult.OK ? model.ToEntity() : null;
    }
}
