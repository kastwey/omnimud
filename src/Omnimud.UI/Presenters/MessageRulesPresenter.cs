using Omnimud.Core.Scripting;
using Omnimud.Data;
using Omnimud.Data.Entities;
using Omnimud.Data.Repositories;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;

namespace Omnimud.UI.Presenters;

/// <summary>The two editors the message rules window opens.</summary>
public interface IMessageRulesDialogs
{
    /// <summary>Null when cancelled.</summary>
    string? AskSetName(string title, string initialName);
    /// <summary>True when accepted; the model then holds valid values.</summary>
    bool EditRule(MessageRuleEditorModel model);

    /// <summary>
    /// Name and type of a new set; null when cancelled. By default only the name is asked and the
    /// set works with patterns, so dialogs written before script sets existed keep working.
    /// </summary>
    NewMessageRuleSet? AskNewSet(string title, string initialName, bool initialIsScript) =>
        AskSetName(title, initialName) is { } name ? new NewMessageRuleSet(name, initialIsScript) : null;
}

/// <summary>What the "new set" dialog answers.</summary>
public sealed record NewMessageRuleSet(string Name, bool IsScript);

/// <summary>
/// Management of message rule sets and their ordered rules. Built-in sets can only be duplicated.
/// Keeps the selection (set and rule) across every reload. No WinForms here.
/// </summary>
public sealed class MessageRulesPresenter
{
    private readonly IMessageRuleRepository _repository;
    private readonly IUserPrompts _prompts;
    private readonly IMessageRulesDialogs _dialogs;
    private readonly Action<string> _announce;
    private readonly MessageRuleScriptTester _scriptTester;
    private string _scriptDraft = string.Empty;

    /// <param name="announce">Speaks state changes that the focus does not reflect (enable/disable, move).</param>
    /// <param name="scriptEngine">Validates and tests Lua scripts. Null = scripts are saved unchecked and cannot be tested.</param>
    public MessageRulesPresenter(IMessageRuleRepository repository, IUserPrompts prompts, IMessageRulesDialogs dialogs,
        Action<string>? announce = null, IScriptEngine? scriptEngine = null)
    {
        _repository = repository;
        _prompts = prompts;
        _dialogs = dialogs;
        _announce = announce ?? (_ => { });
        _scriptTester = new MessageRuleScriptTester(scriptEngine);
    }

    public IReadOnlyList<MessageRuleSetEntity> Sets { get; private set; } = [];
    public MessageRuleSetEntity? SelectedSet { get; private set; }
    public IReadOnlyList<MessageRuleEntity> Rules { get; private set; } = [];
    /// <summary>-1 only when the set has no rules.</summary>
    public int SelectedRuleIndex { get; private set; } = -1;
    public MessageRuleEntity? SelectedRule => SelectedRuleIndex >= 0 && SelectedRuleIndex < Rules.Count ? Rules[SelectedRuleIndex] : null;
    public string Status { get; private set; } = string.Empty;

    /// <summary>Sets or rules were reloaded: the view must repaint both lists and the selection.</summary>
    public event Action? Changed;

    public bool CanDuplicateSet => SelectedSet is not null;
    public bool CanModifySet => SelectedSet is { IsBuiltIn: false };
    public bool CanAddRule => CanModifySet;
    public bool CanModifyRule => CanModifySet && SelectedRule is not null;
    public bool CanMoveUp => CanModifyRule && SelectedRuleIndex > 0;
    public bool CanMoveDown => CanModifyRule && SelectedRuleIndex < Rules.Count - 1;

    // ── Type of the set: patterns or Lua script ────────────────────────────

    /// <summary>True when the selected set works with a Lua script (its patterns are then not evaluated).</summary>
    public bool IsScriptSet => SelectedSet is { IsScript: true };
    public bool CanChangeType => CanModifySet;
    public bool CanSaveScript => CanModifySet && IsScriptSet;

    /// <summary>The script as being edited in the view (what "Save script" and "Test" use).</summary>
    public string ScriptDraft
    {
        get => _scriptDraft;
        set => _scriptDraft = value ?? string.Empty;
    }

    public bool IsScriptDirty => CanSaveScript && Normalize(_scriptDraft) != Normalize(SelectedSet?.Script);

