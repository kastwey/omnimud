using Omnimud.Core.Security;
using Omnimud.Core.Session;
using Omnimud.Data.Entities;
using Omnimud.Data.Exchange;
using Omnimud.Data.Repositories;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;

namespace Omnimud.UI.Presenters;

/// <summary>The dialogs the launcher opens. The real ones are WinForms; tests answer in their place.</summary>
public interface ILauncherDialogs
{
    /// <summary>True when the MUD was saved (the model holds its id).</summary>
    bool EditMud(MudEditorModel model);
    /// <summary>True when the character was saved (the model holds its id).</summary>
    bool EditCharacter(CharacterEditorModel model);
    /// <summary>Null when cancelled.</summary>
    SessionProfile? QuickConnect();
}

/// <summary>Everything the launcher can do. Buttons, main menu, context menu and keys all run these.</summary>
public enum LauncherCommand
{
    Connect,
    QuickConnect,
    AddMud,
    AddCharacter,
    Edit,
    Remove,
    SetDefault,
    ClearDefault,
    ExportMud,
    ExportCharacter,
    Import
}

/// <summary>One entry of the context menu of the tree, already worded for the selected node.</summary>
/// <param name="Checked">"Set as default" on the character that already is the default.</param>
public sealed record LauncherContextEntry(LauncherCommand Command, string Text, bool Enabled, bool Checked = false);

/// <summary>What is selected in the tree: a MUD, or one of its characters.</summary>
public sealed record LauncherSelection(int MudId, int? CharacterId = null);

/// <summary>A MUD of the tree with its characters.</summary>
public sealed record LauncherMudNode(MudEntity Mud, IReadOnlyList<CharacterEntity> Characters)
{
    public string Text => string.Format(Strings.Launcher_NodeMud, Mud.Name, Mud.Host, Mud.Port);

    public static string CharacterText(CharacterEntity character) =>
        character.IsDefault ? string.Format(Strings.Launcher_NodeCharacterDefault, character.Name) : character.Name;
}

/// <summary>
/// Everything the start window does: the MUD → characters tree, which node stays selected after
/// each change (after removing, the neighbour), connecting, default character, import and export.
/// No WinForms here.
/// </summary>
public sealed class LauncherPresenter
{
    private readonly IMudRepository _muds;
    private readonly ICharacterRepository _characters;
    private readonly IMessageRuleRepository _rules;
    private readonly IPasswordProtector _protector;
    private readonly IUserPrompts _prompts;
    private readonly ILauncherDialogs _dialogs;
    private readonly ImportExportPresenter _exchange;
    private readonly Action<SessionProfile> _openSession;
    private readonly HashSet<int> _collapsed = [];

    public LauncherPresenter(IMudRepository muds, ICharacterRepository characters, IMessageRuleRepository rules,
        IPasswordProtector protector, IUserPrompts prompts, ILauncherDialogs dialogs, ImportExportPresenter exchange,
        Action<SessionProfile> openSession)
    {
        _muds = muds;
        _characters = characters;
        _rules = rules;
        _protector = protector;
        _prompts = prompts;
        _dialogs = dialogs;
        _exchange = exchange;
        _openSession = openSession;
    }

    public IReadOnlyList<LauncherMudNode> Nodes { get; private set; } = [];
    public LauncherSelection? Selection { get; private set; }
    /// <summary>Outcome of the last action, shown in the status label.</summary>
    public string Status { get; private set; } = string.Empty;

    /// <summary>The tree was reloaded: repaint it, restore expansion and select <see cref="Selection"/>.</summary>
    public event Action? Changed;
    /// <summary>Only <see cref="Status"/> changed.</summary>
    public event Action? StatusChanged;

    public LauncherMudNode? SelectedNode => Selection is null ? null : Nodes.FirstOrDefault(n => n.Mud.Id == Selection.MudId);
    public MudEntity? SelectedMud => SelectedNode?.Mud;
    public CharacterEntity? SelectedCharacter =>
        Selection?.CharacterId is { } id ? SelectedNode?.Characters.FirstOrDefault(c => c.Id == id) : null;

    public bool CanConnect => SelectedMud is not null;
    public bool CanEdit => SelectedMud is not null;
    public bool CanRemove => SelectedMud is not null;
    public bool CanAddCharacter => Nodes.Count > 0;
    public bool CanSetDefault => SelectedCharacter is { IsDefault: false };
    public bool CanClearDefault => SelectedCharacter is { IsDefault: true };
    public bool CanExportMud => SelectedMud is not null;
    public bool CanExportCharacter => SelectedCharacter is not null;

    // ── Commands ───────────────────────────────────────────────────────────

