using Omnimud.Core.Scripting;
using Omnimud.Core.Sound;
using Omnimud.Data.Entities;
using Omnimud.Data.Exchange;
using Omnimud.Data.Repositories;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;
using Omnimud.UI.Services.Accessibility;

namespace Omnimud.UI.Forms;

/// <summary>Triggers of a character (F6). Their order matters, so they can be moved with Alt+arrows.</summary>
public sealed class FrmTriggers : EntityListForm<TriggerEntity>
{
    private readonly TriggerListPresenter _presenter;
    private readonly IScriptEngine _scriptEngine;
    private readonly bool _ownsEngine;
    private readonly IAnnouncer _announcer;
    private readonly ISoundPlayer? _soundPlayer;

    /// <summary>Reduced: real message boxes, a script engine of its own to validate and test Lua, no import/export.</summary>
    public FrmTriggers(ITriggerRepository triggers, int characterId, string characterName)
        : this(triggers, characterId, characterName, new WinFormsUserPrompts(), new FormAnnouncer(), scriptEngine: null)
    {
    }

    /// <param name="scriptEngine">Validates and tests Lua in the editor. Null = the window creates (and disposes) one.</param>
    /// <param name="sortStore">Null = the order is remembered only while the program runs.</param>
    /// <param name="exchange">Null = Import and Export are disabled.</param>
    /// <param name="characters">With <paramref name="muds"/>, enables "From another character".</param>
    /// <param name="conflicts">Null = the shared conflicts dialog (FrmImportConflicts).</param>
    /// <param name="soundPlayer">Null = the editor has no "Play sound" button.</param>
    public FrmTriggers(ITriggerRepository triggers, int characterId, string characterName, IUserPrompts prompts, IAnnouncer announcer,
        IScriptEngine? scriptEngine, IListSortStore? sortStore = null, IExchangeService? exchange = null,
        ICharacterRepository? characters = null, IMudRepository? muds = null, IImportConflictResolver? conflicts = null,
        ISoundPlayer? soundPlayer = null)
        : this(new TriggerListPresenter(triggers, characterId, prompts, sortStore, announcer), prompts, announcer,
            ListFormServices.Exchange(exchange, prompts, conflicts, ExchangeParts.Triggers, characterId, characterName),
            ListFormServices.OtherCharacters(characters, muds, characterId), characterName, scriptEngine, soundPlayer)
    {
    }

    private FrmTriggers(TriggerListPresenter presenter, IUserPrompts prompts, IAnnouncer announcer, ListExchangePresenter? exchange,
        Func<Task<IReadOnlyList<CharacterChoice>>>? otherCharacters, string characterName, IScriptEngine? scriptEngine, ISoundPlayer? soundPlayer)
        : base(presenter, prompts, announcer, exchange, otherCharacters)
    {
        _presenter = presenter;
        _announcer = announcer;
        _ownsEngine = scriptEngine is null;
        _scriptEngine = scriptEngine ?? new LuaScriptEngine();
        _soundPlayer = soundPlayer;

        Name = nameof(FrmTriggers);
        Build(new ListFormTexts(string.Format(Strings.TrigList_Title, characterName), Strings.TrigList_Label, Strings.TrigList_Name),
            [
                (Strings.TrigList_ColName, 130), (Strings.TrigList_ColPattern, 170), (Strings.TrigList_ColType, 100),
                (Strings.TrigList_ColAction, 100), (Strings.TrigList_ColPriority, 50), (Strings.Lst_ColEnabled, 50),
            ],
            hasToggle: true, hasMove: true);
    }

    protected override string[] Cells(TriggerEntity item) =>
    [
        item.Name, item.Pattern, TriggerEditorModel.PatternTypeName(item.PatternType), TriggerEditorModel.ActionTypeName(item.ActionType),
        item.Priority.ToString(), item.Enabled ? Strings.Lst_Yes : Strings.Lst_No,
    ];

    protected override bool IsItemEnabled(TriggerEntity item) => item.Enabled;
    protected override Task ToggleAsync() => _presenter.ToggleSelectedAsync();
    protected override Task MoveAsync(int delta) => _presenter.MoveSelectedAsync(delta);

    protected override string? SortKeyOfColumn(int columnIndex) => columnIndex switch
    {
        0 => TriggerListPresenter.ColName,
        1 => TriggerListPresenter.ColPattern,
        2 => TriggerListPresenter.ColType,
        3 => TriggerListPresenter.ColAction,
        4 => TriggerListPresenter.ColPriority,
        5 => TriggerListPresenter.ColEnabled,
        _ => null,
    };

    protected override TriggerEntity? ShowEditor(TriggerEntity? current, bool isNew, IReadOnlyList<TriggerEntity> all)
    {
        var model = new TriggerEditorModel(current, isNew, all);
        using var dialog = new FrmAddEditTrigger(model, Prompts, _scriptEngine, _announcer, _soundPlayer);
        return dialog.ShowDialog(this) == DialogResult.OK ? model.ToEntity() : null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _ownsEngine) _scriptEngine.Dispose();
        base.Dispose(disposing);
    }
}
