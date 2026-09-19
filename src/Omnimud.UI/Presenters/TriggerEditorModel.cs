using System.Text.RegularExpressions;
using Omnimud.Core.Scripting;
using Omnimud.Core.Triggers;
using Omnimud.Data.Entities;
using Omnimud.Data.Session;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Presenters;

/// <summary>"What will it do?" in the order the combo shows it.</summary>
public enum TriggerActionChoice
{
    SendCommand = 0,
    PlaySound = 1,
    Both = 2,
    LuaScript = 3,
}

/// <summary>
/// State and rules of the trigger editor: which controls are available for each kind of trigger,
/// normalisation (a command trigger is never regex, multiline nor hides lines) and validation.
/// </summary>
public sealed partial class TriggerEditorModel
{
    public const string FieldName = "name";
    public const string FieldPattern = "pattern";
    public const string FieldAction = "action";
    public const string FieldSound = "sound";

    public const int MinPriority = 0;
    public const int MaxPriority = 1000;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    private readonly TriggerEntity? _existing;
    private readonly IReadOnlyList<TriggerEntity> _others;

    public TriggerEditorModel(TriggerEntity? existing, bool isNew, IReadOnlyList<TriggerEntity>? all = null)
    {
        _existing = existing;
        IsNew = isNew;
        _others = (all ?? []).Where(t => isNew || existing is null || t.Id != existing.Id).ToList();

        Name = existing?.Name ?? string.Empty;
        Pattern = existing?.Pattern ?? string.Empty;
        PatternType = existing is null ? PatternType.Literal : EntityMapper.ToDefinition(existing).PatternType;
        CaseSensitive = existing?.CaseSensitive ?? false;
        Multiline = existing?.Multiline ?? false;
        GagLine = existing?.GagLine ?? false;
        Priority = Math.Clamp(existing?.Priority ?? 50, MinPriority, MaxPriority);
        Enabled = existing?.Enabled ?? true;
        ActionChoice = existing is null ? TriggerActionChoice.SendCommand : ToChoice(EntityMapper.ToDefinition(existing).ActionType);
        Action = existing?.Action ?? string.Empty;
        Sound = existing?.Sound ?? string.Empty;
    }

    public bool IsNew { get; }
    public string Name { get; set; }
    public string Pattern { get; set; }
    public PatternType PatternType { get; set; }
    public bool CaseSensitive { get; set; }
    public bool Multiline { get; set; }
    public bool GagLine { get; set; }
    public int Priority { get; set; }
    public bool Enabled { get; set; }
    public TriggerActionChoice ActionChoice { get; set; }
    public string Action { get; set; }
    public string Sound { get; set; }

    // ── What the form enables ────────────────────────────────────────────

    /// <summary>"@word": fired by what the user types, not by MUD text.</summary>
    public bool IsCommandTrigger => Pattern.TrimStart().StartsWith('@');
    public bool PatternTypeEnabled => !IsCommandTrigger;
    public bool MultilineEnabled => !IsCommandTrigger;
    public bool GagLineEnabled => !IsCommandTrigger && !Multiline;
    public bool IsScript => ActionChoice == TriggerActionChoice.LuaScript;
    public bool ActionEnabled => ActionChoice != TriggerActionChoice.PlaySound;
    public bool SoundEnabled => ActionChoice is TriggerActionChoice.PlaySound or TriggerActionChoice.Both;

    /// <summary>Label of the action box, with mnemonic. Changes to "Lua script:" for scripts.</summary>
    public string ActionLabel => IsScript ? Strings.TrigEdit_ScriptLabel : Strings.TrigEdit_ActionLabel;
    /// <summary>AccessibleName of the action box; must follow the label.</summary>
    public string ActionAccessibleName => IsScript ? Strings.TrigEdit_ScriptName : Strings.TrigEdit_ActionName;

    // ── Effective (normalised) values ────────────────────────────────────

    public PatternType EffectivePatternType => IsCommandTrigger ? PatternType.Literal : PatternType;
    public bool EffectiveMultiline => MultilineEnabled && Multiline;
    public bool EffectiveGagLine => GagLineEnabled && GagLine;

    public static TriggerActionChoice ToChoice(TriggerActionType type) => type switch
    {
        TriggerActionType.PlaySound => TriggerActionChoice.PlaySound,
        TriggerActionType.SendCommandAndPlaySound => TriggerActionChoice.Both,
        TriggerActionType.Script => TriggerActionChoice.LuaScript,
        _ => TriggerActionChoice.SendCommand,
    };

    public static TriggerActionType ToActionType(TriggerActionChoice choice) => choice switch
    {
        TriggerActionChoice.PlaySound => TriggerActionType.PlaySound,
        TriggerActionChoice.Both => TriggerActionType.SendCommandAndPlaySound,
        TriggerActionChoice.LuaScript => TriggerActionType.Script,
        _ => TriggerActionType.SendCommand,
    };

    public static string PatternTypeName(int patternType) => patternType switch
    {
        (int)PatternType.Regex => Strings.TrigEdit_TypeRegex,
        (int)PatternType.Sscanf => Strings.TrigEdit_TypeWildcards,
        _ => Strings.TrigEdit_TypeLiteral,
    };

    public static string ActionTypeName(int actionType) => actionType switch
    {
        (int)TriggerActionType.PlaySound => Strings.TrigEdit_DoSound,
        (int)TriggerActionType.Script => Strings.TrigEdit_DoScript,
        (int)TriggerActionType.SendCommandAndPlaySound => Strings.TrigEdit_DoBoth,
        _ => Strings.TrigEdit_DoCommand,
    };