    public bool CanExecute(LauncherCommand command) => command switch
    {
        LauncherCommand.Connect => CanConnect,
        LauncherCommand.AddCharacter => CanAddCharacter,
        LauncherCommand.Edit => CanEdit,
        LauncherCommand.Remove => CanRemove,
        LauncherCommand.SetDefault => CanSetDefault,
        LauncherCommand.ClearDefault => CanClearDefault,
        LauncherCommand.ExportMud => CanExportMud,
        LauncherCommand.ExportCharacter => CanExportCharacter,
        _ => true,
    };

    /// <summary>The single entry point of every button, menu entry and key of the launcher.</summary>
    public Task ExecuteAsync(LauncherCommand command, CancellationToken ct = default)
    {
        if (!CanExecute(command)) return Task.CompletedTask;
        switch (command)
        {
            case LauncherCommand.Connect: Connect(); return Task.CompletedTask;
            case LauncherCommand.QuickConnect: QuickConnect(); return Task.CompletedTask;
            case LauncherCommand.AddMud: return AddMudAsync(ct);
            case LauncherCommand.AddCharacter: return AddCharacterAsync(ct);
            case LauncherCommand.Edit: return EditAsync(ct);
            case LauncherCommand.Remove: return RemoveAsync(ct);
            case LauncherCommand.SetDefault: return SetDefaultAsync(ct);
            case LauncherCommand.ClearDefault: return ClearDefaultAsync(ct);
            case LauncherCommand.ExportMud: return ExportMudAsync(ct);
            case LauncherCommand.ExportCharacter: return ExportCharacterAsync(ct);
            case LauncherCommand.Import: return ImportAsync(ct);
            default: return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Context menu of the tree for what is selected now: a MUD, a character, or nothing (empty
    /// tree or empty area). Same commands as the buttons, worded for the node.
    /// </summary>
    public IReadOnlyList<LauncherContextEntry> GetContextMenu()
    {
        LauncherContextEntry Entry(LauncherCommand command, string text, bool isChecked = false) =>
            new(command, text, CanExecute(command), isChecked);

        if (SelectedCharacter is { } character)
        {
            return
            [
                Entry(LauncherCommand.Connect, Strings.LauncherCtx_Connect),
                Entry(LauncherCommand.Edit, Strings.LauncherCtx_EditCharacter),
                Entry(LauncherCommand.Remove, Strings.LauncherCtx_DeleteCharacter),
                // Already the default: shown checked and disabled, so the state is heard, not only seen.
                Entry(LauncherCommand.SetDefault, Strings.LauncherCtx_SetDefault, character.IsDefault),
                Entry(LauncherCommand.ExportCharacter, Strings.LauncherCtx_ExportCharacter),
            ];
        }

        if (SelectedMud is not null)
        {
            return
            [
                Entry(LauncherCommand.Connect, Strings.LauncherCtx_Connect),
                Entry(LauncherCommand.Edit, Strings.LauncherCtx_EditMud),
                Entry(LauncherCommand.Remove, Strings.LauncherCtx_DeleteMud),
                Entry(LauncherCommand.AddCharacter, Strings.LauncherCtx_AddCharacter),
                Entry(LauncherCommand.ExportMud, Strings.LauncherCtx_ExportMud),
            ];
        }

        return GetEmptyAreaContextMenu();
    }

    /// <summary>A click on the empty area of the tree: the context menu is then the one without node.</summary>
    public IReadOnlyList<LauncherContextEntry> GetEmptyAreaContextMenu() =>
    [
        new(LauncherCommand.AddMud, Strings.LauncherCtx_AddMud, true),
        new(LauncherCommand.Import, Strings.LauncherCtx_Import, true),
    ];

    public bool IsExpanded(int mudId) => !_collapsed.Contains(mudId);

    public void SetExpanded(int mudId, bool expanded)
    {
        if (expanded) _collapsed.Remove(mudId);
        else _collapsed.Add(mudId);
    }

    /// <summary>The user moved through the tree.</summary>
    public void Select(LauncherSelection? selection)
    {
        if (selection is not null)
            Selection = selection;
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        await ReloadAsync(Selection, ct);
        SetStatus(string.Format(Strings.Launcher_MudsConfigured, Nodes.Count));
    }

    // ── Connect ────────────────────────────────────────────────────────────

    /// <summary>On a character, connects with it; on a MUD, with its default character if it has one.</summary>
    public void Connect()
    {
        if (SelectedNode is not { } node) return;
        var character = SelectedCharacter ?? node.Characters.FirstOrDefault(c => c.IsDefault);
        Open(BuildProfile(node.Mud, character));
    }

    public void QuickConnect()
    {
        if (_dialogs.QuickConnect() is { } profile)
            Open(profile);
    }

    public SessionProfile BuildProfile(MudEntity mud, CharacterEntity? character)
    {
        string? password = null;
        if (character?.EncryptedPassword is { Length: > 0 } blob)
        {
            // An unreadable password (data folder moved to another computer) is simply not sent.
            try { password = _protector.Unprotect(blob); }
            catch { password = null; }
        }

        return new SessionProfile
        {
            Title = character is null ? mud.Name : $"{character.Name} - {mud.Name}",
            Host = mud.Host,
            Port = mud.Port,
            UseTls = mud.UseTls,
            ValidateCertificate = mud.ValidateCertificate,
            Encoding = mud.Encoding,
            LoginScript = mud.LoginScript,
            SaveCommand = mud.SaveCommand,
            QuitCommand = mud.QuitCommand,
            MudId = mud.Id,
            MudName = mud.Name,
            CharacterId = character?.Id,
            CharacterName = character?.Name,
            CharacterPassword = password,
            SoundDirectory = mud.SoundDirectory,
        };
    }

    private void Open(SessionProfile profile)
    {
        _openSession(profile);
        SetStatus(string.Format(Strings.Launcher_ConnectingTo, profile.Title));
    }

    // ── Add, edit, remove ──────────────────────────────────────────────────

    public async Task AddMudAsync(CancellationToken ct = default)
    {
        var model = new MudEditorModel(_muds, _rules);
        if (!_dialogs.EditMud(model) || model.SavedId is not { } id) return;
        await ReloadAsync(new LauncherSelection(id), ct);
        SetStatus(string.Format(Strings.Launcher_StatusMudAdded, model.Name.Trim()));
    }

    /// <summary>For the MUD of the selected node; without selection the dialog asks for the MUD.</summary>
    public async Task AddCharacterAsync(CancellationToken ct = default)
    {
        if (Nodes.Count == 0)
        {
            _prompts.Info(Strings.Launcher_NeedsMudFirst, Strings.App_Title);
            return;
        }

        var model = new CharacterEditorModel(_characters, _muds, _protector, existing: null, mudId: Selection?.MudId);
        if (!_dialogs.EditCharacter(model) || model.SavedId is not { } id || model.MudId is not { } mudId) return;
        SetExpanded(mudId, true);
        await ReloadAsync(new LauncherSelection(mudId, id), ct);
        SetStatus(string.Format(Strings.Launcher_StatusCharacterAdded, model.Name.Trim()));
    }

    /// <summary>Insert key: a sibling of what is selected (a character on a character, a MUD otherwise).</summary>
    public Task AddForSelectionAsync(CancellationToken ct = default) =>
        SelectedCharacter is not null ? AddCharacterAsync(ct) : AddMudAsync(ct);

    public async Task EditAsync(CancellationToken ct = default)
    {
        if (SelectedNode is not { } node) return;

        if (SelectedCharacter is { } character)
        {
            var model = new CharacterEditorModel(_characters, _muds, _protector, character);
            if (!_dialogs.EditCharacter(model)) return;
            await ReloadAsync(Selection, ct);
            SetStatus(string.Format(Strings.Launcher_StatusCharacterSaved, model.Name.Trim()));
        }
        else
        {
            var model = new MudEditorModel(_muds, _rules, node.Mud);
            if (!_dialogs.EditMud(model)) return;
            await ReloadAsync(Selection, ct);
            SetStatus(string.Format(Strings.Launcher_StatusMudSaved, model.Name.Trim()));
        }
    }

    public async Task RemoveAsync(CancellationToken ct = default)
    {
        if (SelectedNode is not { } node) return;

        if (SelectedCharacter is { } character)
        {
            if (!_prompts.Confirm(string.Format(Strings.Launcher_ConfirmRemoveCharacter, character.Name, node.Mud.Name), Strings.Common_Confirm))
                return;

            var index = IndexOf(node.Characters, c => c.Id == character.Id);
            await _characters.DeleteAsync(character.Id, ct);

            // The neighbour: the next character, else the previous one, else the MUD.
            var rest = node.Characters.Where(c => c.Id != character.Id).ToList();
            var neighbour = rest.Count == 0 ? null : rest[Math.Min(index, rest.Count - 1)];
            await ReloadAsync(new LauncherSelection(node.Mud.Id, neighbour?.Id), ct);
            SetStatus(string.Format(Strings.Launcher_StatusCharacterRemoved, character.Name));
        }
        else
        {
            if (!_prompts.Confirm(string.Format(Strings.Launcher_ConfirmRemoveMud, node.Mud.Name), Strings.Common_Confirm))
                return;

            var index = IndexOf(Nodes, n => n.Mud.Id == node.Mud.Id);
            await _muds.DeleteAsync(node.Mud.Id, ct);
            _collapsed.Remove(node.Mud.Id);

            var rest = Nodes.Where(n => n.Mud.Id != node.Mud.Id).ToList();
            var neighbour = rest.Count == 0 ? null : rest[Math.Min(index, rest.Count - 1)];
            await ReloadAsync(neighbour is null ? null : new LauncherSelection(neighbour.Mud.Id), ct);
            SetStatus(string.Format(Strings.Launcher_StatusMudRemoved, node.Mud.Name));
        }
    }

    // ── Default character ──────────────────────────────────────────────────

    public async Task SetDefaultAsync(CancellationToken ct = default)
    {
        if (SelectedNode is not { } node || SelectedCharacter is not { } character) return;
        await _characters.SetDefaultAsync(node.Mud.Id, character.Id, ct);
        await ReloadAsync(Selection, ct);
        SetStatus(string.Format(Strings.Launcher_StatusDefaultSet, character.Name, node.Mud.Name));
    }

    public async Task ClearDefaultAsync(CancellationToken ct = default)
    {
        if (SelectedNode is not { } node || SelectedCharacter is not { IsDefault: true }) return;
        await _characters.ClearDefaultAsync(node.Mud.Id, ct);
        await ReloadAsync(Selection, ct);
        SetStatus(string.Format(Strings.Launcher_StatusDefaultCleared, node.Mud.Name));
    }

    // ── Import and export ──────────────────────────────────────────────────

    public async Task ExportMudAsync(CancellationToken ct = default)
    {
        if (SelectedMud is not { } mud)
        {
            _prompts.Info(Strings.Launcher_SelectMudFirst, Strings.Exchange_ExportMudTitle);
            return;
        }
        if (await _exchange.ExportMudAsync(mud.Id, mud.Name, ct))
            SetStatus(string.Format(Strings.Launcher_StatusExported, mud.Name));
    }

    public async Task ExportCharacterAsync(CancellationToken ct = default)
    {
        if (SelectedCharacter is not { } character)
        {
            _prompts.Info(Strings.Launcher_SelectCharacterFirst, Strings.Exchange_ExportCharacterTitle);
            return;
        }
        if (await _exchange.ExportCharacterAsync(character.Id, character.Name, ct))
            SetStatus(string.Format(Strings.Launcher_StatusExported, character.Name));
    }

    /// <summary>Any .omnimud file. A character file goes into the MUD of the selected node.</summary>
    public async Task ImportAsync(CancellationToken ct = default)
    {
        var target = Selection is null ? ImportTarget.None : ImportTarget.ForMud(Selection.MudId);
        if (await _exchange.ImportAsync(target, ct) is not { } summary) return;
        await ReloadAsync(Selection, ct);
        SetStatus(string.Format(Strings.Launcher_StatusImported, summary.Added, summary.Overwritten, summary.Skipped, summary.Failed));
    }

    // ── Plumbing ───────────────────────────────────────────────────────────

    private async Task ReloadAsync(LauncherSelection? wanted, CancellationToken ct)
    {
        var muds = await _muds.GetAllAsync(ct);
        var nodes = new List<LauncherMudNode>(muds.Count);
        foreach (var mud in muds)
            nodes.Add(new LauncherMudNode(mud, await _characters.GetByMudAsync(mud.Id, ct)));
        Nodes = nodes;

        _collapsed.IntersectWith(nodes.Select(n => n.Mud.Id));
        Selection = Resolve(wanted);
        Changed?.Invoke();
    }

    /// <summary>The wanted node if it still exists; its MUD if the character is gone; else the first MUD. Never nothing while there is something.</summary>
    private LauncherSelection? Resolve(LauncherSelection? wanted)
    {
        if (Nodes.Count == 0) return null;
        var node = wanted is null ? null : Nodes.FirstOrDefault(n => n.Mud.Id == wanted.MudId);
        if (node is null) return new LauncherSelection(Nodes[0].Mud.Id);
        var characterId = wanted!.CharacterId is { } id && node.Characters.Any(c => c.Id == id) ? id : (int?)null;
        return new LauncherSelection(node.Mud.Id, characterId);
    }

    private void SetStatus(string text)
    {
        Status = text;
        StatusChanged?.Invoke();
    }

    private static int IndexOf<T>(IReadOnlyList<T> list, Func<T, bool> match)
    {
        for (var i = 0; i < list.Count; i++)
            if (match(list[i])) return i;
        return 0;
    }
}
