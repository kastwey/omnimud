using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Omnimud.Data.Exchange;

/// <summary>Reading, writing and validating the JSON of .omnimud documents.</summary>
internal static class ExchangeJson
{
    private const int MaxNameLength = 256;
    private const int MaxTextLength = 1024 * 1024;
    private const int MaxCharacters = 1_000;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Readable Spanish text and apostrophes instead of \u escapes. The output is a file, never HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        MaxDepth = 32,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string Serialize(ExchangeDocument document) => JsonSerializer.Serialize(document, Options);

    public static ExchangeDocument Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ExchangeFormatException(ExchangeError.InvalidJson, "The file is empty.");
        if (json.Length > IExchangeService.MaxDocumentLength)
            throw new ExchangeFormatException(ExchangeError.TooLarge,
                $"The file is too large ({json.Length:N0} characters; the limit is {IExchangeService.MaxDocumentLength:N0}).");

        CheckHeader(json);

        ExchangeDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<ExchangeDocument>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new ExchangeFormatException(ExchangeError.InvalidJson, $"The file has an unexpected structure: {ex.Message}", ex);
        }

        if (document is null)
            throw new ExchangeFormatException(ExchangeError.InvalidJson, "The file is empty.");

        Normalize(document);
        CheckContent(document);
        return document;
    }

    /// <summary>The header is checked on the raw JSON so a file from a newer version gets "unsupported version"
    /// instead of a confusing structural error.</summary>
    private static void CheckHeader(string json)
    {
        JsonDocument dom;
        try
        {
            dom = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        }
        catch (JsonException ex)
        {
            throw new ExchangeFormatException(ExchangeError.InvalidJson, $"The file is not valid JSON or is damaged: {ex.Message}", ex);
        }

        using (dom)
        {
            if (dom.RootElement.ValueKind != JsonValueKind.Object)
                throw new ExchangeFormatException(ExchangeError.NotAnOmnimudFile, "The file is not an Omnimud export.");

            JsonElement? format = null, version = null;
            foreach (var property in dom.RootElement.EnumerateObject())
            {
                if (property.NameEquals("format") || property.Name.Equals("format", StringComparison.OrdinalIgnoreCase)) format = property.Value;
                else if (property.Name.Equals("formatVersion", StringComparison.OrdinalIgnoreCase)) version = property.Value;
            }

            if (format is not { ValueKind: JsonValueKind.String } f
                || !string.Equals(f.GetString(), ExchangeDocument.FormatName, StringComparison.OrdinalIgnoreCase)
                || version is not { ValueKind: JsonValueKind.Number } v
                || !v.TryGetInt32(out var number)
                || number < 1)
            {
                throw new ExchangeFormatException(ExchangeError.NotAnOmnimudFile,
                    "The file is not an Omnimud export (\"format\" and \"formatVersion\" are missing or wrong).");
            }

            if (number > ExchangeDocument.CurrentFormatVersion)
                throw new ExchangeFormatException(ExchangeError.UnsupportedVersion,
                    $"The file was written by a newer version of Omnimud (format {number}; this version understands up to {ExchangeDocument.CurrentFormatVersion}).");
        }
    }

    /// <summary>JSON nulls where the model expects a value become empty values, so the rest of the code does not care.</summary>
    private static void Normalize(ExchangeDocument document)
    {
        if (document.Mud is { } mud)
        {
            mud.Name = (mud.Name ?? string.Empty).Trim();
            mud.Host = (mud.Host ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(mud.Encoding)) mud.Encoding = "utf-8";
            mud.Directions ??= [];
            mud.Movements ??= [];
            foreach (var direction in mud.Directions)
            {
                direction.Direction ??= string.Empty;
                direction.Abbreviation ??= string.Empty;
            }
            NormalizeMovements(mud.Movements);
            if (mud.MessageRuleSet is { } set)
            {
                set.Name = (set.Name ?? string.Empty).Trim();
                set.Rules ??= [];
                if (string.IsNullOrWhiteSpace(set.Script)) set.Script = null;
                foreach (var rule in set.Rules)
                {
                    rule.Pattern ??= string.Empty;
                    if (string.IsNullOrEmpty(rule.Template)) rule.Template = "$0";
                }
            }
            if (mud.Characters is not null)
                foreach (var character in mud.Characters) NormalizeCharacter(character);
        }

        if (document.Character is not null) NormalizeCharacter(document.Character);
        NormalizeAliases(document.Aliases);
        NormalizeTriggers(document.Triggers);
        NormalizePaths(document.Paths);
        NormalizeMovements(document.Movements);
    }

    private static void NormalizeCharacter(CharacterDto character)
    {
        character.Name = (character.Name ?? string.Empty).Trim();
        character.Aliases ??= [];
        character.Triggers ??= [];
        character.Paths ??= [];
        character.Movements ??= [];
        NormalizeAliases(character.Aliases);
        NormalizeTriggers(character.Triggers);
        NormalizePaths(character.Paths);
        NormalizeMovements(character.Movements);
    }

    private static void NormalizeAliases(List<AliasDto>? aliases)
    {
        if (aliases is null) return;
        aliases.RemoveAll(a => a is null);
        foreach (var alias in aliases)
        {
            alias.Command ??= string.Empty;
            alias.Action ??= string.Empty;
        }
    }

    private static void NormalizeTriggers(List<TriggerDto>? triggers)
    {
        if (triggers is null) return;
        triggers.RemoveAll(t => t is null);
        foreach (var trigger in triggers)
        {
            trigger.Name ??= string.Empty;
            trigger.Pattern ??= string.Empty;
            trigger.Action ??= string.Empty;
        }
    }

    private static void NormalizePaths(List<PathDto>? paths)
    {
        if (paths is null) return;
        paths.RemoveAll(p => p is null);
        foreach (var path in paths)
        {
            path.Name ??= string.Empty;
            path.Path ??= string.Empty;
        }
    }

    private static void NormalizeMovements(List<MovementDto>? movements)
    {
        if (movements is null) return;
        movements.RemoveAll(m => m is null);
        foreach (var movement in movements) movement.Command ??= string.Empty;
    }

    private static void CheckContent(ExchangeDocument document)
    {
        var missing = document.Kind switch
        {
            ExchangeKind.Mud => document.Mud is null ? "mud" : null,
            ExchangeKind.Character => document.Character is null ? "character" : null,
            ExchangeKind.Aliases => document.Aliases is null ? "aliases" : null,
            ExchangeKind.Triggers => document.Triggers is null ? "triggers" : null,
            ExchangeKind.Paths => document.Paths is null ? "paths" : null,
            ExchangeKind.Movements => document.Movements is null ? "movements" : null,
            ExchangeKind.Options => document.Options is null ? "options" : null,
            ExchangeKind.Lists => null,
            _ => "kind"
        };
        if (missing is not null)
            throw new ExchangeFormatException(ExchangeError.InvalidContent, $"The file says it contains \"{document.Kind}\" but the \"{missing}\" section is missing.");

        CheckCount("aliases", document.Aliases?.Count);
        CheckCount("triggers", document.Triggers?.Count);
        CheckCount("paths", document.Paths?.Count);
        if (document.Mud is { } mud)
        {
            CheckCount("directions", mud.Directions.Count);
            CheckCount("message rules", mud.MessageRuleSet?.Rules.Count);
            CheckCount("characters", mud.Characters?.Count, MaxCharacters);
            foreach (var character in mud.Characters ?? []) CheckCharacterCounts(character);
        }
        if (document.Character is not null) CheckCharacterCounts(document.Character);
    }

    private static void CheckCharacterCounts(CharacterDto character)
    {
        CheckCount("aliases", character.Aliases.Count);
        CheckCount("triggers", character.Triggers.Count);
        CheckCount("paths", character.Paths.Count);
    }

    private static void CheckCount(string what, int? count, int max = IExchangeService.MaxListItems)
    {
        if (count > max)
            throw new ExchangeFormatException(ExchangeError.InvalidContent, $"The file has too many {what} ({count:N0}; the limit is {max:N0}).");
    }

    // ── Element validation: returns the problem, or null when the element can be imported ──

    public static string? Validate(AliasDto alias)
    {
        if (string.IsNullOrWhiteSpace(alias.Command)) return "The alias has no command.";
        if (alias.Command.Length > MaxNameLength) return "The alias command is too long.";
        if (alias.Action.Length > MaxTextLength) return "The alias action is too long.";
        return null;
    }

    public static string? Validate(TriggerDto trigger)
    {
        if (string.IsNullOrWhiteSpace(trigger.Name)) return "The trigger has no name.";
        if (trigger.Name.Length > MaxNameLength) return "The trigger name is too long.";
        if (string.IsNullOrEmpty(trigger.Pattern)) return $"The trigger '{trigger.Name}' has no pattern.";
        if (trigger.Pattern.Length > MaxTextLength || trigger.Action.Length > MaxTextLength) return $"The trigger '{trigger.Name}' is too long.";
        if (!Enum.IsDefined(trigger.PatternType)) return $"The trigger '{trigger.Name}' has an unknown pattern type.";
        if (!Enum.IsDefined(trigger.ActionType)) return $"The trigger '{trigger.Name}' has an unknown action type.";
        return null;
    }

    public static string? Validate(PathDto path)
    {
        if (string.IsNullOrWhiteSpace(path.Name)) return "The path has no name.";
        if (path.Name.Length > MaxNameLength) return "The path name is too long.";
        if (string.IsNullOrWhiteSpace(path.Path)) return $"The path '{path.Name}' is empty.";
        if (path.Path.Length > MaxTextLength) return $"The path '{path.Name}' is too long.";
        return null;
    }

    public static string? Validate(IReadOnlyList<MovementDto> movements)
    {
        var seen = new HashSet<int>();
        foreach (var movement in movements)
        {
            if (!Omnimud.Core.Session.MovementKeys.IsValid(movement.Key)) return $"Movement key {movement.Key} is not a movement key code (0-17).";
            // An empty command is valid: it switches off a key that would otherwise send its default command.
            if (movement.Command is null) return $"Movement key {movement.Key} has no command.";
            if (movement.Command.Length > MaxTextLength) return $"The command of movement key {movement.Key} is too long.";
            if (!seen.Add(movement.Key)) return $"Movement key {movement.Key} appears more than once.";
        }
        return null;
    }

    public static string? Validate(MudDto mud)
    {
        if (string.IsNullOrWhiteSpace(mud.Name)) return "The MUD has no name.";
        if (mud.Name.Length > MaxNameLength) return "The MUD name is too long.";
        if (string.IsNullOrWhiteSpace(mud.Host)) return $"The MUD '{mud.Name}' has no host.";
        if (mud.Port is < 1 or > 65_535) return $"The MUD '{mud.Name}' has an invalid port ({mud.Port}).";
        if ((mud.LoginScript?.Length ?? 0) > MaxTextLength) return $"The login script of '{mud.Name}' is too long.";

        var names = new HashSet<string>(StringComparer.Ordinal);
        var abbreviations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var direction in mud.Directions)
        {
            if (string.IsNullOrWhiteSpace(direction.Direction)) return "A direction has no name.";
            if (direction.Abbreviation.Length != 1) return $"The abbreviation of direction '{direction.Direction}' must be exactly one character.";
            if (!names.Add(direction.Direction)) return $"The direction '{direction.Direction}' appears more than once.";
            if (!abbreviations.Add(direction.Abbreviation)) return $"The abbreviation '{direction.Abbreviation}' appears more than once.";
        }

        return Validate(mud.Movements);
    }

    public static string? Validate(MessageRuleSetDto set)
    {
        if (string.IsNullOrWhiteSpace(set.Name)) return "The message rule set has no name.";
        if (set.Name.Length > MaxNameLength) return "The name of the message rule set is too long.";
        if (set.Script is { Length: > MaxTextLength }) return $"The script of '{set.Name}' is too long.";
        foreach (var rule in set.Rules)
        {
            if (string.IsNullOrEmpty(rule.Pattern)) return $"A rule of '{set.Name}' has no pattern.";
            if (rule.Pattern.Length > MaxTextLength || rule.Template.Length > MaxTextLength) return $"A rule of '{set.Name}' is too long.";
            try
            {
                _ = new Regex(rule.Pattern, RegexOptions.None, TimeSpan.FromMilliseconds(250));
            }
            catch (ArgumentException ex)
            {
                return $"The rule '{rule.Pattern}' of '{set.Name}' is not a valid regular expression: {ex.Message}";
            }
        }
        return null;
    }

    public static string? Validate(CharacterDto character)
    {
        if (string.IsNullOrWhiteSpace(character.Name)) return "The character has no name.";
        if (character.Name.Length > MaxNameLength) return "The character name is too long.";

        var commands = new HashSet<string>(StringComparer.Ordinal);
        foreach (var alias in character.Aliases)
        {
            if (Validate(alias) is { } error) return error;
            if (!commands.Add(alias.Command)) return $"The alias '{alias.Command}' appears more than once.";
        }

        foreach (var trigger in character.Triggers)
            if (Validate(trigger) is { } error) return error;

        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in character.Paths)
        {
            if (Validate(path) is { } error) return error;
            if (!paths.Add(path.Name)) return $"The path '{path.Name}' appears more than once.";
        }

        return Validate(character.Movements);
    }
}
