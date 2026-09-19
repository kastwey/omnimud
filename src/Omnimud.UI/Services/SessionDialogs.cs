using Omnimud.Core.Options;
using Omnimud.Core.Scripting;
using Omnimud.Core.Session;
using Omnimud.Core.Sound;
using Omnimud.Data.Entities;
using Omnimud.Data.Exchange;
using Omnimud.Data.Repositories;
using Omnimud.UI.Forms;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Services.Accessibility;

namespace Omnimud.UI.Services;

/// <summary>Opens the management dialogs from a game window, each with its full set of dependencies.</summary>
internal sealed class SessionDialogs(
    IAliasRepository aliases,
    ITriggerRepository triggers,
    IPathRepository paths,
    IDirectionRepository directions,
    IMovementRepository movements,
    IMudRepository muds,
    ICharacterRepository characters,
    IOptionRepository optionRows,
    IOptionsService options,
    IExchangeService exchange,
    ISoundPlayer soundPlayer,
    TimeProvider time,
    Omnimud.Core.Security.IProxyCredentialStore? proxyCredentials = null) : ISessionDialogs
{
    private readonly IListSortStore _sortStore = new OptionListSortStore(optionRows);

    public bool ShowAliases(IWin32Window owner, SessionProfile profile)
    {
        if (profile.CharacterId is not { } id) return false;
        using var form = new FrmAliases(aliases, id, NameOf(profile), Prompts(owner), AnnouncerFor(owner, profile),
            _sortStore, exchange, characters, muds);
        form.ShowDialog(owner);
        return form.Changed;
    }

    public bool ShowTriggers(IWin32Window owner, SessionProfile profile)
    {
        if (profile.CharacterId is not { } id) return false;
        // The engine only validates and test-runs scripts here; the session has its own.
        using var engine = new LuaScriptEngine(time);
        using var form = new FrmTriggers(triggers, id, NameOf(profile), Prompts(owner), AnnouncerFor(owner, profile), engine,
            _sortStore, exchange, characters, muds, conflicts: null, soundPlayer);
        form.ShowDialog(owner);
        return form.Changed;
    }

    public bool ShowPaths(IWin32Window owner, SessionProfile profile)
    {
        if (profile.CharacterId is not { } id) return false;
        using var form = new FrmPaths(paths, id, NameOf(profile), Prompts(owner), AnnouncerFor(owner, profile),
            directions, profile.MudId, _sortStore, exchange, characters, muds);
        form.ShowDialog(owner);
        return form.Changed;
    }

    public bool ShowNewPath(IWin32Window owner, SessionProfile profile, string recordedPath)
    {
        if (profile.CharacterId is not { } id) return false;

        var existing = Task.Run(() => paths.GetByCharacterAsync(id)).GetAwaiter().GetResult();
        // Without a saved MUD there is no direction dictionary: the path is then stored as recorded.
        var dictionary = profile.MudId is { } mudId
            ? Task.Run(() => PathEditorModel.LoadDirectionsAsync(directions, mudId)).GetAwaiter().GetResult()
            : null;

        var entity = new PathEntity { CharacterId = id, Name = string.Empty, Path = recordedPath };
        var model = new PathEditorModel(entity, isNew: true, existing, dictionary);
        using var form = new FrmAddEditPath(model, Prompts(owner), AnnouncerFor(owner, profile));
        if (form.ShowDialog(owner) != DialogResult.OK) return false;

        Task.Run(() => paths.AddAsync(new PathEntity { CharacterId = id, Name = form.PathName, Path = form.PathValue })).GetAwaiter().GetResult();
        return true;
    }

    public bool ShowOptions(IWin32Window owner, SessionProfile profile)
    {
        // Character level when there is one, else MUD level, else global: as F9 did in the original.
        var (scope, scopeId, name) = profile switch
        {
            { CharacterId: { } character } => (OptionScope.Character, (int?)character, profile.CharacterName ?? profile.Title),
            { MudId: { } mud } => (OptionScope.Mud, (int?)mud, profile.MudName ?? profile.Title),
            _ => (OptionScope.Global, null, string.Empty),
        };
        using var form = new FrmOptions(options, scope, scopeId, name, Prompts(owner),
            parentMudId: scope == OptionScope.Character ? profile.MudId : null, exchange: exchange,
            credentials: proxyCredentials);
        return form.ShowDialog(owner) == DialogResult.OK;
    }

    public bool ShowMovementKeys(IWin32Window owner, SessionProfile profile)
    {
        var (mudId, characterId) = profile.CharacterId is { } c ? ((int?)null, (int?)c) : (profile.MudId, null);
        if (mudId is null && characterId is null)
        {
            Prompts(owner).Info(Strings.Common_NeedsMud);
            return false;
        }

        // A character without keys of its own follows the MUD's, as the original client did; keys
        // nobody configured send the default command for the language (MovementKeysEditor has the rules).
        var (own, inherited) = Task.Run(async () =>
        {
            var ownRows = characterId is { } id ? await movements.GetByCharacterAsync(id) : await movements.GetByMudAsync(mudId!.Value);
            var mudRows = characterId is not null && profile.MudId is { } mud ? await movements.GetByMudAsync(mud) : [];
            return (ownRows.ToDictionary(m => m.KeyCode, m => m.Command), mudRows.ToDictionary(m => m.KeyCode, m => m.Command));
        }).GetAwaiter().GetResult();

        var ownerName = characterId is not null ? profile.CharacterName ?? profile.Title : profile.MudName ?? profile.Title;
        var editor = new MovementKeysEditor(own, inherited,
            MovementKeys.DefaultCommands(Strings.Culture ?? System.Globalization.CultureInfo.CurrentUICulture));
        using var form = new FrmMovements(ownerName, characterId is not null, editor);
        if (form.ShowDialog(owner) != DialogResult.OK) return false;

        var commands = form.Commands;
        Task.Run(() => movements.ReplaceAllAsync(mudId, characterId, commands)).GetAwaiter().GetResult();
        return true;
    }

    public bool ShowDirections(IWin32Window owner, SessionProfile profile)
    {
        if (profile.MudId is not { } mudId)
        {
            Prompts(owner).Info(Strings.Common_NeedsMud);
            return false;
        }

        using var form = new FrmDirections(directions, mudId, profile.MudName ?? profile.Title);
        form.ShowDialog(owner);
        return form.Changed;
    }

    private static string NameOf(SessionProfile profile) => profile.CharacterName ?? profile.Title;

    private static IUserPrompts Prompts(IWin32Window owner) => new WinFormsUserPrompts(owner);

    /// <summary>Dialog announcements use the screen reader mode of the session that opened them.</summary>
    private IAnnouncer AnnouncerFor(IWin32Window owner, SessionProfile profile)
    {
        var mode = Task.Run(() => options.ResolveAsync(profile.MudId, profile.CharacterId)).GetAwaiter().GetResult().ScreenReader;
        IUiaNotifier notifier = owner is Control control ? new ControlUiaNotifier(control) : new NullUiaNotifier();
        return new Announcer(notifier) { Mode = mode };
    }

    private sealed class NullUiaNotifier : IUiaNotifier
    {
        public bool Raise(string text, System.Windows.Forms.Automation.AutomationNotificationProcessing processing) => false;
    }
}
