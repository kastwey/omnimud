using System.Data;
using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;
using Omnimud.Core.Options;
using Omnimud.Core.Triggers;
using Omnimud.Data.Entities;
using Omnimud.Data.Options;

namespace Omnimud.Data.Exchange;

/// <summary>
/// .omnimud export/import and copy between characters. Works straight on one connection so that a
/// whole import is a single transaction (the repositories open a connection per call).
/// </summary>
public sealed class ExchangeService : IExchangeService
{
    private const long MaxFileBytes = 4L * IExchangeService.MaxDocumentLength;

    private readonly IDbConnectionFactory _factory;
    private readonly OptionsService? _optionsService;

    /// <param name="optionsService">When given, its Changed event is raised for every scope whose options an import replaced.</param>
    public ExchangeService(IDbConnectionFactory factory, OptionsService? optionsService = null)
    {
        _factory = factory;
        _optionsService = optionsService;
    }

    // ═════════════════════════════════ Export ═════════════════════════════════

    public async Task<string> ExportMudAsync(int mudId, bool includeCharacters, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        var db = new Db(conn, null, ct);
        var mud = await db.SingleOrDefaultAsync<MudEntity>("SELECT * FROM Muds WHERE Id = @Id", new { Id = mudId })
                  ?? throw new InvalidOperationException($"MUD {mudId} does not exist.");

        var dto = new MudDto
        {
            Name = mud.Name,
            Host = mud.Host,
            Port = mud.Port,
            UseTls = mud.UseTls,
            ValidateCertificate = mud.ValidateCertificate,
            Encoding = mud.Encoding,
            SaveCommand = mud.SaveCommand,
            QuitCommand = mud.QuitCommand,
            LoginScript = mud.LoginScript,
            SoundDirectory = mud.SoundDirectory,
            MovementMode = mud.MovementMode,
            Directions = (await db.QueryAsync<DirectionEntity>("SELECT * FROM Directions WHERE MudId = @Id ORDER BY Id", new { Id = mudId }))
                .Select(d => new DirectionDto { Direction = d.Direction, Abbreviation = d.Abbreviation, Opposite = d.OppositeDirection }).ToList(),
            Movements = await ReadMovementsAsync(db, mudId, null),
            Options = await ReadOwnOptionsAsync(db, OptionScope.Mud, mudId)
        };

        if (mud.MessageRuleSetId is { } ruleSetId
            && await db.SingleOrDefaultAsync<MessageRuleSetEntity>("SELECT * FROM MessageRuleSets WHERE Id = @Id", new { Id = ruleSetId }) is { } set)
        {
            dto.MessageRuleSet = new MessageRuleSetDto
            {
                Name = set.Name,
                Script = set.IsScript ? set.Script : null,
                Rules = (await ReadRulesAsync(db, ruleSetId)).ToList()
            };
        }

        if (includeCharacters)
        {
            var characters = await db.QueryAsync<CharacterEntity>("SELECT * FROM Characters WHERE MudId = @Id ORDER BY Name COLLATE NOCASE", new { Id = mudId });
            dto.Characters = [];
            foreach (var character in characters)
            {
                dto.Characters.Add(await ReadCharacterAsync(db, character, mud.Name));
                if (character.Id == mud.DefaultCharacterId) dto.DefaultCharacter = character.Name;
            }
        }

        return Write(ExchangeKind.Mud, d => d.Mud = dto);
    }

    public async Task<string> ExportCharacterAsync(int characterId, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        var db = new Db(conn, null, ct);
        var character = await GetCharacterAsync(db, characterId);
        var mudName = await db.ScalarAsync<string?>("SELECT Name FROM Muds WHERE Id = @Id", new { Id = character.MudId });
        var dto = await ReadCharacterAsync(db, character, mudName);
        return Write(ExchangeKind.Character, d => d.Character = dto);
    }

    public async Task<string> ExportAliasesAsync(int characterId, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        var aliases = await ReadAliasesAsync(new Db(conn, null, ct), characterId);
        return Write(ExchangeKind.Aliases, d => d.Aliases = aliases);
    }

    public async Task<string> ExportTriggersAsync(int characterId, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        var triggers = await ReadTriggersAsync(new Db(conn, null, ct), characterId);
        return Write(ExchangeKind.Triggers, d => d.Triggers = triggers);
    }

    public async Task<string> ExportPathsAsync(int characterId, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        var paths = await ReadPathsAsync(new Db(conn, null, ct), characterId);
        return Write(ExchangeKind.Paths, d => d.Paths = paths);
    }

    public async Task<string> ExportMovementsAsync(int? mudId, int? characterId, CancellationToken ct = default)
    {
        if ((mudId is null) == (characterId is null))
            throw new ArgumentException("Exactly one of the MUD or the character must be given.");
        using var conn = _factory.Create();
        var movements = await ReadMovementsAsync(new Db(conn, null, ct), mudId, characterId);
        return Write(ExchangeKind.Movements, d => d.Movements = movements);
    }

