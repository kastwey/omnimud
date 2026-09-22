using System.Globalization;
using System.Text;
using Omnimud.Core.Aliases;
using Omnimud.Core.Paths;
using Omnimud.Core.Resources;
using Omnimud.Core.Session;

namespace Omnimud.Core.Commands;

/// <summary>
/// Everything that happens between the user pressing Enter and bytes leaving for the MUD:
/// history, "repeat last", password mode, paths, aliases, @command triggers, the client's own
/// commands, path recording, concatenation and repetition. The evaluation order is the
/// original client's (see docs/03, §4).
/// Not thread-safe: the owning session serializes every call.
/// </summary>
public sealed class InputProcessor
{
    /// <summary>Nested commands (alias → trigger → alias...) deeper than this are dropped.</summary>
    public const int MaxDepth = 10;
    public const int MaxRepeat = 50;

    /// <summary>Stands for an escaped concatenation character while a command is being processed (private use area).</summary>
    private const char LiteralMark = (char)0xE000;

    private readonly IInputHost _host;
    private readonly AliasResolver _aliases = new(caseSensitive: true);
    private readonly DirectionDictionary _directions = new();
    private readonly PathEngine _pathEngine;
    private readonly Dictionary<string, PathDefinition> _paths = new(StringComparer.Ordinal);
    private readonly List<string> _history = [];
    private readonly List<string> _recorded = [];
    private volatile IReadOnlyList<string> _historySnapshot = [];

    public InputProcessor(IInputHost host)
    {
        _host = host;
        _pathEngine = new PathEngine(_directions);
    }

    /// <summary>Oldest first. Safe to read from any thread.</summary>
    public IReadOnlyList<string> History => _historySnapshot;

    public string? LastCommand { get; private set; }

    public bool IsRecordingPath { get; private set; }

    public IReadOnlyList<AliasDefinition> Aliases => _aliases.GetAll();

    public void Load(
        IEnumerable<AliasDefinition> aliases,
        IEnumerable<PathDefinition> paths,
        IEnumerable<DirectionEntry> directions)
    {
        _aliases.Load(aliases);
        _directions.Load(directions);
        _paths.Clear();
        foreach (var path in paths)
            _paths[path.Name] = path;
    }

    /// <summary>The history size option may have shrunk.</summary>
    public void TrimHistory()
    {
        var max = Math.Max(0, _host.Options.HistorySize);
        if (_history.Count <= max) return;
        _history.RemoveRange(0, _history.Count - max);
        _historySnapshot = _history.ToArray();
    }

    /// <summary>What the user typed and confirmed with Enter.</summary>
    public async Task SubmitAsync(string text)
    {
        text = NormalizeNewlines(text ?? string.Empty);

        if (_host.PasswordMode)
        {
            // Straight to the MUD: no history, no "last command", no log, no aliases.
            await SendToMudAsync(text, log: false).ConfigureAwait(false);
            return;
        }

        if (text.Length == 0)
        {
            if (LastCommand is null)
                await SendToMudAsync(string.Empty, log: true).ConfigureAwait(false);
            else
                await ProcessCommandAsync(LastCommand, 0).ConfigureAwait(false);
            return;
        }

        AddToHistory(text);
        LastCommand = text;
        await ProcessCommandAsync(text, 0).ConfigureAwait(false);
    }

    /// <summary>Full pipeline without history (numpad movement, F3/F4, trigger actions, om.send).</summary>
    public Task ExecuteAsync(string command, int depth = 0)
        => ProcessCommandAsync(NormalizeNewlines(command ?? string.Empty), depth);

    /// <summary>
    /// A command that was not written by the user but offered by the MUD (actions menu). It can only
    /// end up as text for the MUD: paths, aliases, @command triggers, concatenation and repetition
    /// work as if it had been typed, because they are the user's own; the client's commands (calias,
    /// uncalias, ±triggers, cls, paths..., callate, hablar) are not obeyed, so a server can never
    /// change the client's configuration through a menu entry. No history.
    /// </summary>
    public Task ExecuteExternalAsync(string command)
        => ProcessCommandAsync(NormalizeNewlines(command ?? string.Empty).Replace('\n', ' '), 0, allowAlias: true, clientCommands: false);

