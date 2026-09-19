using System.Text;
using Omnimud.Data.Exchange;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;

namespace Omnimud.UI.Presenters;

/// <summary>
/// Export to and import from .omnimud files: file pickers, the two-step import (analyze,
/// resolve conflicts, apply), the final summary and every error message. No WinForms here.
/// </summary>
public sealed class ImportExportPresenter
{
    private readonly IExchangeService _exchange;
    private readonly IUserPrompts _prompts;
    private readonly IImportConflictResolver _conflicts;

    public ImportExportPresenter(IExchangeService exchange, IUserPrompts prompts, IImportConflictResolver conflicts)
    {
        _exchange = exchange;
        _prompts = prompts;
        _conflicts = conflicts;
    }

    public static string FileFilter => Strings.Exchange_FileFilter;

    // ── Export ─────────────────────────────────────────────────────────────

    /// <summary>Asks whether to include the characters and where to save. False = cancelled or failed.</summary>
    public Task<bool> ExportMudAsync(int mudId, string mudName, CancellationToken ct = default)
    {
        var includeCharacters = _prompts.Confirm(string.Format(Strings.Exchange_IncludeCharacters, mudName), Strings.Exchange_ExportMudTitle);
        return ExportAsync(Strings.Exchange_ExportMudTitle, mudName, () => _exchange.ExportMudAsync(mudId, includeCharacters, ct), ct);
    }

    /// <summary>Aliases, triggers, paths, movements and options of the character. Never the password.</summary>
    public Task<bool> ExportCharacterAsync(int characterId, string characterName, CancellationToken ct = default) =>
        ExportAsync(Strings.Exchange_ExportCharacterTitle, characterName, () => _exchange.ExportCharacterAsync(characterId, ct), ct);

    /// <summary>Generic export: picks the file, writes what <paramref name="export"/> produces and confirms.</summary>
    public async Task<bool> ExportAsync(string title, string suggestedName, Func<Task<string>> export, CancellationToken ct = default)
    {
        var path = _prompts.PickSaveFile(title, FileFilter, SafeFileName(suggestedName) + IExchangeService.FileExtension);
        if (path is null) return false;

        try
        {
            var json = await export();
            await _exchange.SaveToFileAsync(path, json, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _prompts.Error(string.Format(Strings.Exchange_ExportFailed, path, ex.Message), title);
            return false;
        }

        _prompts.Info(string.Format(Strings.Exchange_ExportDone, path), title);
        return true;
    }

    // ── Import ─────────────────────────────────────────────────────────────

    /// <summary>Asks for the file and imports it. Null = cancelled or rejected (the user has already been told why).</summary>
    public async Task<ImportSummary?> ImportAsync(ImportTarget target, CancellationToken ct = default)
    {
        var path = _prompts.PickOpenFile(Strings.Exchange_ImportTitle, FileFilter);
        return path is null ? null : await ImportFileAsync(path, target, ct);
    }

    public Task<ImportSummary?> ImportFileAsync(string path, ImportTarget target, CancellationToken ct = default) =>
        RunImportAsync(async () =>
        {
            var json = await _exchange.LoadFromFileAsync(path, ct);
            return await _exchange.AnalyzeAsync(json, target, ct);
        }, ct);

    /// <summary>
    /// Steps shared by every import (file or copy from another character): analyze, ask about the
    /// conflicts, apply and show the summary.
    /// </summary>
    public async Task<ImportSummary?> RunImportAsync(Func<Task<ImportAnalysis>> analyze, CancellationToken ct = default)
    {
        ImportAnalysis analysis;
        try
        {
            analysis = await analyze();
        }
        catch (ExchangeFormatException ex)
        {
            _prompts.Error(DescribeError(ex.Error), Strings.Exchange_ImportTitle);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _prompts.Error(string.Format(Strings.Exchange_ReadFailed, ex.Message), Strings.Exchange_ImportTitle);
            return null;
        }

        if (analysis.Items.Count == 0)
        {
            _prompts.Info(Strings.Exchange_NothingToImport, Strings.Exchange_ImportTitle);
            return null;
        }

        IReadOnlyDictionary<string, ImportDecision>? decisions = null;
        if (analysis.HasConflicts)
        {
            decisions = _conflicts.Resolve(analysis.Items);
            if (decisions is null) return null;
        }

        ImportSummary summary;
        try
        {
            summary = await _exchange.ApplyAsync(analysis, decisions, ct);
        }
        catch (ExchangeFormatException ex)
        {
            _prompts.Error(DescribeError(ex.Error), Strings.Exchange_ImportTitle);
            return null;
        }

        var text = FormatSummary(summary);
        if (summary.Failed > 0) _prompts.Warn(text, Strings.Exchange_ImportTitle);
        else _prompts.Info(text, Strings.Exchange_ImportTitle);
        return summary;
    }

    public static string DescribeError(ExchangeError error) => error switch
    {
        ExchangeError.TooLarge => Strings.Exchange_ErrorTooLarge,
        ExchangeError.InvalidJson => Strings.Exchange_ErrorInvalidJson,
        ExchangeError.NotAnOmnimudFile => Strings.Exchange_ErrorNotOmnimud,
        ExchangeError.UnsupportedVersion => Strings.Exchange_ErrorUnsupportedVersion,
        ExchangeError.InvalidContent => Strings.Exchange_ErrorInvalidContent,
        ExchangeError.TargetRequired => Strings.Exchange_ErrorTargetRequired,
        _ => Strings.Exchange_ErrorInvalidContent,
    };

    /// <summary>Counts first (what a screen reader user wants to hear), then the elements that failed.</summary>
    public static string FormatSummary(ImportSummary summary)
    {
        var text = new StringBuilder();
        text.AppendLine(Strings.Exchange_SummaryHeader);
        text.AppendLine(string.Format(Strings.Exchange_SummaryAdded, summary.Added));
        text.AppendLine(string.Format(Strings.Exchange_SummaryOverwritten, summary.Overwritten));
        text.AppendLine(string.Format(Strings.Exchange_SummarySkipped, summary.Skipped));
        text.Append(string.Format(Strings.Exchange_SummaryFailed, summary.Failed));

        foreach (var failed in summary.Results.Where(r => r.Outcome == ImportOutcome.Failed))
        {
            text.AppendLine();
            text.Append(string.Format(Strings.Exchange_SummaryFailedItem,
                string.Format(Strings.Exchange_ItemFormat, ImportConflictsPresenter.KindName(failed.Kind), failed.Name),
                failed.Error ?? string.Empty));
        }
        return text.ToString();
    }

    internal static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return clean.Length == 0 ? "omnimud" : clean;
    }
}
