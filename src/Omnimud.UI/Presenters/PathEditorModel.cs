using Omnimud.Core.Paths;
using Omnimud.Data.Entities;
using Omnimud.Data.Repositories;
using Omnimud.Data.Session;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Presenters;

/// <summary>
/// Rules of the path editor. A path is a run of "[count]letter" where each letter is an
/// abbreviation of the MUD's direction dictionary; it is stored collapsed ("nnn" → "3n").
/// Without a dictionary (<c>directions</c> null) the path is accepted as typed.
/// </summary>
public sealed class PathEditorModel
{
    public const string FieldName = "name";
    public const string FieldPath = "path";
    private const int MaxCountDigits = 3;

    private readonly PathEntity? _existing;
    private readonly IReadOnlyList<PathEntity> _others;
    private readonly DirectionDictionary? _dictionary;
    private readonly PathEngine? _engine;

    /// <param name="directions">The MUD's dictionary; empty = the MUD has none yet; null = do not validate against it.</param>
    public PathEditorModel(PathEntity? existing, bool isNew, IReadOnlyList<PathEntity>? all = null, IReadOnlyList<DirectionEntry>? directions = null)
    {
        _existing = existing;
        IsNew = isNew;
        _others = (all ?? []).Where(p => isNew || existing is null || p.Id != existing.Id).ToList();
        Name = existing?.Name ?? string.Empty;
        Path = existing?.Path ?? string.Empty;

        if (directions is not null)
        {
            _dictionary = new DirectionDictionary();
            _dictionary.Load(directions);
            _engine = new PathEngine(_dictionary);
        }
    }

    /// <summary>Reads the MUD's dictionary for the constructor.</summary>
    public static async Task<IReadOnlyList<DirectionEntry>> LoadDirectionsAsync(IDirectionRepository repository, int mudId, CancellationToken ct = default)
    {
        var entities = await repository.GetByMudAsync(mudId, ct).ConfigureAwait(false);
        return entities.Select(EntityMapper.ToEntry).OfType<DirectionEntry>().ToList();
    }

    public bool IsNew { get; }
    public string Name { get; set; }
    public string Path { get; set; }

    public bool ValidatesAgainstDictionary => _engine is not null;

    /// <summary>The path without blanks: "3n 2e" is a natural way of typing it.</summary>
    private string CompactPath => string.Concat(Path.Where(c => !char.IsWhiteSpace(c)));

    /// <summary>What gets stored.</summary>
    public string CollapsedPath => _engine is null || Validate(checkName: false) is not null
        ? CompactPath
        : _engine.Collapse(_engine.Expand(CompactPath));

    public EditorIssue? Validate() => Validate(checkName: true);

    private EditorIssue? Validate(bool checkName)
    {
        var name = Name.Trim();
        if (checkName)
        {
            if (name.Length == 0) return new(FieldName, Strings.PathEdit_ErrNameEmpty);
            if (_others.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                return new(FieldName, string.Format(Strings.PathEdit_ErrDuplicateName, name));
        }

        var path = CompactPath;
        if (path.Length == 0) return new(FieldPath, Strings.PathEdit_ErrPathEmpty);
        if (_dictionary is null || _engine is null) return null;

        if (_dictionary.GetAll().Count == 0) return new(FieldPath, Strings.PathEdit_ErrNoDirections);

        var digits = 0;
        foreach (var c in path)
        {
            if (char.IsDigit(c))
            {
                // Expanding "999999999n" would exhaust memory: no real path repeats a step a thousand times.
                if (++digits > MaxCountDigits) return new(FieldPath, Strings.PathEdit_ErrCountTooBig);
                continue;
            }
            digits = 0;
            if (!_dictionary.IsKnownDirection(c.ToString()))
                return new(FieldPath, string.Format(Strings.PathEdit_ErrUnknownAbbreviation, c));
        }

        if (char.IsDigit(path[^1])) return new(FieldPath, Strings.PathEdit_ErrEndsWithNumber);
        if (!_engine.IsValid(path)) return new(FieldPath, Strings.PathEdit_ErrInvalid);
        return null;
    }

    /// <summary>False when some direction has no (known) opposite: "_name -r" will not work.</summary>
    public bool IsReversible
    {
        get
        {
            if (_engine is null) return true;
            var reversed = _engine.Reverse(CompactPath);
            return reversed is not null && reversed.All(d => !string.IsNullOrWhiteSpace(d));
        }
    }

    /// <summary>Questions the user must answer Yes to before saving. Call after a successful <see cref="Validate()"/>.</summary>
    public IReadOnlyList<string> Warnings()
    {
        var warnings = new List<string>();
        if (!IsReversible) warnings.Add(Strings.PathEdit_WarnNotReversible);

        var collapsed = CollapsedPath;
        if (_others.FirstOrDefault(p => p.Path == collapsed) is { } same)
            warnings.Add(string.Format(Strings.PathEdit_WarnSamePath, same.Name));
        return warnings;
    }

    /// <summary>"north, north, east…" (or the reason it cannot be expanded).</summary>
    public string DescribeExpansion()
    {
        if (_engine is null) return Strings.PathEdit_ExpansionUnavailable;
        if (CompactPath.Length == 0) return string.Empty;
        if (Validate(checkName: false) is { } issue) return issue.Message;

        var steps = _engine.Expand(CompactPath);
        return string.Format(Strings.PathEdit_Expansion, steps.Count, string.Join(", ", steps));
    }

    public PathEntity ToEntity() => new()
    {
        Id = IsNew ? 0 : _existing?.Id ?? 0,
        CharacterId = _existing?.CharacterId ?? 0,
        Name = Name.Trim(),
        Path = CollapsedPath,
    };
}
