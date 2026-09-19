using Omnimud.Data.Exchange;
using Omnimud.Data.Repositories;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;

namespace Omnimud.UI.Presenters;

/// <summary>Another character the lists can be copied from.</summary>
public sealed record CharacterChoice(int Id, string Name, string MudName)
{
    /// <summary>"Gandalf (Reinos de Leyenda)", as the original client listed them.</summary>
    public string Display => string.Format(Strings.PickChar_ItemFormat, Name, MudName);
    public override string ToString() => Display;
}

/// <summary>
/// Export, import from file and copy from another character for ONE list (aliases, triggers or
/// paths) of one character. The two-step import, the conflicts and the summary are the shared
/// <see cref="ImportExportPresenter"/>; this class adds what is specific to a list.
/// </summary>
public sealed class ListExchangePresenter
{
    private readonly IExchangeService _exchange;
    private readonly ImportExportPresenter _io;
    private readonly IUserPrompts _prompts;
    private readonly ExchangeParts _part;
    private readonly int _characterId;
    private readonly string _characterName;

    /// <param name="part">Exactly one of Aliases, Triggers or Paths.</param>
    public ListExchangePresenter(IExchangeService exchange, IUserPrompts prompts, IImportConflictResolver conflicts,
        ExchangeParts part, int characterId, string characterName)
    {
        if (part is not (ExchangeParts.Aliases or ExchangeParts.Triggers or ExchangeParts.Paths))
            throw new ArgumentOutOfRangeException(nameof(part), part, "One list: Aliases, Triggers or Paths.");

        _exchange = exchange;
        _prompts = prompts;
        _io = new ImportExportPresenter(exchange, prompts, conflicts);
        _part = part;
        _characterId = characterId;
        _characterName = characterName;
    }

    private string ListName => _part switch
    {
        ExchangeParts.Aliases => Strings.Exch_ListAliases,
        ExchangeParts.Triggers => Strings.Exch_ListTriggers,
        _ => Strings.Exch_ListPaths,
    };

    private ExchangeKind ExpectedKind => _part switch
    {
        ExchangeParts.Aliases => ExchangeKind.Aliases,
        ExchangeParts.Triggers => ExchangeKind.Triggers,
        _ => ExchangeKind.Paths,
    };

    /// <summary>False = cancelled or failed (the user has been told).</summary>
    public Task<bool> ExportAsync(CancellationToken ct = default)
    {
        var title = string.Format(Strings.Exch_ExportTitle, ListName);
        var suggested = $"{_characterName} - {ListName}";
        return _io.ExportAsync(title, suggested, () => _part switch
        {
            ExchangeParts.Aliases => _exchange.ExportAliasesAsync(_characterId, ct),
            ExchangeParts.Triggers => _exchange.ExportTriggersAsync(_characterId, ct),
            _ => _exchange.ExportPathsAsync(_characterId, ct),
        }, ct);
    }

    /// <summary>Asks for the file. True when something was written to the list.</summary>
    public async Task<bool> ImportFromFileAsync(CancellationToken ct = default)
    {
        var path = _prompts.PickOpenFile(string.Format(Strings.Exch_ImportTitle, ListName), ImportExportPresenter.FileFilter);
        return path is not null && await ImportFileAsync(path, ct);
    }

    public async Task<bool> ImportFileAsync(string path, CancellationToken ct = default)
    {
        ImportAnalysis? analysis = null;
        Exception? failure = null;
        try
        {
            var json = await _exchange.LoadFromFileAsync(path, ct);
            analysis = await _exchange.AnalyzeAsync(json, ImportTarget.ForCharacter(_characterId), ct);
        }
        catch (Exception ex) when (ex is ExchangeFormatException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            failure = ex; // explained by the shared presenter below
        }

        // A whole character or a MUD must not slip in through the alias list.
        if (analysis is not null && analysis.Kind != ExpectedKind)
        {
            _prompts.Warn(string.Format(Strings.Exch_WrongKind, ListName), string.Format(Strings.Exch_ImportTitle, ListName));
            return false;
        }

        var summary = await _io.RunImportAsync(
            () => failure is null ? Task.FromResult(analysis!) : Task.FromException<ImportAnalysis>(failure), ct);
        return Wrote(summary);
    }

    /// <summary>Copies this list from another character of any MUD. True when something was written.</summary>
    public async Task<bool> ImportFromCharacterAsync(CharacterChoice source, CancellationToken ct = default)
    {
        if (source.Id == _characterId) return false;
        var summary = await _io.RunImportAsync(() => _exchange.AnalyzeCopyAsync(source.Id, _characterId, _part, ct), ct);
        return Wrote(summary);
    }

    private static bool Wrote(ImportSummary? summary) => summary is not null && summary.Added + summary.Overwritten > 0;

    /// <summary>Every character but this one, sorted by MUD and name.</summary>
    public static async Task<IReadOnlyList<CharacterChoice>> LoadOtherCharactersAsync(
        ICharacterRepository characters, IMudRepository muds, int exceptCharacterId, CancellationToken ct = default)
    {
        var result = new List<CharacterChoice>();
        foreach (var mud in await muds.GetAllAsync(ct))
            foreach (var character in await characters.GetByMudAsync(mud.Id, ct))
                if (character.Id != exceptCharacterId)
                    result.Add(new CharacterChoice(character.Id, character.Name, mud.Name));

        return [.. result.OrderBy(c => c.MudName, StringComparer.CurrentCultureIgnoreCase).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)];
    }
}