    /// <summary>The script could not be saved: the view shows the message and puts the caret on the line.</summary>
    public event Action<EditorIssue>? ScriptRejected;

    public static string DisplayName(MessageRuleSetEntity set) =>
        set.IsBuiltIn ? string.Format(Strings.MsgRules_SetBuiltIn, set.Name) : set.Name;

    public Task LoadAsync(CancellationToken ct = default) => ReloadAsync(SelectedSet?.Id, SelectedRuleIndex, fallbackSetIndex: 0, ct);

    public async Task SelectSetAsync(int setId, CancellationToken ct = default)
    {
        if (SelectedSet?.Id == setId) return;
        if (!await LeaveScriptAsync(ct))
        {
            Changed?.Invoke(); // the view goes back to the set whose script could not be saved
            return;
        }
        await ReloadAsync(setId, 0, 0, ct);
    }

    /// <summary>
    /// Before leaving the selected set (another set, closing the window): offers to save a script with
    /// changes. False = stay, because the user wanted to save and the script is not valid.
    /// </summary>
    public async Task<bool> LeaveScriptAsync(CancellationToken ct = default)
    {
        if (!IsScriptDirty || SelectedSet is not { } set) return true;
        if (!_prompts.Confirm(string.Format(Strings.MsgRules_ConfirmSaveScript, set.Name), Strings.MsgRules_Title))
        {
            _scriptDraft = set.Script ?? string.Empty;
            return true;
        }
        return await SaveScriptAsync(ct);
    }

    /// <summary>Patterns to Lua script and back. Going to script starts from a small template; going back discards the script (asked first).</summary>
    public async Task SetTypeAsync(bool script, CancellationToken ct = default)
    {
        if (SelectedSet is not { } set || set.IsScript == script) return;
        if (!EnsureModifiable())
        {
            Changed?.Invoke();
            return;
        }

        if (!script && !_prompts.Confirm(string.Format(Strings.MsgRules_ConfirmDiscardScript, set.Name), Strings.MsgRules_Title))
        {
            Changed?.Invoke(); // the view's type selector goes back
            return;
        }

        await _repository.UpdateRuleSetAsync(new MessageRuleSetEntity
        {
            Id = set.Id, Name = set.Name, IsBuiltIn = set.IsBuiltIn, Script = script ? MessageRuleScriptTester.Template : null
        }, ct);
        await ReloadAsync(set.Id, SelectedRuleIndex, 0, ct, notify: false);
        Say(string.Format(script ? Strings.MsgRules_TypeChangedScript : Strings.MsgRules_TypeChangedPatterns, set.Name));
        Changed?.Invoke();
    }

    /// <summary>Validates <see cref="ScriptDraft"/> (Lua syntax) and saves it. False = rejected (see <see cref="ScriptRejected"/>).</summary>
    public async Task<bool> SaveScriptAsync(CancellationToken ct = default)
    {
        if (!EnsureModifiable() || SelectedSet is not { IsScript: true } set) return false;

        if (_scriptTester.Validate(_scriptDraft) is { } issue)
        {
            _prompts.Warn(issue.Message, Strings.MsgRules_Title);
            ScriptRejected?.Invoke(issue);
            return false;
        }

        await _repository.UpdateRuleSetAsync(new MessageRuleSetEntity { Id = set.Id, Name = set.Name, IsBuiltIn = set.IsBuiltIn, Script = Normalize(_scriptDraft) }, ct);
        await ReloadAsync(set.Id, SelectedRuleIndex, 0, ct, notify: false);
        Say(string.Format(Strings.MsgRules_StatusScriptSaved, set.Name));
        Changed?.Invoke();
        return true;
    }

    /// <summary>Runs <see cref="ScriptDraft"/> (saved or not) over a sample block, as a session would, and announces the outcome.</summary>
    public async Task<MessageRuleScriptTestResult> TestScriptAsync(string sample, CancellationToken ct = default)
    {
        if (SelectedSet is null) return new(false, [], Strings.MsgRules_NoSetSelected);
        var result = await _scriptTester.TestAsync(_scriptDraft, sample, ct);
        _announce(result.Text.ReplaceLineEndings(". "));
        return result;
    }

    private static string Normalize(string? script) => (script ?? string.Empty).ReplaceLineEndings("\n");