    // ── Validation ───────────────────────────────────────────────────────

    /// <param name="engine">Compiles Lua scripts. Null = scripts are not checked.</param>
    public EditorIssue? Validate(IScriptEngine? engine)
    {
        var name = Name.Trim();
        if (name.Length == 0) return new(FieldName, Strings.TrigEdit_ErrNameEmpty);
        if (_others.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
            return new(FieldName, string.Format(Strings.TrigEdit_ErrDuplicateName, name));

        return ValidatePattern() ?? ValidateAction(engine);
    }

    /// <summary>The part of the validation the Test button needs: a usable pattern.</summary>
    public EditorIssue? ValidatePattern()
    {
        var pattern = Pattern.Trim();
        if (pattern.Length == 0) return new(FieldPattern, Strings.TrigEdit_ErrPatternEmpty);

        if (IsCommandTrigger)
        {
            if (pattern.Length == 1) return new(FieldPattern, Strings.TrigEdit_ErrCommandEmpty);
            if (pattern.Any(char.IsWhiteSpace)) return new(FieldPattern, Strings.TrigEdit_ErrCommandSpaces);
            return null;
        }

        if (PatternType == PatternType.Regex && RegexError(pattern) is { } error)
            return new(FieldPattern, string.Format(Strings.TrigEdit_ErrRegex, error));
        return null;
    }

    /// <summary>The action part: required fields for the chosen kind and, for Lua, a script that compiles.</summary>
    public EditorIssue? ValidateAction(IScriptEngine? engine)
    {
        if (ActionEnabled)
        {
            if (string.IsNullOrWhiteSpace(Action))
                return new(FieldAction, IsScript ? Strings.TrigEdit_ErrScriptEmpty : Strings.TrigEdit_ErrActionEmpty);

            if (!IsScript && Action.Trim().AsSpan().IndexOfAny('\r', '\n') >= 0)
                return new(FieldAction, Strings.TrigEdit_ErrActionLines);

            if (IsScript && engine?.Validate(Action) is { } scriptError)
            {
                var line = ErrorLine(scriptError);
                var message = line is null
                    ? string.Format(Strings.TrigEdit_ErrScript, scriptError)
                    : string.Format(Strings.TrigEdit_ErrScriptAtLine, line, scriptError);
                return new(FieldAction, message, line);
            }
        }

        if (SoundEnabled && string.IsNullOrWhiteSpace(Sound))
            return new(FieldSound, Strings.TrigEdit_ErrSoundEmpty);
        return null;
    }

    /// <summary>Questions the user must answer Yes to before saving.</summary>
    public IReadOnlyList<string> Warnings()
    {
        var pattern = Pattern.Trim();
        var same = _others.FirstOrDefault(t => t.Pattern == pattern);
        return same is null ? [] : [string.Format(Strings.TrigEdit_WarnSamePattern, same.Name)];
    }

    /// <summary>Null when the regex compiles and can be run; otherwise why not. Never hangs: it carries a timeout.</summary>
    internal string? RegexError(string pattern)
    {
        try
        {
            var options = RegexOptions.CultureInvariant;
            if (!CaseSensitive) options |= RegexOptions.IgnoreCase;
            if (EffectiveMultiline) options |= RegexOptions.Multiline;
            _ = new Regex(pattern, options, RegexTimeout).IsMatch("prueba test 123");
            return null;
        }
        catch (RegexMatchTimeoutException)
        {
            return Strings.TrigEdit_ErrRegexSlow;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>The engine reports "… line N: message".</summary>
    internal static int? ErrorLine(string error)
    {
        var match = LineRegex().Match(error);
        return match.Success && int.TryParse(match.Groups[1].Value, out var line) && line > 0 ? line : null;
    }

    /// <summary>Where the 1-based <paramref name="line"/> starts in <paramref name="text"/> and how long it is
    /// (without its line break), so the editor can put the cursor there. Past the end = the last line.</summary>
    public static (int Start, int Length) LineSpan(string text, int line)
    {
        var start = 0;
        for (var current = 1; current < line; current++)
        {
            var next = text.IndexOf('\n', start);
            if (next < 0) break;
            start = next + 1;
        }

        var end = text.IndexOf('\n', start);
        if (end < 0) end = text.Length;
        if (end > start && text[end - 1] == '\r') end--;
        return (start, end - start);
    }

    [GeneratedRegex(@"\bline (\d+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LineRegex();

    // ── Output ───────────────────────────────────────────────────────────

    public TriggerEntity ToEntity() => new()
    {
        Id = IsNew ? Guid.NewGuid().ToString() : _existing?.Id ?? Guid.NewGuid().ToString(),
        CharacterId = _existing?.CharacterId ?? 0,
        Name = Name.Trim(),
        Pattern = Pattern.Trim(),
        PatternType = (int)EffectivePatternType,
        // What is disabled is kept as typed: switching the kind back must not lose a script or a sound.
        Action = IsScript ? Action : Action.Trim(),
        ActionType = (int)ToActionType(ActionChoice),
        Sound = string.IsNullOrWhiteSpace(Sound) ? null : Sound.Trim(),
        Enabled = Enabled,
        CaseSensitive = CaseSensitive,
        Priority = Math.Clamp(Priority, MinPriority, MaxPriority),
        Multiline = EffectiveMultiline,
        GagLine = EffectiveGagLine,
        SortOrder = IsNew ? 0 : _existing?.SortOrder ?? 0,
        CreatedAt = _existing?.CreatedAt ?? DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    public TriggerDefinition ToDefinition() => EntityMapper.ToDefinition(ToEntity());
}
