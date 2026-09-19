using Omnimud.Core.Options;

namespace Omnimud.Data.Exchange;

/// <summary>Why an .omnimud file was rejected. The UI localizes by code; Message is English for logs.</summary>
public enum ExchangeError
{
    /// <summary>The text is larger than <see cref="IExchangeService.MaxDocumentLength"/>.</summary>
    TooLarge,
    /// <summary>Not JSON, truncated, or with values of the wrong type.</summary>
    InvalidJson,
    /// <summary>Valid JSON that is not an Omnimud export (missing or wrong "format"/"formatVersion").</summary>
    NotAnOmnimudFile,
    /// <summary>Written by a newer Omnimud: formatVersion is higher than this build understands.</summary>
    UnsupportedVersion,
    /// <summary>The section announced by "kind" is missing, or a list exceeds the allowed number of elements.</summary>
    InvalidContent,
    /// <summary>The file needs a destination (MUD or character) that was not given or does not exist.</summary>
    TargetRequired
}

public sealed class ExchangeFormatException : Exception
{
    public ExchangeFormatException(ExchangeError error, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
    }

    public ExchangeError Error { get; }
}

/// <summary>Where imported data goes. A MUD file needs none; a character file needs the MUD; loose
/// alias/trigger/path lists need the character; movements go to the character if given, else to the MUD;
/// options go to the character if given, else the MUD if given, else global.</summary>
public sealed record ImportTarget(int? MudId = null, int? CharacterId = null)
{
    public static ImportTarget None { get; } = new();
    public static ImportTarget ForMud(int mudId) => new(MudId: mudId);
    public static ImportTarget ForCharacter(int characterId) => new(CharacterId: characterId);
}

public enum ImportItemKind
{
    Mud,
    MessageRuleSet,
    Character,
    Alias,
    Trigger,
    Path,
    Movements,
    Options
}

public enum ImportItemStatus
{
    /// <summary>Does not exist yet: it will be added unless the decision says Skip.</summary>
    New,
    /// <summary>Something with the same name exists: it is skipped unless the decision says Overwrite.</summary>
    Conflict,
    /// <summary>Cannot be imported (see <see cref="ImportItem.Error"/>); reported as failed.</summary>
    Invalid
}

public enum ImportDecision
{
    /// <summary>Add it, replacing what exists.</summary>
    Overwrite,
    /// <summary>Leave the database as it is for this element.</summary>
    Skip
}

/// <summary>One element the user can decide about.</summary>
/// <param name="Key">Opaque, unique within the analysis; the key of the decisions dictionary.</param>
/// <param name="Name">Display name (MUD name, alias command, trigger name...).</param>
/// <param name="ParentKey">Key of the MUD item for characters inside a MUD file.</param>
public sealed record ImportItem(string Key, ImportItemKind Kind, string Name, ImportItemStatus Status, string? ParentKey = null, string? Error = null);

/// <summary>Result of the first step of an import. Hand it back to ApplyAsync with the decisions.</summary>
public sealed class ImportAnalysis
{
    internal ImportAnalysis(ExchangeDocument document, ImportTarget target, IReadOnlyList<ImportItem> items)
    {
        Document = document;
        Target = target;
        Items = items;
    }

    internal ExchangeDocument Document { get; }
    public ImportTarget Target { get; }
    public ExchangeKind Kind => Document.Kind;
    public IReadOnlyList<ImportItem> Items { get; }
    public IEnumerable<ImportItem> Conflicts => Items.Where(i => i.Status == ImportItemStatus.Conflict);
    public bool HasConflicts => Items.Any(i => i.Status == ImportItemStatus.Conflict);
}

public enum ImportOutcome
{
    Added,
    Overwritten,
    Skipped,
    Failed
}

public sealed record ImportItemResult(string Key, ImportItemKind Kind, string Name, ImportOutcome Outcome, string? Error = null);

public sealed class ImportSummary
{
    internal ImportSummary(IReadOnlyList<ImportItemResult> results)
    {
        Results = results;
    }

    public IReadOnlyList<ImportItemResult> Results { get; }
    public int Added => Results.Count(r => r.Outcome == ImportOutcome.Added);
    public int Overwritten => Results.Count(r => r.Outcome == ImportOutcome.Overwritten);
    public int Skipped => Results.Count(r => r.Outcome == ImportOutcome.Skipped);
    public int Failed => Results.Count(r => r.Outcome == ImportOutcome.Failed);
}

[Flags]
public enum ExchangeParts
{
    None = 0,
    Aliases = 1,
    Triggers = 2,
    Paths = 4,
    Movements = 8,
    All = Aliases | Triggers | Paths | Movements
}

/// <summary>Export to and import from .omnimud (JSON) text, and copy between characters.</summary>
public interface IExchangeService
{
    const string FileExtension = ".omnimud";
    /// <summary>Largest accepted document, in characters (16 MiB).</summary>
    const int MaxDocumentLength = 16 * 1024 * 1024;
    /// <summary>Largest accepted number of elements in any list of the document.</summary>
    const int MaxListItems = 10_000;

    Task<string> ExportMudAsync(int mudId, bool includeCharacters, CancellationToken ct = default);
    /// <summary>Aliases, triggers, paths, movements and options. Never the password.</summary>
    Task<string> ExportCharacterAsync(int characterId, CancellationToken ct = default);
    Task<string> ExportAliasesAsync(int characterId, CancellationToken ct = default);
    Task<string> ExportTriggersAsync(int characterId, CancellationToken ct = default);
    Task<string> ExportPathsAsync(int characterId, CancellationToken ct = default);
    /// <summary>Exactly one of the owners.</summary>
    Task<string> ExportMovementsAsync(int? mudId, int? characterId, CancellationToken ct = default);
    /// <summary>The scope's own options, or the ones it resolves to when it inherits.</summary>
    Task<string> ExportOptionsAsync(OptionScope scope, int? scopeId, CancellationToken ct = default);

    Task SaveToFileAsync(string path, string json, CancellationToken ct = default);
    /// <exception cref="ExchangeFormatException">The file is larger than allowed.</exception>
    Task<string> LoadFromFileAsync(string path, CancellationToken ct = default);

    /// <summary>Step 1: validates the document and says which elements are new and which collide. Writes nothing.</summary>
    /// <exception cref="ExchangeFormatException">Corrupt file, future version, missing destination...</exception>
    Task<ImportAnalysis> AnalyzeAsync(string json, ImportTarget target, CancellationToken ct = default);

    /// <summary>Step 1 for "import from another character": same analysis, no file involved.</summary>
    Task<ImportAnalysis> AnalyzeCopyAsync(int sourceCharacterId, int targetCharacterId, ExchangeParts parts, CancellationToken ct = default);

    /// <summary>Step 2: applies everything in ONE transaction. Elements without decision: new → added, conflict → skipped.
    /// A failing element is rolled back on its own and reported; an unexpected error rolls back the whole import and is thrown.</summary>
    Task<ImportSummary> ApplyAsync(ImportAnalysis analysis, IReadOnlyDictionary<string, ImportDecision>? decisions = null, CancellationToken ct = default);
}