    public void SelectRule(int index)
    {
        if (index >= 0 && index < Rules.Count)
            SelectedRuleIndex = index;
    }

    // ── Sets ───────────────────────────────────────────────────────────────

    public async Task AddSetAsync(CancellationToken ct = default)
    {
        if (!await LeaveScriptAsync(ct)) return;
        var asScript = false;
        await AskNameAsync(Strings.MsgRules_NewSetTitle, string.Empty,
            name => _repository.AddRuleSetAsync(new MessageRuleSetEntity { Name = name, Script = asScript ? MessageRuleScriptTester.Template : null }, ct),
            Strings.MsgRules_StatusSetAdded, ct,
            ask: (title, initial) =>
            {
                var answer = _dialogs.AskNewSet(title, initial, asScript);
                asScript = answer?.IsScript ?? asScript;
                return answer?.Name;
            });
    }

    public async Task DuplicateSetAsync(CancellationToken ct = default)
    {
        if (SelectedSet is not { } source) return;
        if (!await LeaveScriptAsync(ct)) return;
        await AskNameAsync(Strings.MsgRules_DuplicateSetTitle, string.Format(Strings.MsgRules_CopyName, source.Name),
            name => _repository.DuplicateRuleSetAsync(source.Id, name, ct),
            Strings.MsgRules_StatusSetDuplicated, ct);
    }

    public async Task RenameSetAsync(CancellationToken ct = default)
    {
        if (!EnsureModifiable() || SelectedSet is not { } set) return;
        await AskNameAsync(Strings.MsgRules_RenameSetTitle, set.Name, async name =>
        {
            await _repository.UpdateRuleSetAsync(new MessageRuleSetEntity { Id = set.Id, Name = name, IsBuiltIn = set.IsBuiltIn, Script = set.Script }, ct);
            return set.Id;
        }, Strings.MsgRules_StatusSetRenamed, ct);
    }

    public async Task RemoveSetAsync(CancellationToken ct = default)
    {
        if (!EnsureModifiable() || SelectedSet is not { } set) return;
        if (!_prompts.Confirm(string.Format(Strings.MsgRules_ConfirmRemoveSet, set.Name), Strings.MsgRules_Title)) return;

        var index = IndexOfSet(set.Id);
        await _repository.DeleteRuleSetAsync(set.Id, ct);
        Status = string.Format(Strings.MsgRules_StatusSetRemoved, set.Name);
        await ReloadAsync(null, 0, index, ct);
    }

    /// <summary>Asks for a name until it is accepted or cancelled; a duplicate is a message, never an exception.</summary>
    private async Task AskNameAsync(string title, string initial, Func<string, Task<int>> save, string statusFormat, CancellationToken ct,
        Func<string, string, string?>? ask = null)
    {
        ask ??= _dialogs.AskSetName;
        var name = initial;
        while (true)
        {
            var typed = ask(title, name);
            if (typed is null) return;
            name = typed.Trim();
            if (name.Length == 0)
            {
                _prompts.Warn(Strings.MsgRules_NameRequired, title);
                continue;
            }

            try
            {
                var id = await save(name);
                Status = string.Format(statusFormat, name);
                await ReloadAsync(id, 0, 0, ct);
                return;
            }
            catch (DuplicateEntityException)
            {
                _prompts.Warn(string.Format(Strings.MsgRules_DuplicateName, name), title);
            }
        }
    }

    // ── Rules ──────────────────────────────────────────────────────────────

    public async Task AddRuleAsync(CancellationToken ct = default)
    {
        if (!EnsureModifiable() || SelectedSet is not { } set) return;
        var model = new MessageRuleEditorModel();
        if (!_dialogs.EditRule(model)) return;

        await _repository.AddRuleAsync(model.ToEntity(set.Id), ct);
        Status = Strings.MsgRules_StatusRuleAdded;
        await ReloadAsync(set.Id, int.MaxValue, 0, ct);
    }

    public async Task EditRuleAsync(CancellationToken ct = default)
    {
        if (!EnsureModifiable() || SelectedSet is not { } set || SelectedRule is not { } rule) return;
        var model = new MessageRuleEditorModel(rule);
        if (!_dialogs.EditRule(model)) return;

        await _repository.UpdateRuleAsync(model.ToEntity(set.Id), ct);
        Status = Strings.MsgRules_StatusRuleSaved;
        await ReloadAsync(set.Id, SelectedRuleIndex, 0, ct);
    }