    public async Task<string> ExportOptionsAsync(OptionScope scope, int? scopeId, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        var db = new Db(conn, null, ct);

        Dictionary<string, string>? options = null;
        if (scope == OptionScope.Character && scopeId is not null)
        {
            options = await ReadOwnOptionsAsync(db, OptionScope.Character, scopeId);
            if (options is null)
            {
                scope = OptionScope.Mud;
                scopeId = await db.ScalarAsync<int?>("SELECT MudId FROM Characters WHERE Id = @Id", new { Id = scopeId });
            }
        }
        if (options is null && scope == OptionScope.Mud && scopeId is not null)
            options = await ReadOwnOptionsAsync(db, OptionScope.Mud, scopeId);
        options ??= await ReadOwnOptionsAsync(db, OptionScope.Global, null)
                    ?? new Dictionary<string, string>(OptionsSerializer.Serialize(new OmnimudOptions()));

        return Write(ExchangeKind.Options, d => d.Options = options);
    }

    public async Task SaveToFileAsync(string path, string json, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct).ConfigureAwait(false);
    }

    public async Task<string> LoadFromFileAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var length = new FileInfo(path).Length;
        if (length > MaxFileBytes)
            throw new ExchangeFormatException(ExchangeError.TooLarge, $"The file is too large ({length:N0} bytes; the limit is {MaxFileBytes:N0}).");
        return await File.ReadAllTextAsync(path, Encoding.UTF8, ct).ConfigureAwait(false);
    }

    private static string Write(ExchangeKind kind, Action<ExchangeDocument> fill)
    {
        var document = new ExchangeDocument { Kind = kind, ExportedAt = DateTime.UtcNow };
        fill(document);
        return ExchangeJson.Serialize(document);
    }

    // ═════════════════════════════════ Import ═════════════════════════════════

    public async Task<ImportAnalysis> AnalyzeAsync(string json, ImportTarget target, CancellationToken ct = default)
    {
        var document = ExchangeJson.Parse(json);
        return await AnalyzeDocumentAsync(document, target ?? ImportTarget.None, ct).ConfigureAwait(false);
    }

    public async Task<ImportAnalysis> AnalyzeCopyAsync(int sourceCharacterId, int targetCharacterId, ExchangeParts parts, CancellationToken ct = default)
    {
        if (sourceCharacterId == targetCharacterId)
            throw new ArgumentException("Source and target are the same character.", nameof(targetCharacterId));

        var document = new ExchangeDocument { Kind = ExchangeKind.Lists, ExportedAt = DateTime.UtcNow };
        using (var conn = _factory.Create())
        {
            var db = new Db(conn, null, ct);
            await GetCharacterAsync(db, sourceCharacterId);
            if (parts.HasFlag(ExchangeParts.Aliases)) document.Aliases = await ReadAliasesAsync(db, sourceCharacterId);
            if (parts.HasFlag(ExchangeParts.Triggers)) document.Triggers = await ReadTriggersAsync(db, sourceCharacterId);
            if (parts.HasFlag(ExchangeParts.Paths)) document.Paths = await ReadPathsAsync(db, sourceCharacterId);
            if (parts.HasFlag(ExchangeParts.Movements)) document.Movements = await ReadMovementsAsync(db, null, sourceCharacterId);
        }

        return await AnalyzeDocumentAsync(document, ImportTarget.ForCharacter(targetCharacterId), ct).ConfigureAwait(false);
    }

    private async Task<ImportAnalysis> AnalyzeDocumentAsync(ExchangeDocument document, ImportTarget target, CancellationToken ct)
    {
        using var conn = _factory.Create();
        var run = new ImportRun(new Db(conn, null, ct), decisions: null, apply: false);
        target = await ResolveTargetAsync(run.Db, document, target);
        await WalkAsync(run, document, target);
        return new ImportAnalysis(document, target, run.Items);
    }

    public async Task<ImportSummary> ApplyAsync(ImportAnalysis analysis, IReadOnlyDictionary<string, ImportDecision>? decisions = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        ImportRun run;
        using (var conn = _factory.Create())
        using (var tx = conn.BeginTransaction())
        {
            run = new ImportRun(new Db(conn, tx, ct), decisions ?? new Dictionary<string, ImportDecision>(), apply: true);
            // Existence is evaluated again inside the transaction: the database may have changed since the analysis.
            var target = await ResolveTargetAsync(run.Db, analysis.Document, analysis.Target);
            await WalkAsync(run, analysis.Document, target);
            tx.Commit();
        }

        foreach (var (scope, scopeId) in run.ChangedOptionScopes.Distinct())
            _optionsService?.NotifyChanged(scope, scopeId);

        return new ImportSummary(run.Results);
    }

    private static async Task<ImportTarget> ResolveTargetAsync(Db db, ExchangeDocument document, ImportTarget target)
    {
        var mudId = target.MudId;
        var characterId = target.CharacterId;

        if (characterId is not null)
        {
            mudId = await db.ScalarAsync<int?>("SELECT MudId FROM Characters WHERE Id = @Id", new { Id = characterId })
                    ?? throw new ExchangeFormatException(ExchangeError.TargetRequired, "The destination character does not exist.");
        }
        else if (mudId is not null && await db.ScalarAsync<int?>("SELECT Id FROM Muds WHERE Id = @Id", new { Id = mudId }) is null)
        {
            throw new ExchangeFormatException(ExchangeError.TargetRequired, "The destination MUD does not exist.");
        }

        if (document.Character is not null && mudId is null)
            throw new ExchangeFormatException(ExchangeError.TargetRequired, "A character file needs the MUD it will be imported into.");
        if ((document.Aliases is not null || document.Triggers is not null || document.Paths is not null) && characterId is null)
            throw new ExchangeFormatException(ExchangeError.TargetRequired, "Aliases, triggers and paths need the character they will be imported into.");
        if (document.Movements is not null && mudId is null)
            throw new ExchangeFormatException(ExchangeError.TargetRequired, "Movements need the MUD or the character they will be imported into.");

        return new ImportTarget(mudId, characterId);
    }

    /// <summary>The single walk over a document, used both to analyze (no writes) and to apply.</summary>
    private static async Task WalkAsync(ImportRun run, ExchangeDocument document, ImportTarget target)
    {
        if (document.Mud is { } mud)
            await WalkMudAsync(run, mud);

        if (document.Character is { } character)
            await WalkCharacterAsync(run, character, target.MudId, parentKey: null);

        if (target.CharacterId is { } characterId)
        {
            foreach (var alias in document.Aliases ?? [])
                await WalkAliasAsync(run, characterId, alias);

            var replacedTriggerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var trigger in document.Triggers ?? [])
                await WalkTriggerAsync(run, characterId, trigger, replacedTriggerNames);

            foreach (var path in document.Paths ?? [])
                await WalkPathAsync(run, characterId, path);
        }

        if (document.Movements is { } movements)
        {
            // Character if there is one, else the MUD.
            var ownerMudId = target.CharacterId is null ? target.MudId : null;
            await WalkMovementsAsync(run, ownerMudId, target.CharacterId, movements);
        }

        if (document.Options is { } options)
        {
            var (scope, scopeId) = target.CharacterId is not null ? (OptionScope.Character, target.CharacterId)
                : target.MudId is not null ? (OptionScope.Mud, target.MudId)
                : (OptionScope.Global, (int?)null);
            await WalkOptionsAsync(run, scope, scopeId, options);
        }
    }

    // ── MUD ────────────────────────────────────────────────────────────────

    private static async Task WalkMudAsync(ImportRun run, MudDto mud)
    {
        var db = run.Db;
        var mudKey = run.UniqueKey($"mud:{mud.Name}");
        var existingMudId = await db.ScalarAsync<int?>("SELECT Id FROM Muds WHERE Name = @Name COLLATE NOCASE ORDER BY Id LIMIT 1", new { mud.Name });
        var error = ExchangeJson.Validate(mud);

        // The rule set goes first so the MUD can point to it; if the MUD is not going to be written, neither is the set.
        var mudWillBeWritten = error is null && run.WouldWrite(mudKey, existingMudId is not null);
        int? ruleSetId = null;
        if (mud.MessageRuleSet is { } set)
            ruleSetId = await WalkRuleSetAsync(run, set, forceSkip: run.Apply && !mudWillBeWritten);

        int? mudId = existingMudId;
        var mudWritten = false;
        var mudFailed = error is not null;
        if (error is not null)
        {
            run.Report(mudKey, ImportItemKind.Mud, mud.Name, exists: existingMudId is not null, error);
            mudId = null;
        }
        else
        {
            var outcome = await run.ItemAsync(mudKey, ImportItemKind.Mud, mud.Name, existingMudId is not null, async () =>
            {
                mudId = await WriteMudAsync(run, mud, existingMudId, ruleSetId);
            });
            mudWritten = outcome is ImportOutcome.Added or ImportOutcome.Overwritten;
            mudFailed = outcome is ImportOutcome.Failed;
            if (mudFailed || (outcome is ImportOutcome.Skipped && existingMudId is null))
                mudId = null;
        }

        foreach (var character in mud.Characters ?? [])
        {
            if (run.Apply && mudId is null)
            {
                // No MUD to hang them from (it was new and skipped, or it failed).
                run.Results.Add(new ImportItemResult(run.UniqueKey($"character:{character.Name}"), ImportItemKind.Character, character.Name,
                    mudFailed ? ImportOutcome.Failed : ImportOutcome.Skipped,
                    mudFailed ? "The MUD of this character could not be imported." : null));
                continue;
            }
            await WalkCharacterAsync(run, character, mudId, mudKey);
        }

        if (run.Apply && mudWritten && mudId is not null && !string.IsNullOrEmpty(mud.DefaultCharacter))
        {
            await db.ExecuteAsync(
                """
                UPDATE Muds SET DefaultCharacterId = COALESCE(
                    (SELECT Id FROM Characters WHERE MudId = @MudId AND Name = @Name COLLATE NOCASE ORDER BY Id LIMIT 1), DefaultCharacterId)
                WHERE Id = @MudId;
                UPDATE Characters SET IsDefault = CASE WHEN Id = (SELECT DefaultCharacterId FROM Muds WHERE Id = @MudId) THEN 1 ELSE 0 END
                WHERE MudId = @MudId;
                """, new { MudId = mudId, Name = mud.DefaultCharacter });
        }
    }

    private static async Task<int> WriteMudAsync(ImportRun run, MudDto mud, int? existingMudId, int? ruleSetId)
    {
        var db = run.Db;
        var args = new
        {
            Id = existingMudId, mud.Name, mud.Host, mud.Port, mud.UseTls, mud.ValidateCertificate, mud.SaveCommand, mud.QuitCommand,
            mud.LoginScript, mud.SoundDirectory, mud.Encoding, mud.MovementMode, MessageRuleSetId = ruleSetId
        };

        int mudId;
        if (existingMudId is null)
        {
            mudId = await db.ScalarAsync<int>(
                """
                INSERT INTO Muds (Name, Host, Port, UseTls, ValidateCertificate, SaveCommand, QuitCommand, LoginScript,
                                  MessageRuleSetId, SoundDirectory, Encoding, MovementMode, CreatedAt, UpdatedAt)
                VALUES (@Name, @Host, @Port, @UseTls, @ValidateCertificate, @SaveCommand, @QuitCommand, @LoginScript,
                        @MessageRuleSetId, @SoundDirectory, @Encoding, @MovementMode, datetime('now'), datetime('now'));
                SELECT last_insert_rowid();
                """, args);
        }
        else
        {
            mudId = existingMudId.Value;
            await db.ExecuteAsync(
                """
                UPDATE Muds SET Name = @Name, Host = @Host, Port = @Port, UseTls = @UseTls, ValidateCertificate = @ValidateCertificate,
                    SaveCommand = @SaveCommand, QuitCommand = @QuitCommand, LoginScript = @LoginScript,
                    MessageRuleSetId = @MessageRuleSetId, SoundDirectory = @SoundDirectory, Encoding = @Encoding,
                    MovementMode = @MovementMode, UpdatedAt = datetime('now')
                WHERE Id = @Id
                """, args);
            await db.ExecuteAsync("DELETE FROM Directions WHERE MudId = @MudId", new { MudId = mudId });
        }

        foreach (var direction in mud.Directions)
        {
            await db.ExecuteAsync(
                "INSERT INTO Directions (MudId, Direction, Abbreviation, OppositeDirection) VALUES (@MudId, @Direction, @Abbreviation, @Opposite)",
                new { MudId = mudId, direction.Direction, direction.Abbreviation, direction.Opposite });
        }

        await WriteMovementsAsync(db, mudId, null, mud.Movements);
        await WriteOptionsAsync(run, OptionScope.Mud, mudId, mud.Options);
        return mudId;
    }

    // ── Message rule set ───────────────────────────────────────────────────

    /// <summary>Returns the id the MUD must point to, or null when there is none to point to.</summary>
    private static async Task<int?> WalkRuleSetAsync(ImportRun run, MessageRuleSetDto set, bool forceSkip)
    {
        var db = run.Db;
        var key = run.UniqueKey($"ruleset:{set.Name}");
        var existing = await db.SingleOrDefaultAsync<MessageRuleSetEntity>("SELECT * FROM MessageRuleSets WHERE Name = @Name COLLATE NOCASE", new { set.Name });
        int? existingId = existing?.Id;

        if (ExchangeJson.Validate(set) is { } error)
        {
            run.Report(key, ImportItemKind.MessageRuleSet, set.Name, existingId is not null, error);
            return existingId;
        }

        // An identical set needs no question: the MUD is simply linked to it.
        if (existing is not null && SameScript(existing, set)
            && (await ReadRulesAsync(db, existing.Id)).SequenceEqual(set.Rules, MessageRuleDtoComparer.Instance))
            return existingId;

        if (forceSkip)
        {
            run.Results.Add(new ImportItemResult(key, ImportItemKind.MessageRuleSet, set.Name, ImportOutcome.Skipped));
            return existingId;
        }

        int? result = existingId;
        var outcome = await run.ItemAsync(key, ImportItemKind.MessageRuleSet, set.Name, existingId is not null, async () =>
        {
            if (existingId is null)
            {
                result = await db.ScalarAsync<int>(
                    "INSERT INTO MessageRuleSets (Name, IsBuiltIn, Script) VALUES (@Name, 0, @Script); SELECT last_insert_rowid();",
                    new { set.Name, set.Script });
            }
            else
            {
                await db.ExecuteAsync("UPDATE MessageRuleSets SET Script = @Script WHERE Id = @Id", new { Id = existingId, set.Script });
                await db.ExecuteAsync("DELETE FROM MessageRules WHERE RuleSetId = @Id", new { Id = existingId });
            }

            for (var i = 0; i < set.Rules.Count; i++)
            {
                var rule = set.Rules[i];
                await db.ExecuteAsync(
                    """
                    INSERT INTO MessageRules (RuleSetId, SortOrder, Pattern, Template, CaseSensitive, Channel, Enabled)
                    VALUES (@RuleSetId, @SortOrder, @Pattern, @Template, @CaseSensitive, @Channel, @Enabled)
                    """,
                    new { RuleSetId = result, SortOrder = i + 1, rule.Pattern, rule.Template, rule.CaseSensitive, rule.Channel, rule.Enabled });
            }
        });

        return outcome == ImportOutcome.Failed ? existingId : result;
    }

    /// <summary>
    /// Same script, ignoring line-ending style. A file written before scripts existed carries the
    /// patterns of a built-in set and no script: that is still the built-in set, not a conflict.
    /// </summary>
    private static bool SameScript(MessageRuleSetEntity existing, MessageRuleSetDto incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming.Script))
            return !existing.IsScript || existing.IsBuiltIn;
        return existing.IsScript
            && string.Equals(existing.Script!.ReplaceLineEndings().Trim(), incoming.Script.ReplaceLineEndings().Trim(), StringComparison.Ordinal);
    }

    // ── Character ──────────────────────────────────────────────────────────

    private static async Task WalkCharacterAsync(ImportRun run, CharacterDto character, int? mudId, string? parentKey)
    {
        var db = run.Db;
        var key = run.UniqueKey($"character:{character.Name}");
        var existingId = mudId is null
            ? null
            : await db.ScalarAsync<int?>("SELECT Id FROM Characters WHERE MudId = @MudId AND Name = @Name COLLATE NOCASE ORDER BY Id LIMIT 1",
                new { MudId = mudId, character.Name });

        if (ExchangeJson.Validate(character) is { } error)
        {
            run.Report(key, ImportItemKind.Character, character.Name, existingId is not null, error, parentKey);
            return;
        }

        await run.ItemAsync(key, ImportItemKind.Character, character.Name, existingId is not null, async () =>
        {
            int characterId;
            if (existingId is null)
            {
                // Imported characters never carry a password: the user types it again.
                characterId = await db.ScalarAsync<int>(
                    """
                    INSERT INTO Characters (MudId, Name, EncryptedPassword, IsDefault, MovementMode, CreatedAt, UpdatedAt)
                    VALUES (@MudId, @Name, NULL, 0, @MovementMode, datetime('now'), datetime('now'));
                    SELECT last_insert_rowid();
                    """, new { MudId = mudId, character.Name, character.MovementMode });
            }
            else
            {
                // Overwriting keeps the row (and its stored password) and replaces everything that hangs from it.
                characterId = existingId.Value;
                await db.ExecuteAsync(
                    """
                    UPDATE Characters SET Name = @Name, MovementMode = @MovementMode, UpdatedAt = datetime('now') WHERE Id = @Id;
                    DELETE FROM Aliases WHERE CharacterId = @Id;
                    DELETE FROM Triggers WHERE CharacterId = @Id;
                    DELETE FROM Paths WHERE CharacterId = @Id;
                    """, new { Id = characterId, character.Name, character.MovementMode });
            }

            foreach (var alias in character.Aliases) await InsertAliasAsync(db, characterId, alias);
            for (var i = 0; i < character.Triggers.Count; i++) await InsertTriggerAsync(db, characterId, character.Triggers[i], i + 1);
            foreach (var path in character.Paths) await InsertPathAsync(db, characterId, path);
            await WriteMovementsAsync(db, null, characterId, character.Movements);
            await WriteOptionsAsync(run, OptionScope.Character, characterId, character.Options);
        }, parentKey);
    }

    // ── Loose lists ────────────────────────────────────────────────────────

    private static async Task WalkAliasAsync(ImportRun run, int characterId, AliasDto alias)
    {
        var db = run.Db;
        var baseKey = $"alias:{alias.Command}";
        var repeated = run.IsKeyUsed(baseKey);
        var key = run.UniqueKey(baseKey);
        // Aliases are matched case-sensitively, and so is their uniqueness.
        var existingId = await db.ScalarAsync<int?>("SELECT Id FROM Aliases WHERE CharacterId = @CharacterId AND Command = @Command",
            new { CharacterId = characterId, alias.Command });

        var error = ExchangeJson.Validate(alias) ?? (repeated ? $"The alias '{alias.Command}' appears more than once in the file." : null);
        if (error is not null)
        {
            run.Report(key, ImportItemKind.Alias, alias.Command, existingId is not null, error);
            return;
        }

        await run.ItemAsync(key, ImportItemKind.Alias, alias.Command, existingId is not null, async () =>
        {
            if (existingId is null) await InsertAliasAsync(db, characterId, alias);
            else await db.ExecuteAsync("UPDATE Aliases SET Action = @Action, Enabled = @Enabled WHERE Id = @Id", new { Id = existingId, alias.Action, alias.Enabled });
        });
    }

    private static async Task WalkTriggerAsync(ImportRun run, int characterId, TriggerDto trigger, HashSet<string> replacedNames)
    {
        var db = run.Db;
        var key = run.UniqueKey($"trigger:{trigger.Name}");
        // Two triggers of the file with the same name: the second one is not a conflict with the first.
        var existing = replacedNames.Contains(trigger.Name)
            ? []
            : (await db.QueryAsync<TriggerEntity>("SELECT * FROM Triggers WHERE CharacterId = @CharacterId AND Name = @Name COLLATE NOCASE ORDER BY SortOrder",
                new { CharacterId = characterId, trigger.Name })).ToList();

        if (ExchangeJson.Validate(trigger) is { } error)
        {
            run.Report(key, ImportItemKind.Trigger, trigger.Name, existing.Count > 0, error);
            return;
        }

        var outcome = await run.ItemAsync(key, ImportItemKind.Trigger, trigger.Name, existing.Count > 0, async () =>
        {
            int sortOrder;
            if (existing.Count > 0)
            {
                // The replacement keeps the position (the N of "-trigger N") of the one it replaces.
                sortOrder = existing[0].SortOrder;
                await db.ExecuteAsync("DELETE FROM Triggers WHERE CharacterId = @CharacterId AND Name = @Name COLLATE NOCASE",
                    new { CharacterId = characterId, trigger.Name });
            }
            else
            {
                sortOrder = await db.ScalarAsync<int>("SELECT COALESCE(MAX(SortOrder), 0) + 1 FROM Triggers WHERE CharacterId = @CharacterId",
                    new { CharacterId = characterId });
            }
            await InsertTriggerAsync(db, characterId, trigger, sortOrder);
        });

        if (outcome is ImportOutcome.Added or ImportOutcome.Overwritten) replacedNames.Add(trigger.Name);
    }

    private static async Task WalkPathAsync(ImportRun run, int characterId, PathDto path)
    {
        var db = run.Db;
        var baseKey = $"path:{path.Name}";
        var repeated = run.IsKeyUsed(baseKey);
        var key = run.UniqueKey(baseKey);
        var existingId = await db.ScalarAsync<int?>("SELECT Id FROM Paths WHERE CharacterId = @CharacterId AND Name = @Name",
            new { CharacterId = characterId, path.Name });

        var error = ExchangeJson.Validate(path) ?? (repeated ? $"The path '{path.Name}' appears more than once in the file." : null);
        if (error is not null)
        {
            run.Report(key, ImportItemKind.Path, path.Name, existingId is not null, error);
            return;
        }

        await run.ItemAsync(key, ImportItemKind.Path, path.Name, existingId is not null, async () =>
        {
            if (existingId is null) await InsertPathAsync(db, characterId, path);
            else await db.ExecuteAsync("UPDATE Paths SET Path = @Path WHERE Id = @Id", new { Id = existingId, path.Path });
        });
    }

    private static async Task WalkMovementsAsync(ImportRun run, int? mudId, int? characterId, List<MovementDto> movements)
    {
        var db = run.Db;
        var key = run.UniqueKey("movements");
        var exists = await db.ScalarAsync<long>(
            "SELECT EXISTS (SELECT 1 FROM Movements WHERE (@MudId IS NOT NULL AND MudId = @MudId) OR (@CharacterId IS NOT NULL AND CharacterId = @CharacterId))",
            new { MudId = mudId, CharacterId = characterId }) != 0;

        if (ExchangeJson.Validate(movements) is { } error)
        {
            run.Report(key, ImportItemKind.Movements, "movements", exists, error);
            return;
        }

        await run.ItemAsync(key, ImportItemKind.Movements, "movements", exists, () => WriteMovementsAsync(db, mudId, characterId, movements));
    }

    private static async Task WalkOptionsAsync(ImportRun run, OptionScope scope, int? scopeId, Dictionary<string, string> options)
    {
        var key = run.UniqueKey("options");
        var exists = await ReadOwnOptionsAsync(run.Db, scope, scopeId) is not null;
        await run.ItemAsync(key, ImportItemKind.Options, "options", exists, () => WriteOptionsAsync(run, scope, scopeId, options));
    }

    // ═════════════════════════════ Reads and writes ═════════════════════════════

    private static async Task<CharacterEntity> GetCharacterAsync(Db db, int characterId) =>
        await db.SingleOrDefaultAsync<CharacterEntity>("SELECT * FROM Characters WHERE Id = @Id", new { Id = characterId })
        ?? throw new InvalidOperationException($"Character {characterId} does not exist.");

    /// <summary>Note what is NOT read: EncryptedPassword never leaves the database.</summary>
    private static async Task<CharacterDto> ReadCharacterAsync(Db db, CharacterEntity character, string? mudName) => new()
    {
        Name = character.Name,
        MudName = mudName,
        MovementMode = character.MovementMode,
        Aliases = await ReadAliasesAsync(db, character.Id),
        Triggers = await ReadTriggersAsync(db, character.Id),
        Paths = await ReadPathsAsync(db, character.Id),
        Movements = await ReadMovementsAsync(db, null, character.Id),
        Options = await ReadOwnOptionsAsync(db, OptionScope.Character, character.Id)
    };

    private static async Task<List<AliasDto>> ReadAliasesAsync(Db db, int characterId) =>
        (await db.QueryAsync<AliasEntity>("SELECT * FROM Aliases WHERE CharacterId = @Id ORDER BY Command", new { Id = characterId }))
        .Select(a => new AliasDto { Command = a.Command, Action = a.Action, Enabled = a.Enabled }).ToList();

    private static async Task<List<TriggerDto>> ReadTriggersAsync(Db db, int characterId) =>
        (await db.QueryAsync<TriggerEntity>("SELECT * FROM Triggers WHERE CharacterId = @Id ORDER BY SortOrder, CreatedAt, rowid", new { Id = characterId }))
        .Select(t => new TriggerDto
        {
            Name = t.Name,
            Pattern = t.Pattern,
            PatternType = ToEnum(t.PatternType, PatternType.Literal),
            Action = t.Action,
            ActionType = ToEnum(t.ActionType, TriggerActionType.SendCommand),
            Sound = t.Sound,
            Enabled = t.Enabled,
            CaseSensitive = t.CaseSensitive,
            Priority = t.Priority,
            Multiline = t.Multiline,
            GagLine = t.GagLine
        }).ToList();

    private static async Task<List<PathDto>> ReadPathsAsync(Db db, int characterId) =>
        (await db.QueryAsync<PathEntity>("SELECT * FROM Paths WHERE CharacterId = @Id ORDER BY Name", new { Id = characterId }))
        .Select(p => new PathDto { Name = p.Name, Path = p.Path }).ToList();

    private static async Task<List<MovementDto>> ReadMovementsAsync(Db db, int? mudId, int? characterId) =>
        (await db.QueryAsync<MovementEntity>(
            "SELECT * FROM Movements WHERE (@MudId IS NOT NULL AND MudId = @MudId) OR (@CharacterId IS NOT NULL AND CharacterId = @CharacterId) ORDER BY KeyCode",
            new { MudId = mudId, CharacterId = characterId }))
        .Select(m => new MovementDto { Key = m.KeyCode, Command = m.Command }).ToList();

    private static async Task<List<MessageRuleDto>> ReadRulesAsync(Db db, int ruleSetId) =>
        (await db.QueryAsync<MessageRuleEntity>("SELECT * FROM MessageRules WHERE RuleSetId = @Id ORDER BY SortOrder, Id", new { Id = ruleSetId }))
        .Select(r => new MessageRuleDto { Pattern = r.Pattern, Template = r.Template, CaseSensitive = r.CaseSensitive, Channel = r.Channel, Enabled = r.Enabled })
        .ToList();

    /// <summary>Null when the scope has no rows (it inherits). Values go through the serializer so legacy keys travel under their current name.</summary>
    private static async Task<Dictionary<string, string>?> ReadOwnOptionsAsync(Db db, OptionScope scope, int? scopeId)
    {
        var rows = await db.QueryAsync<OptionEntity>(
            "SELECT * FROM Options WHERE Scope = @Scope AND COALESCE(ScopeId, 0) = COALESCE(@ScopeId, 0)",
            new { Scope = (int)scope, ScopeId = scope == OptionScope.Global ? null : scopeId });
        // Secrets (the protected proxy password) never reach a file: they only make sense with this installation's key.
        return rows.Count == 0
            ? null
            : OptionsSerializer.WithoutSecrets(OptionsSerializer.Serialize(
                OptionsSerializer.Deserialize(rows.Select(r => new KeyValuePair<string, string>(r.Key, r.Value)))));
    }

    private static Task InsertAliasAsync(Db db, int characterId, AliasDto alias) =>
        db.ExecuteAsync("INSERT INTO Aliases (CharacterId, Command, Action, Enabled) VALUES (@CharacterId, @Command, @Action, @Enabled)",
            new { CharacterId = characterId, alias.Command, alias.Action, alias.Enabled });

    private static Task InsertPathAsync(Db db, int characterId, PathDto path) =>
        db.ExecuteAsync("INSERT INTO Paths (CharacterId, Name, Path) VALUES (@CharacterId, @Name, @Path)",
            new { CharacterId = characterId, path.Name, path.Path });

    /// <summary>Always a new identifier: the same file can be imported into several characters.</summary>
    private static Task InsertTriggerAsync(Db db, int characterId, TriggerDto trigger, int sortOrder) =>
        db.ExecuteAsync(
            """
            INSERT INTO Triggers (Id, CharacterId, Name, Pattern, PatternType, Action, ActionType, Sound, Enabled,
                                  CaseSensitive, Priority, Multiline, GagLine, SortOrder, CreatedAt, UpdatedAt)
            VALUES (@Id, @CharacterId, @Name, @Pattern, @PatternType, @Action, @ActionType, @Sound, @Enabled,
                    @CaseSensitive, @Priority, @Multiline, @GagLine, @SortOrder, datetime('now'), datetime('now'))
            """,
            new
            {
                Id = Guid.NewGuid().ToString(), CharacterId = characterId, trigger.Name, trigger.Pattern,
                PatternType = (int)trigger.PatternType, trigger.Action, ActionType = (int)trigger.ActionType, trigger.Sound,
                trigger.Enabled, trigger.CaseSensitive, trigger.Priority, trigger.Multiline, trigger.GagLine, SortOrder = sortOrder
            });

    private static async Task WriteMovementsAsync(Db db, int? mudId, int? characterId, IReadOnlyList<MovementDto> movements)
    {
        await db.ExecuteAsync(
            "DELETE FROM Movements WHERE (@MudId IS NOT NULL AND MudId = @MudId) OR (@CharacterId IS NOT NULL AND CharacterId = @CharacterId)",
            new { MudId = mudId, CharacterId = characterId });
        foreach (var movement in movements)
        {
            await db.ExecuteAsync("INSERT INTO Movements (MudId, CharacterId, KeyCode, Command) VALUES (@MudId, @CharacterId, @KeyCode, @Command)",
                new { MudId = mudId, CharacterId = characterId, KeyCode = movement.Key, movement.Command });
        }
    }

    /// <summary>Null options = the scope inherits, so its rows are removed. Unknown keys and corrupt values do not survive the serializer.</summary>
    private static async Task WriteOptionsAsync(ImportRun run, OptionScope scope, int? scopeId, Dictionary<string, string>? options)
    {
        var db = run.Db;
        var storedId = scope == OptionScope.Global ? null : scopeId;

        // What the file cannot bring (it is never exported, and a hand-made value would belong to another key)
        // is kept as the scope had it.
        Dictionary<string, string> kept = options is null
            ? []
            : (await db.QueryAsync<OptionEntity>(
                "SELECT * FROM Options WHERE Scope = @Scope AND COALESCE(ScopeId, 0) = COALESCE(@ScopeId, 0)",
                new { Scope = (int)scope, ScopeId = storedId }))
              .Where(r => OptionsSerializer.SecretKeys.Contains(r.Key))
              .ToDictionary(r => r.Key, r => r.Value, StringComparer.OrdinalIgnoreCase);

        var deleted = await db.ExecuteAsync("DELETE FROM Options WHERE Scope = @Scope AND COALESCE(ScopeId, 0) = COALESCE(@ScopeId, 0)",
            new { Scope = (int)scope, ScopeId = storedId });

        if (options is not null)
        {
            var incoming = OptionsSerializer.WithoutSecrets(options);
            foreach (var (key, value) in kept)
                incoming[key] = value;

            foreach (var (key, value) in OptionsSerializer.Serialize(OptionsSerializer.Deserialize(incoming)))
            {
                await db.ExecuteAsync("INSERT INTO Options (Scope, ScopeId, Key, Value) VALUES (@Scope, @ScopeId, @Key, @Value)",
                    new { Scope = (int)scope, ScopeId = storedId, Key = key, Value = value });
            }
        }

        if (options is not null || deleted > 0)
            run.ChangedOptionScopes.Add((scope, storedId));
    }

    private static TEnum ToEnum<TEnum>(int value, TEnum fallback) where TEnum : struct, Enum
    {
        var candidate = (TEnum)Enum.ToObject(typeof(TEnum), value);
        return Enum.IsDefined(candidate) ? candidate : fallback;
    }

    // ═════════════════════════════════ Plumbing ═════════════════════════════════

    /// <summary>Connection + optional transaction + token, so every statement of an import shares them.</summary>
    private sealed class Db(SqliteConnection connection, IDbTransaction? transaction, CancellationToken ct)
    {
        public Task<int> ExecuteAsync(string sql, object? args = null) =>
            connection.ExecuteAsync(new CommandDefinition(sql, args, transaction, cancellationToken: ct));

        public Task<T?> ScalarAsync<T>(string sql, object? args = null) =>
            connection.ExecuteScalarAsync<T>(new CommandDefinition(sql, args, transaction, cancellationToken: ct));

        public Task<T?> SingleOrDefaultAsync<T>(string sql, object? args = null) =>
            connection.QuerySingleOrDefaultAsync<T?>(new CommandDefinition(sql, args, transaction, cancellationToken: ct));

        public async Task<List<T>> QueryAsync<T>(string sql, object? args = null) =>
            (await connection.QueryAsync<T>(new CommandDefinition(sql, args, transaction, cancellationToken: ct))).ToList();
    }

    /// <summary>State of one walk: in analysis mode it only collects items; in apply mode it writes and collects results.</summary>
    private sealed class ImportRun(Db db, IReadOnlyDictionary<string, ImportDecision>? decisions, bool apply)
    {
        private readonly Dictionary<string, int> _keys = new(StringComparer.Ordinal);
        private int _savepoint;

        public Db Db { get; } = db;
        public bool Apply { get; } = apply;
        public List<ImportItem> Items { get; } = [];
        public List<ImportItemResult> Results { get; } = [];
        public List<(OptionScope Scope, int? ScopeId)> ChangedOptionScopes { get; } = [];

        public bool IsKeyUsed(string baseKey) => _keys.ContainsKey(baseKey);

        /// <summary>Deterministic, so analysis and apply produce the same keys for the same document.</summary>
        public string UniqueKey(string baseKey)
        {
            var count = _keys.GetValueOrDefault(baseKey) + 1;
            _keys[baseKey] = count;
            return count == 1 ? baseKey : $"{baseKey}#{count}";
        }

        /// <summary>No decision: new elements are added and conflicts skipped.</summary>
        public bool WouldWrite(string key, bool exists) =>
            decisions is not null && decisions.TryGetValue(key, out var decision)
                ? decision == ImportDecision.Overwrite
                : !exists;

        /// <summary>An element that cannot be imported.</summary>
        public void Report(string key, ImportItemKind kind, string name, bool exists, string error, string? parentKey = null)
        {
            if (Apply) Results.Add(new ImportItemResult(key, kind, name, ImportOutcome.Failed, error));
            else Items.Add(new ImportItem(key, kind, name, ImportItemStatus.Invalid, parentKey, error));
        }

        /// <summary>Analysis: records the item. Apply: decides, and writes inside a savepoint so a failing
        /// element is undone on its own without losing the rest of the import.</summary>
        public async Task<ImportOutcome?> ItemAsync(string key, ImportItemKind kind, string name, bool exists, Func<Task> write, string? parentKey = null)
        {
            if (!Apply)
            {
                Items.Add(new ImportItem(key, kind, name, exists ? ImportItemStatus.Conflict : ImportItemStatus.New, parentKey));
                return null;
            }

            if (!WouldWrite(key, exists))
            {
                Results.Add(new ImportItemResult(key, kind, name, ImportOutcome.Skipped));
                return ImportOutcome.Skipped;
            }

            var savepoint = $"import_item_{++_savepoint}";
            await Db.ExecuteAsync($"SAVEPOINT {savepoint}");
            try
            {
                await write();
                await Db.ExecuteAsync($"RELEASE {savepoint}");
            }
            catch (Exception ex) when (ex is SqliteException or InvalidOperationException or ArgumentException)
            {
                await Db.ExecuteAsync($"ROLLBACK TO {savepoint}; RELEASE {savepoint}");
                Results.Add(new ImportItemResult(key, kind, name, ImportOutcome.Failed, ex.Message));
                return ImportOutcome.Failed;
            }

            var outcome = exists ? ImportOutcome.Overwritten : ImportOutcome.Added;
            Results.Add(new ImportItemResult(key, kind, name, outcome));
            return outcome;
        }
    }

    private sealed class MessageRuleDtoComparer : IEqualityComparer<MessageRuleDto>
    {
        public static MessageRuleDtoComparer Instance { get; } = new();

        public bool Equals(MessageRuleDto? x, MessageRuleDto? y) =>
            ReferenceEquals(x, y) || (x is not null && y is not null
                && x.Pattern == y.Pattern && x.Template == y.Template && x.CaseSensitive == y.CaseSensitive
                && (x.Channel ?? string.Empty) == (y.Channel ?? string.Empty) && x.Enabled == y.Enabled);

        public int GetHashCode(MessageRuleDto obj) => HashCode.Combine(obj.Pattern, obj.Template, obj.CaseSensitive, obj.Enabled);
    }
}
