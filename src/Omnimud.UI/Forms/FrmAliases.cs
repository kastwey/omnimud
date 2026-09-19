using Omnimud.Data.Entities;
using Omnimud.Data.Exchange;
using Omnimud.Data.Repositories;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;
using Omnimud.UI.Services.Accessibility;

namespace Omnimud.UI.Forms;

/// <summary>Aliases of a character (F5).</summary>
public sealed class FrmAliases : EntityListForm<AliasEntity>
{
    private readonly AliasListPresenter _presenter;

    /// <summary>Reduced: real message boxes, order remembered in memory, no import/export.</summary>
    public FrmAliases(IAliasRepository aliases, int characterId, string characterName)
        : this(aliases, characterId, characterName, new WinFormsUserPrompts(), new FormAnnouncer())
    {
    }

    /// <param name="sortStore">Null = the order is remembered only while the program runs.</param>
    /// <param name="exchange">Null = Import and Export are disabled.</param>
    /// <param name="characters">With <paramref name="muds"/>, enables "From another character".</param>
    /// <param name="conflicts">Null = the shared conflicts dialog (FrmImportConflicts).</param>
    public FrmAliases(IAliasRepository aliases, int characterId, string characterName, IUserPrompts prompts, IAnnouncer announcer,
        IListSortStore? sortStore = null, IExchangeService? exchange = null, ICharacterRepository? characters = null,
        IMudRepository? muds = null, IImportConflictResolver? conflicts = null)
        : this(new AliasListPresenter(aliases, characterId, prompts, sortStore, announcer), prompts, announcer,
            ListFormServices.Exchange(exchange, prompts, conflicts, ExchangeParts.Aliases, characterId, characterName),
            ListFormServices.OtherCharacters(characters, muds, characterId), characterName)
    {
    }

    private FrmAliases(AliasListPresenter presenter, IUserPrompts prompts, IAnnouncer announcer, ListExchangePresenter? exchange,
        Func<Task<IReadOnlyList<CharacterChoice>>>? otherCharacters, string characterName)
        : base(presenter, prompts, announcer, exchange, otherCharacters)
    {
        _presenter = presenter;
        Name = nameof(FrmAliases);
        Build(new ListFormTexts(string.Format(Strings.AliasList_Title, characterName), Strings.AliasList_Label, Strings.AliasList_Name),
            [(Strings.AliasList_ColCommand, 150), (Strings.AliasList_ColAction, 360), (Strings.Lst_ColEnabled, 90)],
            hasToggle: true, hasMove: false);
    }

    protected override string[] Cells(AliasEntity item) => [item.Command, item.Action, item.Enabled ? Strings.Lst_Yes : Strings.Lst_No];
    protected override bool IsItemEnabled(AliasEntity item) => item.Enabled;
    protected override Task ToggleAsync() => _presenter.ToggleSelectedAsync();

    protected override string? SortKeyOfColumn(int columnIndex) => columnIndex switch
    {
        0 => AliasListPresenter.ColCommand,
        1 => AliasListPresenter.ColAction,
        2 => AliasListPresenter.ColEnabled,
        _ => null,
    };

    protected override AliasEntity? ShowEditor(AliasEntity? current, bool isNew, IReadOnlyList<AliasEntity> all)
    {
        var model = new AliasEditorModel(current, isNew, all);
        using var dialog = new FrmAddEditAlias(model, Prompts);
        return dialog.ShowDialog(this) == DialogResult.OK ? model.ToEntity() : null;
    }
}