    public async Task RemoveRuleAsync(CancellationToken ct = default)
    {
        if (!EnsureModifiable() || SelectedSet is not { } set || SelectedRule is not { } rule) return;
        if (!_prompts.Confirm(string.Format(Strings.MsgRules_ConfirmRemoveRule, rule.Pattern), Strings.MsgRules_Title)) return;

        await _repository.DeleteRuleAsync(rule.Id, ct);
        Status = Strings.MsgRules_StatusRuleRemoved;
        // The neighbour takes the place of the removed rule.
        await ReloadAsync(set.Id, SelectedRuleIndex, 0, ct);
    }

    public Task MoveUpAsync(CancellationToken ct = default) => MoveAsync(-1, ct);
    public Task MoveDownAsync(CancellationToken ct = default) => MoveAsync(+1, ct);

    private async Task MoveAsync(int delta, CancellationToken ct)
    {
        if (!EnsureModifiable() || SelectedSet is not { } set || SelectedRule is null) return;
        var from = SelectedRuleIndex;
        var to = from + delta;
        if (to < 0 || to >= Rules.Count)
        {
            Say(delta < 0 ? Strings.MsgRules_AlreadyFirst : Strings.MsgRules_AlreadyLast);
            return;
        }

        var reordered = Rules.ToList();
        (reordered[from], reordered[to]) = (reordered[to], reordered[from]);
        await _repository.ReplaceRulesAsync(set.Id, reordered, ct);
        await ReloadAsync(set.Id, to, 0, ct, notify: false);
        Say(string.Format(Strings.MsgRules_StatusRuleMoved, to + 1, Rules.Count));
        Changed?.Invoke();
    }

    public async Task ToggleRuleAsync(CancellationToken ct = default)
    {
        if (!EnsureModifiable() || SelectedSet is not { } set || SelectedRule is not { } rule) return;
        rule.Enabled = !rule.Enabled;
        await _repository.UpdateRuleAsync(rule, ct);
        await ReloadAsync(set.Id, SelectedRuleIndex, 0, ct, notify: false);
        Say(rule.Enabled ? Strings.MsgRules_RuleEnabled : Strings.MsgRules_RuleDisabled);
        Changed?.Invoke();
    }

    /// <summary>Tries the sample against the enabled rules of the selected set, in order.</summary>
    public MessageRuleTestResult TestSet(string sample)
    {
        if (SelectedSet is null) return new(false, Strings.MsgRules_NoSetSelected);
        return MessageRuleTester.Test(Rules, sample);
    }

    // ── Plumbing ───────────────────────────────────────────────────────────

    private bool EnsureModifiable()
    {
        if (SelectedSet is null) return false;
        if (!SelectedSet.IsBuiltIn) return true;
        _prompts.Info(string.Format(Strings.MsgRules_BuiltInReadOnly, SelectedSet.Name), Strings.MsgRules_Title);
        return false;
    }

    private void Say(string text)
    {
        Status = text;
        _announce(text);
    }

    private int IndexOfSet(int id)
    {
        for (var i = 0; i < Sets.Count; i++)
            if (Sets[i].Id == id) return i;
        return -1;
    }

    private async Task ReloadAsync(int? selectSetId, int selectRuleIndex, int fallbackSetIndex, CancellationToken ct, bool notify = true)
    {
        var sets = await _repository.GetRuleSetsAsync(ct);
        Sets = sets.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        // A list is never left without a selected element.
        SelectedSet = Sets.FirstOrDefault(s => s.Id == selectSetId)
                      ?? (Sets.Count > 0 ? Sets[Math.Clamp(fallbackSetIndex, 0, Sets.Count - 1)] : null);

        Rules = SelectedSet is null ? [] : (await _repository.GetRulesAsync(SelectedSet.Id, ct)).ToList();
        SelectedRuleIndex = Rules.Count == 0 ? -1 : Math.Clamp(selectRuleIndex, 0, Rules.Count - 1);
        _scriptDraft = SelectedSet?.Script ?? string.Empty;

        if (notify) Changed?.Invoke();
    }
}