    /// <summary>
    /// Splits on the concatenation character; a doubled character is a literal one. Empty
    /// parts are dropped.
    /// </summary>
    public static IReadOnlyList<string> SplitConcatenated(string text, char concatChar)
    {
        var parts = new List<string>();
        var current = new StringBuilder();

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != concatChar)
            {
                current.Append(c);
            }
            else if (i + 1 < text.Length && text[i + 1] == concatChar)
            {
                current.Append(concatChar);
                i++;
            }
            else
            {
                if (current.Length > 0) parts.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0) parts.Add(current.ToString());
        return parts;
    }

    /// <summary>"N#command" → (N capped to <see cref="MaxRepeat"/>, command). False if it is not a repetition.</summary>
    public static bool TryParseRepeat(string text, char repeatChar, out int count, out string command)
    {
        count = 0;
        command = string.Empty;

        var index = text.IndexOf(repeatChar);
        if (index <= 0 || index == text.Length - 1)
            return false;

        var prefix = text.AsSpan(0, index);
        foreach (var c in prefix)
            if (!char.IsAsciiDigit(c)) return false;

        count = int.TryParse(prefix, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? Math.Min(n, MaxRepeat)
            : MaxRepeat; // so many digits that it overflows
        command = text[(index + 1)..];
        return true;
    }

    private async Task ProcessCommandAsync(string command, int depth, bool allowAlias = true, bool clientCommands = true)
    {
        if (depth > MaxDepth)
        {
            _host.WriteSystem(string.Format(Strings.Cmd_RecursionLimit, RestoreLiterals(command)));
            return;
        }

        if (command.Length == 0)
        {
            await SendToMudAsync(string.Empty, log: true).ConfigureAwait(false);
            return;
        }

        // 1. Paths.
        if (command[0] == '_')
        {
            await RunPathAsync(command).ConfigureAwait(false);
            return;
        }

        // 2. Aliases: first word, exact match, one pass.
        var aliasApplied = false;
        if (allowAlias)
        {
            var resolved = _aliases.Resolve(command);
            aliasApplied = !ReferenceEquals(resolved, command) && resolved != command;
            command = resolved;
        }
        if (command.Length == 0)
        {
            await SendToMudAsync(string.Empty, log: true).ConfigureAwait(false);
            return;
        }

        var words = command.Split(' ');
        var first = words[0];

        // 3. Command triggers (@word). The command is not sent.
        if (_host.TriggersEnabled && first.Length > 0)
        {
            var args = words.Length > 1 ? words[1..].Select(RestoreLiterals).ToArray() : [];
            if (await _host.RunCommandTriggersAsync(first, args, RestoreLiterals(command), depth).ConfigureAwait(false))
                return;
        }

        // 4-7. Commands with arguments.
        switch (clientCommands ? first : null)
        {
            case "-triggers" when words.Length == 1:
                SetTriggerSystem(false);
                return;
            case "+triggers" when words.Length == 1:
                SetTriggerSystem(true);
                return;
            case "-trigger":
                await SetTriggerAsync(words, false).ConfigureAwait(false);
                return;
            case "+trigger":
                await SetTriggerAsync(words, true).ConfigureAwait(false);
                return;
            case "uncalias":
                await RemoveAliasAsync(words).ConfigureAwait(false);
                return;
            case "calias" when words.Length > 1:
                await QueryOrAddAliasAsync(command, words).ConfigureAwait(false);
                return;
        }

        // 8. Path recording: note the direction and carry on (it is sent as well).
        if (IsRecordingPath && _directions.IsKnownDirection(command))
        {
            _recorded.Add(_directions.Normalize(command));
            _host.PlayUiSound("pop");
        }

        // 9. Exact commands.
        switch (clientCommands ? command : null)
        {
            case "cls":
                _host.RequestClear();
                _host.WriteSystem(Strings.Cmd_ClsOk);
                return;
            case "triggers":
                OpenWindow(SessionWindow.Triggers);
                return;
            case "calias":
                OpenWindow(SessionWindow.Aliases);
                return;
            case "paths":
                OpenWindow(SessionWindow.Paths);
                return;
            case "paths iniciar":
                StartRecording();
                return;
            case "paths ultima":
                ShowLastRecorded();
                return;
            case "paths ultima borrar":
                RemoveLastRecorded();
                return;
            case "paths grabado":
                ShowRecorded();
                return;
            case "paths cancelar":
                CancelRecording();
                return;
            case "paths detener":
                StopRecording();
                return;
            case "callate":
                SetSilent(true);
                return;
            case "hablar":
                SetSilent(false);
                return;
        }

        // 10. To the MUD.
        await SendCommandAsync(command, depth, aliasApplied, clientCommands).ConfigureAwait(false);
    }

    private async Task SendCommandAsync(string command, int depth, bool aliasApplied, bool clientCommands)
    {
        var options = _host.Options;

        if (options.UseConcatChar && command.Contains(options.ConcatChar))
        {
            var parts = SplitConcatenated(command, options.ConcatChar);
            // Each part is a command of its own (paths, aliases, internal commands...). The literal
            // characters it now contains travel as a private mark so that they never split again,
            // while an alias expanded inside the part still can.
            if (depth + 1 > MaxDepth)
            {
                _host.WriteSystem(string.Format(Strings.Cmd_RecursionLimit, RestoreLiterals(command)));
                return;
            }

            for (var i = 0; i < parts.Count; i++)
            {
                var part =parts[i].Replace(options.ConcatChar, LiteralMark);
                // Aliases are a single pass: the first part of an expanded alias is not expanded again.
                await ProcessCommandAsync(part, depth + 1, allowAlias: i > 0 || !aliasApplied, clientCommands).ConfigureAwait(false);
            }
            return;
        }

        if (options.UseRepeatChar && TryParseRepeat(command, options.RepeatChar, out var count, out var repeated))
        {
            for (var i = 0; i < count; i++)
            {
                if (!await SendToMudAsync(repeated, log: true).ConfigureAwait(false))
                    return;
            }
            return;
        }

        await SendToMudAsync(command, log: true).ConfigureAwait(false);
    }

    /// <summary>False when nothing could be sent (offline or disconnected); the user has been told.</summary>
    private async Task<bool> SendToMudAsync(string line, bool log)
    {
        switch (_host.State)
        {
            case SessionState.Offline:
                _host.WriteSystem(Strings.Cmd_Offline);
                return false;
            case SessionState.Connected:
                await _host.SendLineAsync(RestoreLiterals(line), log).ConfigureAwait(false);
                return true;
            default:
                _host.WriteSystem(Strings.Cmd_NotConnected);
                return false;
        }
    }

    // ── Paths ──────────────────────────────────────────────────────────────

    private async Task RunPathAsync(string command)
    {
        if (_directions.GetAll().Count == 0)
        {
            _host.WriteSystem(Strings.Path_NoDirections);
            return;
        }

        var name = command[1..];
        var reverse = false;
        if (name.EndsWith(" -r", StringComparison.Ordinal))
        {
            reverse = true;
            name = name[..^3];
        }

        if (name.Length == 0)
        {
            _host.WriteSystem(Strings.Path_Usage);
            return;
        }

        if (!_paths.TryGetValue(name, out var path))
        {
            _host.WriteSystem(Strings.Path_NotFound);
            return;
        }

        if (!_pathEngine.IsValid(path.Path))
        {
            _host.WriteSystem(string.Format(Strings.Path_Invalid, path.Path));
            return;
        }

        var steps = reverse ? _pathEngine.Reverse(path.Path) : _pathEngine.Expand(path.Path);
        if (steps is null)
        {
            _host.WriteSystem(Strings.Path_NotReversible);
            return;
        }

        foreach (var step in steps)
        {
            if (!await SendToMudAsync(step, log: true).ConfigureAwait(false))
                return;
        }
    }

    private void StartRecording()
    {
        if (_host.CharacterId is null)
        {
            _host.WriteSystem(Strings.Cmd_RequiresCharacter);
            return;
        }
        if (_directions.GetAll().Count == 0)
        {
            _host.WriteSystem(Strings.Path_NoDirections);
            return;
        }
        if (IsRecordingPath)
        {
            _host.WriteSystem(Strings.Rec_AlreadyStarted);
            return;
        }

        _recorded.Clear();
        IsRecordingPath = true;
        _host.WriteSystem(Strings.Rec_Started);
        _host.SetStatus(Strings.Rec_Status);
    }

    private bool EnsureRecording(bool needsDirections)
    {
        if (!IsRecordingPath)
        {
            _host.WriteSystem(Strings.Rec_NotRecording);
            return false;
        }
        if (needsDirections && _recorded.Count == 0)
        {
            _host.WriteSystem(Strings.Rec_Empty);
            return false;
        }
        return true;
    }

    private void ShowLastRecorded()
    {
        if (!EnsureRecording(needsDirections: true)) return;
        _host.WriteSystem(string.Format(Strings.Rec_Last, _recorded[^1]));
    }

    private void RemoveLastRecorded()
    {
        if (!EnsureRecording(needsDirections: true)) return;
        var last = _recorded[^1];
        _recorded.RemoveAt(_recorded.Count - 1);
        _host.WriteSystem(string.Format(Strings.Rec_LastRemoved, last));
    }

    private void ShowRecorded()
    {
        if (!EnsureRecording(needsDirections: true)) return;
        _host.WriteSystem(string.Format(Strings.Rec_Recorded, _pathEngine.Collapse(_recorded)));
    }

    private void CancelRecording()
    {
        if (!EnsureRecording(needsDirections: false)) return;
        if (!_host.Confirm(Strings.Rec_CancelConfirm)) return;

        EndRecording();
        _host.WriteSystem(Strings.Rec_Cancelled);
    }

    private void StopRecording()
    {
        if (!EnsureRecording(needsDirections: true)) return;

        var collapsed = _pathEngine.Collapse(_recorded);
        EndRecording();
        _host.WriteSystem(Strings.Rec_Stopped);
        _host.RequestWindow(SessionWindow.NewPath, collapsed);
    }

    private void EndRecording()
    {
        IsRecordingPath = false;
        _recorded.Clear();
        _host.SetStatus(string.Empty);
    }

    // ── Triggers ───────────────────────────────────────────────────────────

    private void SetTriggerSystem(bool enabled)
    {
        if (_host.TriggersEnabled == enabled)
        {
            _host.WriteSystem(enabled ? Strings.Triggers_AlreadyEnabled : Strings.Triggers_AlreadyDisabled);
            return;
        }

        _host.TriggersEnabled = enabled;
        _host.WriteSystem(enabled ? Strings.Triggers_Enabled : Strings.Triggers_Disabled);
    }

    private async Task SetTriggerAsync(string[] words, bool enabled)
    {
        var name = string.Join(' ', words.Skip(1)).Trim();
        if (name.Length == 0)
        {
            _host.WriteSystem(enabled ? Strings.Trigger_EnableWhich : Strings.Trigger_DisableWhich);
            return;
        }

        var trigger = _host.FindTrigger(name);
        if (trigger is null)
        {
            _host.WriteSystem(string.Format(Strings.Trigger_NotFound, name));
            return;
        }

        if (trigger.Enabled == enabled)
        {
            _host.WriteSystem(enabled ? Strings.Trigger_AlreadyEnabled : Strings.Trigger_AlreadyDisabled);
            return;
        }

        try
        {
            await _host.Store.SetTriggerEnabledAsync(trigger.Id, enabled).ConfigureAwait(false);
        }
        catch (Exception)
        {
            _host.WriteSystem(enabled ? Strings.Trigger_EnableError : Strings.Trigger_DisableError);
            return;
        }

        trigger.Enabled = enabled;
        _host.WriteSystem(enabled ? Strings.Trigger_EnabledOk : Strings.Trigger_DisabledOk);
    }

    // ── Aliases ────────────────────────────────────────────────────────────

    private async Task RemoveAliasAsync(string[] words)
    {
        if (_host.CharacterId is not { } characterId)
        {
            _host.WriteSystem(Strings.Cmd_RequiresCharacter);
            return;
        }

        var names = words.Skip(1).Where(w => w.Length > 0).ToArray();
        if (names.Length == 0)
        {
            _host.WriteSystem(Strings.Alias_RemoveNeedsName);
            return;
        }
        if (names.Length > 1)
        {
            _host.WriteSystem(Strings.Alias_NameNoSpaces);
            return;
        }

        var name = names[0];
        if (_aliases.Get(name) is null)
        {
            _host.WriteSystem(Strings.Alias_NotFound);
            return;
        }

        try
        {
            await _host.Store.RemoveAliasAsync(characterId, name).ConfigureAwait(false);
        }
        catch (Exception)
        {
            _host.WriteSystem(Strings.Alias_RemoveError);
            return;
        }

        _aliases.Remove(name);
        _host.WriteSystem(string.Format(Strings.Alias_Removed, name));
    }

    private async Task QueryOrAddAliasAsync(string command, string[] words)
    {
        var name = words[1];
        if (name.Length == 0)
        {
            _host.WriteSystem(words.Length > 2 ? Strings.Alias_EmptyCommand : Strings.Alias_QueryNeedsName);
            return;
        }

        if (words.Length == 2)
        {
            var existing = _aliases.Get(name);
            _host.WriteSystem(existing is null
                ? string.Format(Strings.Alias_NotStored, name)
                : string.Format(Strings.Alias_Assigned, name, existing.Action));
            return;
        }

        if (_host.CharacterId is not { } characterId)
        {
            _host.WriteSystem(Strings.Cmd_RequiresCharacter);
            return;
        }

        // Everything after "calias name ", untouched (inner spaces are part of the action).
        var action = RestoreLiterals(command[("calias ".Length + name.Length + 1)..]);
        if (string.IsNullOrWhiteSpace(action))
        {
            _host.WriteSystem(Strings.Alias_EmptyAction);
            return;
        }

        if (_aliases.Get(name) is not null)
        {
            _host.WriteSystem(Strings.Alias_AlreadyExists);
            return;
        }

        var sameAction = _aliases.GetAll().FirstOrDefault(a => a.Action == action);
        if (sameAction is not null &&
            !_host.Confirm(string.Format(Strings.Alias_SameActionConfirm, sameAction.Command)))
        {
            _host.WriteSystem(Strings.Alias_Cancelled);
            return;
        }

        bool added;
        try
        {
            added = await _host.Store.AddAliasAsync(characterId, name, action).ConfigureAwait(false);
        }
        catch (Exception)
        {
            _host.WriteSystem(Strings.Alias_AddError);
            return;
        }

        if (!added)
        {
            _host.WriteSystem(Strings.Alias_AlreadyExists);
            return;
        }

        _aliases.Add(new AliasDefinition(name, action));
        _host.WriteSystem(string.Format(Strings.Alias_Added, name));
    }

    // ── Misc ───────────────────────────────────────────────────────────────

    private void OpenWindow(SessionWindow window)
    {
        if (_host.CharacterId is null)
        {
            _host.WriteSystem(Strings.Cmd_RequiresCharacter);
            return;
        }
        _host.RequestWindow(window, null);
    }

    private void SetSilent(bool silent)
    {
        if (_host.SilentMode == silent)
        {
            _host.WriteSystem(silent ? Strings.Silent_AlreadyOn : Strings.Silent_NotOn);
            return;
        }

        // The session confirms the change out loud, whoever asks for it (this command, F8 or the menu).
        _host.SilentMode = silent;
    }

    private void AddToHistory(string text)
    {
        var max = _host.Options.HistorySize;
        if (max <= 0) return;
        if (_history.Count > 0 && _history[^1] == text) return;

        _history.Add(text);
        if (_history.Count > max)
            _history.RemoveRange(0, _history.Count - max);
        _historySnapshot = _history.ToArray();
    }

    /// <summary>Escaped concatenation characters become the real character again when text leaves the processor.</summary>
    private string RestoreLiterals(string text)
        => text.Contains(LiteralMark) ? text.Replace(LiteralMark, _host.Options.ConcatChar) : text;

    private static string NormalizeNewlines(string text)
        => text.Contains('\r') ? text.Replace("\r\n", "\n").Replace('\r', '\n') : text;
}
