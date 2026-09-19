using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Dapper;
using Omnimud.Core.Options;
using Omnimud.Data.Entities;
using Omnimud.Data.Exchange;
using Omnimud.Data.Options;
using Omnimud.Data.Repositories;

namespace Omnimud.Data.Tests.Exchange;

public sealed class ExchangeServiceTests : IAsyncLifetime
{
    private static readonly byte[] Password = Encoding.UTF8.GetBytes("S3cr3t0-Muy-Privado");

    private readonly World _source = new();
    private readonly World _target = new();

    public async Task InitializeAsync()
    {
        await _source.InitializeAsync();
        await _target.InitializeAsync();
        await _source.SeedAsync();
    }

    public async Task DisposeAsync()
    {
        await _source.Db.DisposeAsync();
        await _target.Db.DisposeAsync();
    }

    // ═════════════════════════════ MUD ═════════════════════════════

    [Fact]
    public async Task Mud_WithCharacters_ExportImportIntoEmptyDatabase_ReproducesEverything()
    {
        var json = await _source.Exchange.ExportMudAsync(_source.MudId, includeCharacters: true);

        var analysis = await _target.Exchange.AnalyzeAsync(json, ImportTarget.None);
        analysis.Kind.Should().Be(ExchangeKind.Mud);
        analysis.HasConflicts.Should().BeFalse();
        analysis.Items.Select(i => (i.Key, i.Kind, i.Status, i.ParentKey)).Should().BeEquivalentTo(new[]
        {
            ("ruleset:MisReglas", ImportItemKind.MessageRuleSet, ImportItemStatus.New, (string?)null),
            ("mud:Reinos", ImportItemKind.Mud, ImportItemStatus.New, null),
            ("character:Ana", ImportItemKind.Character, ImportItemStatus.New, "mud:Reinos"),
            ("character:Berto", ImportItemKind.Character, ImportItemStatus.New, "mud:Reinos")
        });

        var summary = await _target.Exchange.ApplyAsync(analysis);
        summary.Added.Should().Be(4);
        (summary.Overwritten, summary.Skipped, summary.Failed).Should().Be((0, 0, 0));

        // Exporting what was imported must give the same document.
        var mud = (await _target.Muds.GetByNameAsync("Reinos"))!;
        var again = await _target.Exchange.ExportMudAsync(mud.Id, includeCharacters: true);
        ExchangeJson.Parse(again).Should().BeEquivalentTo(ExchangeJson.Parse(json), o => o.Excluding(d => d.ExportedAt).WithStrictOrdering());

        // And a few direct checks, so the comparison above cannot pass by exporting nothing.
        mud.Should().BeEquivalentTo(new { Host = "rl.example.org", Port = 5001, UseTls = true, ValidateCertificate = false, Encoding = "iso-8859-1", MovementMode = true, LoginScript = "%n\n%p" });
        mud.MessageRuleSetId.Should().NotBeNull();
        (await _target.Directions.GetByMudAsync(mud.Id)).Should().HaveCount(2);
        (await _target.Movements.GetByMudAsync(mud.Id)).Should().ContainSingle().Which.Command.Should().Be("norte");
        (await _target.Options.ResolveAsync(mud.Id, null)).Volume.Should().Be(20);

        var characters = await _target.Characters.GetByMudAsync(mud.Id);
        characters.Select(c => c.Name).Should().Equal("Ana", "Berto");
        var ana = characters[0];
        ana.IsDefault.Should().BeTrue();
        mud.DefaultCharacterId.Should().Be(ana.Id);
        ana.MovementMode.Should().BeTrue();
        ana.EncryptedPassword.Should().BeNull("passwords never travel");
        (await _target.Aliases.GetByCharacterAsync(ana.Id)).Should().HaveCount(2);
        (await _target.Triggers.GetByCharacterAsync(ana.Id)).Select(t => t.Name).Should().Equal("Primero", "Segundo", "Tercero");
        (await _target.Paths.GetByCharacterAsync(ana.Id)).Should().ContainSingle();
        (await _target.Movements.GetByCharacterAsync(ana.Id)).Should().ContainSingle().Which.KeyCode.Should().Be(5);
        (await _target.Options.ResolveAsync(mud.Id, ana.Id)).Volume.Should().Be(30);
    }

    [Fact]
    public async Task Mud_WithoutCharacters_CarriesNone()
    {
        var json = await _source.Exchange.ExportMudAsync(_source.MudId, includeCharacters: false);
        json.Should().NotContain("Ana").And.NotContain("\"characters\"").And.NotContain("defaultCharacter");

        var summary = await _target.Exchange.ApplyAsync(await _target.Exchange.AnalyzeAsync(json, ImportTarget.None));

        summary.Added.Should().Be(2, "the MUD and its rule set");
        var mud = (await _target.Muds.GetByNameAsync("Reinos"))!;
        (await _target.Characters.GetByMudAsync(mud.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Mud_SeededRuleSet_IsLinkedWithoutAsking()
    {
        var simauria = (await _source.Rules.GetRuleSetByNameAsync("Simauria"))!;
        await _source.Muds.SetMessageRuleSetAsync(_source.MudId, simauria.Id);
        var json = await _source.Exchange.ExportMudAsync(_source.MudId, includeCharacters: false);

        var analysis = await _target.Exchange.AnalyzeAsync(json, ImportTarget.None);
        analysis.Items.Should().ContainSingle().Which.Kind.Should().Be(ImportItemKind.Mud);
        await _target.Exchange.ApplyAsync(analysis);

        var targetSet = (await _target.Rules.GetRuleSetByNameAsync("Simauria"))!;
        (await _target.Muds.GetByNameAsync("Reinos"))!.MessageRuleSetId.Should().Be(targetSet.Id);
    }

    // ── Rule sets of type script ───────────────────────────────────────────

    private const string SampleScript = "for _, line in ipairs(om.lines) do\n  if line ~= '' then om.message(line) end\nend";

    private async Task<int> MakeSourceSetAScriptAsync(string script = SampleScript)
    {
        var set = (await _source.Rules.GetRuleSetByNameAsync("MisReglas"))!;
        set.Script = script;
        await _source.Rules.UpdateRuleSetAsync(set);
        return set.Id;
    }

    [Fact]
    public async Task Mud_ScriptRuleSet_TravelsWithItsScript_AndItsPatterns()
    {
        await MakeSourceSetAScriptAsync();
        var json = await _source.Exchange.ExportMudAsync(_source.MudId, includeCharacters: false);
        ExchangeJson.Parse(json).Mud!.MessageRuleSet!.Script.Should().Be(SampleScript);

        var summary = await _target.Exchange.ApplyAsync(await _target.Exchange.AnalyzeAsync(json, ImportTarget.None));
        summary.Failed.Should().Be(0);

        var imported = (await _target.Rules.GetRuleSetByNameAsync("MisReglas"))!;
        imported.Script.Should().Be(SampleScript);
        imported.IsScript.Should().BeTrue();
        imported.IsBuiltIn.Should().BeFalse();
        (await _target.Rules.GetRulesAsync(imported.Id)).Should().HaveCount(2);

        var again = await _target.Exchange.ExportMudAsync((await _target.Muds.GetByNameAsync("Reinos"))!.Id, includeCharacters: false);
        ExchangeJson.Parse(again).Should().BeEquivalentTo(ExchangeJson.Parse(json), o => o.Excluding(d => d.ExportedAt).WithStrictOrdering());
    }

    [Fact]
    public async Task Mud_PatternRuleSet_ExportsNoScript_AndOldFilesWithoutTheFieldStillImport()
    {
        var json = await _source.Exchange.ExportMudAsync(_source.MudId, includeCharacters: false);
        json.Should().NotContain("om.message");
        ExchangeJson.Parse(json).Mud!.MessageRuleSet!.Script.Should().BeNull();

        await _target.Exchange.ApplyAsync(await _target.Exchange.AnalyzeAsync(json, ImportTarget.None));
        var imported = (await _target.Rules.GetRuleSetByNameAsync("MisReglas"))!;
        imported.Script.Should().BeNull();
        imported.IsScript.Should().BeFalse();
    }

    [Fact]
    public async Task Mud_ScriptRuleSet_SameScript_IsLinkedWithoutAsking_DifferentScript_IsAConflict()
    {
        await MakeSourceSetAScriptAsync();
        var json = await _source.Exchange.ExportMudAsync(_source.MudId, includeCharacters: false);
        await _target.Exchange.ApplyAsync(await _target.Exchange.AnalyzeAsync(json, ImportTarget.None));
        await _target.Muds.DeleteAsync((await _target.Muds.GetByNameAsync("Reinos"))!.Id);

        // Same script (even with Windows line ends): nothing to ask about the set.
        var set = (await _target.Rules.GetRuleSetByNameAsync("MisReglas"))!;
        set.Script = SampleScript.Replace("\n", "\r\n");
        await _target.Rules.UpdateRuleSetAsync(set);
        var same = await _target.Exchange.AnalyzeAsync(json, ImportTarget.None);
        same.Items.Select(i => i.Kind).Should().Equal(ImportItemKind.Mud);

        // A different script: a conflict; overwriting replaces it, skipping keeps it.
        set.Script = "om.message('otra cosa')";
        await _target.Rules.UpdateRuleSetAsync(set);
        var different = await _target.Exchange.AnalyzeAsync(json, ImportTarget.None);
        different.Conflicts.Select(i => i.Key).Should().Equal("ruleset:MisReglas");

        await _target.Exchange.ApplyAsync(different);
        (await _target.Rules.GetRuleSetByNameAsync("MisReglas"))!.Script.Should().Be("om.message('otra cosa')");

        await _target.Muds.DeleteAsync((await _target.Muds.GetByNameAsync("Reinos"))!.Id);
        await _target.Exchange.ApplyAsync(await _target.Exchange.AnalyzeAsync(json, ImportTarget.None), Decide("ruleset:MisReglas", ImportDecision.Overwrite));
        (await _target.Rules.GetRuleSetByNameAsync("MisReglas"))!.Script.Should().Be(SampleScript);
    }

    [Fact]
    public async Task Mud_ScriptSetInTheFile_OverAPatternSetInTheDatabase_AndTheOtherWayRound()
    {
        // Database: pattern set with the same rules. File: the same set, now a script.
        var plain = await _source.Exchange.ExportMudAsync(_source.MudId, includeCharacters: false);
        await _target.Exchange.ApplyAsync(await _target.Exchange.AnalyzeAsync(plain, ImportTarget.None));
        await _target.Muds.DeleteAsync((await _target.Muds.GetByNameAsync("Reinos"))!.Id);

        await MakeSourceSetAScriptAsync();
        var scripted = await _source.Exchange.ExportMudAsync(_source.MudId, includeCharacters: false);
        var analysis = await _target.Exchange.AnalyzeAsync(scripted, ImportTarget.None);
        analysis.Conflicts.Select(i => i.Key).Should().Equal("ruleset:MisReglas");
        await _target.Exchange.ApplyAsync(analysis, Decide("ruleset:MisReglas", ImportDecision.Overwrite));
        (await _target.Rules.GetRuleSetByNameAsync("MisReglas"))!.Script.Should().Be(SampleScript);

        // And back: a file without script over a user's script set is a conflict too; overwriting clears the script.
        await _target.Muds.DeleteAsync((await _target.Muds.GetByNameAsync("Reinos"))!.Id);
        var back = await _target.Exchange.AnalyzeAsync(plain, ImportTarget.None);
        back.Conflicts.Select(i => i.Key).Should().Equal("ruleset:MisReglas");
        await _target.Exchange.ApplyAsync(back, Decide("ruleset:MisReglas", ImportDecision.Overwrite));
        (await _target.Rules.GetRuleSetByNameAsync("MisReglas"))!.Script.Should().BeNull();
    }

    [Fact]
    public async Task Mud_BuiltInSet_FromAFileWrittenBeforeScriptsExisted_IsStillLinkedWithoutAsking()
    {
        var callandor = (await _source.Rules.GetRuleSetByNameAsync("Callandor"))!;
        callandor.IsScript.Should().BeTrue();
        await _source.Muds.SetMessageRuleSetAsync(_source.MudId, callandor.Id);
        var document = ExchangeJson.Parse(await _source.Exchange.ExportMudAsync(_source.MudId, includeCharacters: false));
        document.Mud!.MessageRuleSet!.Script.Should().Be(callandor.Script);
        document.Mud.MessageRuleSet.Script = null; // as version 5 wrote it
        var oldJson = ExchangeJson.Serialize(document);

        var analysis = await _target.Exchange.AnalyzeAsync(oldJson, ImportTarget.None);
        analysis.Items.Should().ContainSingle().Which.Kind.Should().Be(ImportItemKind.Mud);
        await _target.Exchange.ApplyAsync(analysis);

        var targetSet = (await _target.Rules.GetRuleSetByNameAsync("Callandor"))!;
        targetSet.Script.Should().Be(callandor.Script, "the built-in script is not lost");
        (await _target.Muds.GetByNameAsync("Reinos"))!.MessageRuleSetId.Should().Be(targetSet.Id);
    }

    [Fact]
    public async Task Mud_Conflict_Overwrite_ReplacesMudData_AndDecidesEachCharacter()
    {
        var existing = await SeedConflictingMudAsync();
        var json = await _source.Exchange.ExportMudAsync(_source.MudId, includeCharacters: true);

        var analysis = await _target.Exchange.AnalyzeAsync(json, ImportTarget.None);
        analysis.Conflicts.Select(i => i.Key).Should().BeEquivalentTo("mud:Reinos", "ruleset:MisReglas", "character:Ana");
        analysis.Items.Single(i => i.Key == "character:Berto").Status.Should().Be(ImportItemStatus.New);

        var summary = await _target.Exchange.ApplyAsync(analysis, new Dictionary<string, ImportDecision>
        {
            ["mud:Reinos"] = ImportDecision.Overwrite,
            ["ruleset:MisReglas"] = ImportDecision.Overwrite,
            ["character:Ana"] = ImportDecision.Overwrite
        });

        (summary.Added, summary.Overwritten, summary.Skipped, summary.Failed).Should().Be((1, 3, 0, 0));

        var mud = (await _target.Muds.GetByIdAsync(existing.MudId))!;
        mud.Name.Should().Be("Reinos");
        mud.Host.Should().Be("rl.example.org");
        (await _target.Directions.GetByMudAsync(mud.Id)).Select(d => d.Direction).Should().Equal("norte", "sur");
        (await _target.Rules.GetRulesAsync(mud.MessageRuleSetId!.Value)).Select(r => r.Pattern).Should().Equal("^(\\w+) te dice: (.*)$", "^\\[(\\w+)\\]");

        var characters = await _target.Characters.GetByMudAsync(mud.Id);
        characters.Select(c => c.Name).Should().Equal("Ana", "Berto", "Zoe");

        var ana = characters[0];
        ana.Id.Should().Be(existing.AnaId, "overwriting keeps the row");
        ana.EncryptedPassword.Should().Equal(new byte[] { 1, 2, 3 }, "and therefore the password the user had stored");
        (await _target.Aliases.GetByCharacterAsync(ana.Id)).Select(a => a.Command).Should().Equal("h", "k");
        (await _target.Options.HasOwnOptionsAsync(OptionScope.Character, ana.Id)).Should().BeTrue();
        (await _target.Aliases.GetByCharacterAsync(characters[2].Id)).Should().ContainSingle("Zoe is not in the file and stays as she was");
    }

    [Fact]
    public async Task Mud_Conflict_Skip_LeavesTheMud_ButStillOffersItsCharacters()
    {
        var existing = await SeedConflictingMudAsync();
        var json = await _source.Exchange.ExportMudAsync(_source.MudId, includeCharacters: true);
        var analysis = await _target.Exchange.AnalyzeAsync(json, ImportTarget.None);

        // No decisions at all: conflicts are skipped, new elements added.
        var summary = await _target.Exchange.ApplyAsync(analysis);

        summary.Results.Select(r => (r.Key, r.Outcome)).Should().BeEquivalentTo(new[]
        {
            ("ruleset:MisReglas", ImportOutcome.Skipped),
            ("mud:Reinos", ImportOutcome.Skipped),
            ("character:Ana", ImportOutcome.Skipped),
            ("character:Berto", ImportOutcome.Added)
        });

        var mud = (await _target.Muds.GetByIdAsync(existing.MudId))!;
        mud.Host.Should().Be("viejo.example.org");
        (await _target.Directions.GetByMudAsync(mud.Id)).Select(d => d.Direction).Should().Equal("arriba");
        (await _target.Rules.GetRulesAsync(mud.MessageRuleSetId!.Value)).Should().ContainSingle().Which.Pattern.Should().Be("vieja");
        (await _target.Aliases.GetByCharacterAsync(existing.AnaId)).Select(a => a.Command).Should().Equal("viejo");
        (await _target.Characters.GetByMudAsync(mud.Id)).Select(c => c.Name).Should().Equal("ana", "Berto", "Zoe");
    }

    [Fact]
    public async Task Mud_NewButExplicitlySkipped_SkipsItsCharactersAndRuleSet()
    {
        var json = await _source.Exchange.ExportMudAsync(_source.MudId, includeCharacters: true);
        var analysis = await _target.Exchange.AnalyzeAsync(json, ImportTarget.None);

        var summary = await _target.Exchange.ApplyAsync(analysis, new Dictionary<string, ImportDecision> { ["mud:Reinos"] = ImportDecision.Skip });

        summary.Skipped.Should().Be(4);
        (await _target.Muds.GetAllAsync()).Should().BeEmpty();
        (await _target.Rules.GetRuleSetByNameAsync("MisReglas")).Should().BeNull();
    }

    [Fact]
    public async Task Mud_FailingElement_IsRolledBackOnItsOwn_AndReported()
    {
        // Two MUDs whose names differ only in case: overwriting the first with the file's name collides with the second.
        var firstId = await _target.Muds.AddAsync(new MudEntity { Name = "REINOS", Host = "a", Port = 1 });
        await _target.Muds.AddAsync(new MudEntity { Name = "Reinos", Host = "b", Port = 2 });
        await _target.Directions.AddAsync(new DirectionEntity { MudId = firstId, Direction = "arriba", Abbreviation = "a" });
        var json = await _source.Exchange.ExportMudAsync(_source.MudId, includeCharacters: true);
        var analysis = await _target.Exchange.AnalyzeAsync(json, ImportTarget.None);

        var summary = await _target.Exchange.ApplyAsync(analysis, new Dictionary<string, ImportDecision> { ["mud:Reinos"] = ImportDecision.Overwrite });

        var failed = summary.Results.Single(r => r.Key == "mud:Reinos");
        failed.Outcome.Should().Be(ImportOutcome.Failed);
        failed.Error.Should().NotBeNullOrEmpty();
        summary.Results.Where(r => r.Kind == ImportItemKind.Character).Should().OnlyContain(r => r.Outcome == ImportOutcome.Failed);
        summary.Results.Single(r => r.Kind == ImportItemKind.MessageRuleSet).Outcome.Should().Be(ImportOutcome.Added, "it was written before the MUD failed");

        (await _target.Muds.GetByIdAsync(firstId))!.Host.Should().Be("a");
        (await _target.Directions.GetByMudAsync(firstId)).Select(d => d.Direction).Should().Equal(["arriba"], "the half-done overwrite was undone");
    }

    [Fact]
    public async Task Apply_UnexpectedError_RollsBackTheWholeImport()
    {
        var json = await _source.Exchange.ExportMudAsync(_source.MudId, includeCharacters: true);
        var analysis = await _target.Exchange.AnalyzeAsync(json, ImportTarget.None);

        var apply = () => _target.Exchange.ApplyAsync(analysis, new ExplodingDecisions("character:Berto"));

        await apply.Should().ThrowAsync<TimeoutException>();
        (await _target.Muds.GetAllAsync()).Should().BeEmpty("the MUD and Ana had already been written inside the transaction");
        (await _target.Rules.GetRuleSetByNameAsync("MisReglas")).Should().BeNull();
    }

    // ═════════════════════════════ Character ═════════════════════════════

    [Fact]
    public async Task Character_ExportImport_IntoAnotherMud()
    {
        var mudId = await _target.Muds.AddAsync(new MudEntity { Name = "Destino", Host = "h", Port = 1 });
        var json = await _source.Exchange.ExportCharacterAsync(_source.AnaId);

        var analysis = await _target.Exchange.AnalyzeAsync(json, ImportTarget.ForMud(mudId));
        analysis.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(new ImportItem("character:Ana", ImportItemKind.Character, "Ana", ImportItemStatus.New));
        var summary = await _target.Exchange.ApplyAsync(analysis);

        summary.Added.Should().Be(1);
        var ana = (await _target.Characters.GetByMudAsync(mudId)).Single();
        var again = await _target.Exchange.ExportCharacterAsync(ana.Id);
        ExchangeJson.Parse(again).Character.Should().BeEquivalentTo(ExchangeJson.Parse(json).Character,
            o => o.Excluding(c => c!.MudName).WithStrictOrdering());
        ExchangeJson.Parse(again).Character!.MudName.Should().Be("Destino");
        ExchangeJson.Parse(json).Character!.Triggers.Should().HaveCount(3);
    }

    [Fact]
    public async Task Character_Conflict_OverwriteAndSkip()
    {
        var existing = await SeedConflictingMudAsync();
        var json = await _source.Exchange.ExportCharacterAsync(_source.AnaId);

        var analysis = await _target.Exchange.AnalyzeAsync(json, ImportTarget.ForMud(existing.MudId));
        analysis.Items.Should().ContainSingle().Which.Status.Should().Be(ImportItemStatus.Conflict);

        (await _target.Exchange.ApplyAsync(analysis, Decide("character:Ana", ImportDecision.Skip))).Skipped.Should().Be(1);
        (await _target.Aliases.GetByCharacterAsync(existing.AnaId)).Select(a => a.Command).Should().Equal("viejo");

        (await _target.Exchange.ApplyAsync(analysis, Decide("character:Ana", ImportDecision.Overwrite))).Overwritten.Should().Be(1);
        (await _target.Aliases.GetByCharacterAsync(existing.AnaId)).Select(a => a.Command).Should().Equal("h", "k");
        (await _target.Triggers.GetByCharacterAsync(existing.AnaId)).Should().HaveCount(3, "the old trigger was replaced with the rest");
    }

    [Fact]
    public async Task Character_NeedsAMud()
    {
        var json = await _source.Exchange.ExportCharacterAsync(_source.AnaId);

        var analyze = () => _target.Exchange.AnalyzeAsync(json, ImportTarget.None);

        (await analyze.Should().ThrowAsync<ExchangeFormatException>()).Which.Error.Should().Be(ExchangeError.TargetRequired);
    }

    // ═════════════════════════════ Loose lists ═════════════════════════════

    [Fact]
    public async Task Aliases_IntoEmptyCharacter_ThenWithConflicts()
    {
        var characterId = await _target.AddCharacterAsync();
        var json = await _source.Exchange.ExportAliasesAsync(_source.AnaId);

        var first = await _target.Exchange.ApplyAsync(await _target.Exchange.AnalyzeAsync(json, ImportTarget.ForCharacter(characterId)));
        first.Added.Should().Be(2);

        var k = (await _target.Aliases.GetByCharacterAsync(characterId)).Single(a => a.Command == "k");
        k.Action = "cambiado";
        await _target.Aliases.UpdateAsync(k);
        var h = (await _target.Aliases.GetByCharacterAsync(characterId)).Single(a => a.Command == "h");
        h.Action = "cambiado también";
        await _target.Aliases.UpdateAsync(h);

        var analysis = await _target.Exchange.AnalyzeAsync(json, ImportTarget.ForCharacter(characterId));
        analysis.Conflicts.Select(c => c.Key).Should().BeEquivalentTo("alias:k", "alias:h");
        var second = await _target.Exchange.ApplyAsync(analysis, new Dictionary<string, ImportDecision>
        {
            ["alias:k"] = ImportDecision.Overwrite,
            ["alias:h"] = ImportDecision.Skip
        });

        (second.Overwritten, second.Skipped).Should().Be((1, 1));
        var aliases = await _target.Aliases.GetByCharacterAsync(characterId);
        aliases.Single(a => a.Command == "k").Should().BeEquivalentTo(new { Action = "matar $1", Enabled = true });
        aliases.Single(a => a.Command == "h").Action.Should().Be("cambiado también");
    }

    [Fact]
    public async Task Triggers_IntoEmptyCharacter_ThenWithConflicts_KeepPosition()
    {
        var characterId = await _target.AddCharacterAsync();
        var json = await _source.Exchange.ExportTriggersAsync(_source.AnaId);

        (await _target.Exchange.ApplyAsync(await _target.Exchange.AnalyzeAsync(json, ImportTarget.ForCharacter(characterId)))).Added.Should().Be(3);
        var imported = await _target.Triggers.GetByCharacterAsync(characterId);
        imported.Select(t => t.Name).Should().Equal("Primero", "Segundo", "Tercero");
        imported.Select(t => t.Id).Should().NotIntersectWith((await _source.Triggers.GetByCharacterAsync(_source.AnaId)).Select(t => t.Id), "imported triggers get new identifiers");
        imported[1].Should().BeEquivalentTo(new { PatternType = 1, ActionType = 3, Sound = "ding.wav", Action = "", Priority = 90, Multiline = true, GagLine = true, CaseSensitive = true, Enabled = false });

        imported[1].Action = "tocado";
        await _target.Triggers.UpdateAsync(imported[1]);

        var analysis = await _target.Exchange.AnalyzeAsync(json, ImportTarget.ForCharacter(characterId));
        analysis.Conflicts.Should().HaveCount(3);
        var summary = await _target.Exchange.ApplyAsync(analysis, Decide("trigger:Segundo", ImportDecision.Overwrite));

        (summary.Overwritten, summary.Skipped).Should().Be((1, 2));
        var after = await _target.Triggers.GetByCharacterAsync(characterId);
        after.Select(t => t.Name).Should().Equal(["Primero", "Segundo", "Tercero"], "the replaced trigger keeps its place");
        after[1].Action.Should().BeEmpty();
    }

    [Fact]
    public async Task Paths_IntoEmptyCharacter_ThenWithConflicts()
    {
        var characterId = await _target.AddCharacterAsync();
        await _target.Paths.AddAsync(new PathEntity { CharacterId = characterId, Name = "otro", Path = "s" });
        var json = await _source.Exchange.ExportPathsAsync(_source.AnaId);

        (await _target.Exchange.ApplyAsync(await _target.Exchange.AnalyzeAsync(json, ImportTarget.ForCharacter(characterId)))).Added.Should().Be(1);

        var plaza = (await _target.Paths.GetByCharacterAsync(characterId)).Single(p => p.Name == "plaza");
        plaza.Path = "9s";
        await _target.Paths.UpdateAsync(plaza);
        var analysis = await _target.Exchange.AnalyzeAsync(json, ImportTarget.ForCharacter(characterId));
        analysis.Conflicts.Should().ContainSingle();

        (await _target.Exchange.ApplyAsync(analysis)).Skipped.Should().Be(1);
        (await _target.Paths.GetByCharacterAsync(characterId)).Single(p => p.Name == "plaza").Path.Should().Be("9s");

        (await _target.Exchange.ApplyAsync(analysis, Decide("path:plaza", ImportDecision.Overwrite))).Overwritten.Should().Be(1);
        (await _target.Paths.GetByCharacterAsync(characterId)).Select(p => $"{p.Name}={p.Path}").Should().Equal("otro=s", "plaza=3n2e");
    }

    [Fact]
    public async Task Movements_OfAMud_IntoACharacter_AndBack()
    {
        var characterId = await _target.AddCharacterAsync();
        var json = await _source.Exchange.ExportMovementsAsync(_source.MudId, null);

        var analysis = await _target.Exchange.AnalyzeAsync(json, ImportTarget.ForCharacter(characterId));
        analysis.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Key = "movements", Kind = ImportItemKind.Movements, Status = ImportItemStatus.New });
        (await _target.Exchange.ApplyAsync(analysis)).Added.Should().Be(1);
        (await _target.Movements.GetByCharacterAsync(characterId)).Select(m => $"{m.KeyCode}={m.Command}").Should().Equal("8=norte");

        await _target.Movements.SetAsync(null, characterId, 1, "propio");
        var conflict = await _target.Exchange.AnalyzeAsync(json, ImportTarget.ForCharacter(characterId));
        conflict.HasConflicts.Should().BeTrue();
        (await _target.Exchange.ApplyAsync(conflict)).Skipped.Should().Be(1);
        (await _target.Movements.GetByCharacterAsync(characterId)).Should().HaveCount(2);
        (await _target.Exchange.ApplyAsync(conflict, Decide("movements", ImportDecision.Overwrite))).Overwritten.Should().Be(1);
        (await _target.Movements.GetByCharacterAsync(characterId)).Select(m => m.KeyCode).Should().Equal([8], "overwriting replaces the whole set");

        // With only a MUD as destination they become the MUD's.
        var mudId = (await _target.Characters.GetByIdAsync(characterId))!.MudId;
        await _target.Exchange.ApplyAsync(await _target.Exchange.AnalyzeAsync(json, ImportTarget.ForMud(mudId)));
        (await _target.Movements.GetByMudAsync(mudId)).Should().ContainSingle();
    }

    [Fact]
    public async Task Options_ExportImport_ForEveryScope_AndRaisesChanged()
    {
        var characterId = await _target.AddCharacterAsync();
        var mudId = (await _target.Characters.GetByIdAsync(characterId))!.MudId;
        var changes = new List<(OptionScope, int?)>();
        _target.Options.Changed += (_, e) => changes.Add((e.Scope, e.ScopeId));
        var json = await _source.Exchange.ExportOptionsAsync(OptionScope.Character, _source.AnaId);

        // Global, empty: added.
        (await _target.Exchange.ApplyAsync(await _target.Exchange.AnalyzeAsync(json, ImportTarget.None))).Added.Should().Be(1);
        (await _target.Options.ResolveAsync(null, null)).Should().Be(await _source.Options.ResolveAsync(_source.MudId, _source.AnaId));

        // Character with own options: conflict, skip then overwrite.
        await _target.Options.SaveAsync(OptionScope.Character, characterId, new OmnimudOptions { Volume = 1 });
        var analysis = await _target.Exchange.AnalyzeAsync(json, ImportTarget.ForCharacter(characterId));
        analysis.Items.Should().ContainSingle().Which.Status.Should().Be(ImportItemStatus.Conflict);
        (await _target.Exchange.ApplyAsync(analysis)).Skipped.Should().Be(1);
        (await _target.Options.ResolveAsync(mudId, characterId)).Volume.Should().Be(1);
        (await _target.Exchange.ApplyAsync(analysis, Decide("options", ImportDecision.Overwrite))).Overwritten.Should().Be(1);
        (await _target.Options.ResolveAsync(mudId, characterId)).Volume.Should().Be(30);

        // MUD.
        (await _target.Exchange.ApplyAsync(await _target.Exchange.AnalyzeAsync(json, ImportTarget.ForMud(mudId)))).Added.Should().Be(1);
        (await _target.Options.HasOwnOptionsAsync(OptionScope.Mud, mudId)).Should().BeTrue();

        changes.Should().Equal((OptionScope.Global, null), (OptionScope.Character, characterId), (OptionScope.Character, characterId), (OptionScope.Mud, mudId));
    }

    [Fact]
    public async Task Options_OfAnInheritingScope_ExportWhatItResolvesTo()
    {
        var json = await _source.Exchange.ExportOptionsAsync(OptionScope.Character, _source.BertoId);

        ExchangeJson.Parse(json).Options.Should().BeEquivalentTo(OptionsSerializer.SerializeForExport(new OmnimudOptions { Volume = 20, LogType = LogMode.PerSession }),
            "Berto inherits the MUD's block");
    }

    [Fact]
    public async Task Options_TheProxyPassword_IsNeverExported_NotEvenProtected_ButTheUserNameIs()
    {
        const string secret = "UFJPVEVHSURPLWNvbi1sYS1jbGF2ZS1kZS1lc3RhLWluc3RhbGFjaW9u";
        var withProxy = new OmnimudOptions { Volume = 20, ProxyType = ProxyMode.Manual, ProxyHost = "proxy.corp", ProxyPort = 3128, ProxyUsername = "juan", ProxyPasswordProtected = secret };
        await _source.Options.SaveAsync(OptionScope.Global, null, withProxy);
        await _source.Options.SaveAsync(OptionScope.Mud, _source.MudId, withProxy);
        await _source.Options.SaveAsync(OptionScope.Character, _source.AnaId, withProxy);

        var files = new[]
        {
            await _source.Exchange.ExportOptionsAsync(OptionScope.Global, null),
            await _source.Exchange.ExportOptionsAsync(OptionScope.Mud, _source.MudId),
            await _source.Exchange.ExportOptionsAsync(OptionScope.Character, _source.AnaId),
            await _source.Exchange.ExportOptionsAsync(OptionScope.Character, _source.BertoId),
            await _source.Exchange.ExportMudAsync(_source.MudId, includeCharacters: true),
            await _source.Exchange.ExportCharacterAsync(_source.AnaId)
        };

        foreach (var json in files)
        {
            json.Should().NotContain(secret).And.NotContain(nameof(OmnimudOptions.ProxyPasswordProtected));
            json.Should().Contain("proxy.corp").And.Contain("juan");
        }
        (await _source.Options.ResolveAsync(null, null)).ProxyPasswordProtected.Should().Be(secret, "exporting does not touch what is stored");
    }

    [Fact]
    public async Task Options_Import_KeepsTheProxyPasswordTheScopeAlreadyHad_AndIgnoresOneSmuggledInTheFile()
    {
        const string mine = "TUlBLXByb3RlZ2lkYS1jb24tbWktY2xhdmU=";
        await _target.Options.SaveAsync(OptionScope.Global, null, new OmnimudOptions { Volume = 1, ProxyUsername = "yo", ProxyPasswordProtected = mine });
        const string json = """
            { "format": "omnimud", "formatVersion": 1, "kind": "options",
              "options": { "Volume": "55", "ProxyUsername": "otro", "ProxyPasswordProtected": "QUpFTkEtZGUtb3RyYS1pbnN0YWxhY2lvbg==" } }
            """;

        var analysis = await _target.Exchange.AnalyzeAsync(json, ImportTarget.None);
        await _target.Exchange.ApplyAsync(analysis, Decide("options", ImportDecision.Overwrite));

        (await _target.Options.ResolveAsync(null, null))
            .Should().Be(new OmnimudOptions { Volume = 55, ProxyUsername = "otro", ProxyPasswordProtected = mine });
    }

    [Fact]
    public async Task Options_Import_IntoAScopeWithoutPassword_BringsNone()
    {
        const string json = """
            { "format": "omnimud", "formatVersion": 1, "kind": "options",
              "options": { "Volume": "55", "ProxyPasswordProtected": "QUpFTkEtZGUtb3RyYS1pbnN0YWxhY2lvbg==" } }
            """;

        await _target.Exchange.ApplyAsync(await _target.Exchange.AnalyzeAsync(json, ImportTarget.None));

        (await _target.Options.ResolveAsync(null, null)).Should().Be(new OmnimudOptions { Volume = 55 });
    }

    [Fact]
    public async Task Options_UnknownKeysAndCorruptValues_AreSanitizedOnImport()
    {
        const string json = """
            { "format": "omnimud", "formatVersion": 1, "kind": "options",
              "options": { "Volume": "55", "HistorySize": "muchos", "Inventada": "x", "SoundEnabled": "false" } }
            """;

        await _target.Exchange.ApplyAsync(await _target.Exchange.AnalyzeAsync(json, ImportTarget.None));

        (await _target.Options.ResolveAsync(null, null)).Should().Be(new OmnimudOptions { Volume = 55, EnableSounds = false });
        (await _target.OptionRows.GetByScope(0, null)).Select(r => r.Key).Should().BeEquivalentTo(OptionsSerializer.Keys);
    }

    // ═════════════════════════════ Copy between characters ═════════════════════════════

    [Fact]
    public async Task Copy_FromAnotherCharacter_UsesTheSameConflictMechanism()
    {
        await _source.Aliases.AddAsync(new AliasEntity { CharacterId = _source.BertoId, Command = "k", Action = "el de Berto" });
        await _source.Paths.AddAsync(new PathEntity { CharacterId = _source.BertoId, Name = "casa", Path = "2o" });

        var analysis = await _source.Exchange.AnalyzeCopyAsync(_source.AnaId, _source.BertoId, ExchangeParts.Aliases | ExchangeParts.Triggers | ExchangeParts.Paths);

        analysis.Kind.Should().Be(ExchangeKind.Lists);
        analysis.Items.Select(i => (i.Key, i.Status)).Should().BeEquivalentTo(new[]
        {
            ("alias:h", ImportItemStatus.New), ("alias:k", ImportItemStatus.Conflict),
            ("trigger:Primero", ImportItemStatus.New), ("trigger:Segundo", ImportItemStatus.New), ("trigger:Tercero", ImportItemStatus.New),
            ("path:plaza", ImportItemStatus.New)
        });

        var summary = await _source.Exchange.ApplyAsync(analysis, Decide("alias:k", ImportDecision.Skip));

        (summary.Added, summary.Skipped, summary.Overwritten, summary.Failed).Should().Be((5, 1, 0, 0));
        (await _source.Aliases.GetByCharacterAsync(_source.BertoId)).Single(a => a.Command == "k").Action.Should().Be("el de Berto");
        (await _source.Triggers.GetByCharacterAsync(_source.BertoId)).Select(t => t.Name).Should().Equal("Primero", "Segundo", "Tercero");
        (await _source.Paths.GetByCharacterAsync(_source.BertoId)).Select(p => p.Name).Should().Equal("casa", "plaza");
        (await _source.Movements.GetByCharacterAsync(_source.BertoId)).Should().BeEmpty("movements were not requested");
        (await _source.Aliases.GetByCharacterAsync(_source.AnaId)).Should().HaveCount(2, "the source is untouched");
    }

    [Fact]
    public async Task Copy_OnlyTheRequestedParts_AndNeverOntoItself()
    {
        var analysis = await _source.Exchange.AnalyzeCopyAsync(_source.AnaId, _source.BertoId, ExchangeParts.Movements);
        analysis.Items.Should().ContainSingle().Which.Kind.Should().Be(ImportItemKind.Movements);

        var same = () => _source.Exchange.AnalyzeCopyAsync(_source.AnaId, _source.AnaId, ExchangeParts.All);
        await same.Should().ThrowAsync<ArgumentException>();
    }

    // ═════════════════════════════ Security and validation ═════════════════════════════

    [Fact]
    public async Task Password_NeverAppearsInAnyExport()
    {
        var exports = new[]
        {
            await _source.Exchange.ExportMudAsync(_source.MudId, includeCharacters: true),
            await _source.Exchange.ExportCharacterAsync(_source.AnaId)
        };

        foreach (var json in exports)
        {
            json.Should().NotContainEquivalentOf("password");
            json.Should().NotContain("S3cr3t0");
            json.Should().NotContain(Convert.ToBase64String(Password));
            json.Should().NotContain(Convert.ToBase64String(Password).TrimEnd('='));
            json.Should().NotContainEquivalentOf(Convert.ToHexString(Password));
        }

        typeof(CharacterDto).GetProperties().Select(p => p.Name).Should().NotContain(n => n.Contains("Password", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("", ExchangeError.InvalidJson)]
    [InlineData("   ", ExchangeError.InvalidJson)]
    [InlineData("esto no es json", ExchangeError.InvalidJson)]
    [InlineData("{ \"format\": \"omnimud\", \"formatVersion\": 1, \"kind\": \"aliases\", \"aliases\": [ { \"command\": ", ExchangeError.InvalidJson)]
    [InlineData("{ \"format\": \"omnimud\", \"formatVersion\": 1, \"kind\": \"aliases\", \"aliases\": \"no es una lista\" }", ExchangeError.InvalidJson)]
    [InlineData("{ \"format\": \"omnimud\", \"formatVersion\": 1, \"kind\": \"triggers\", \"triggers\": [ { \"name\": \"t\", \"pattern\": \"p\", \"patternType\": \"Inventado\" } ] }", ExchangeError.InvalidJson)]
    [InlineData("{ \"format\": \"omnimud\", \"formatVersion\": 1, \"kind\": \"inventado\" }", ExchangeError.InvalidJson)]
    [InlineData("[1, 2, 3]", ExchangeError.NotAnOmnimudFile)]
    [InlineData("{ \"name\": \"otro programa\" }", ExchangeError.NotAnOmnimudFile)]
    [InlineData("{ \"format\": \"otro\", \"formatVersion\": 1 }", ExchangeError.NotAnOmnimudFile)]
    [InlineData("{ \"format\": \"omnimud\", \"formatVersion\": \"uno\" }", ExchangeError.NotAnOmnimudFile)]
    [InlineData("{ \"format\": \"omnimud\", \"formatVersion\": 0 }", ExchangeError.NotAnOmnimudFile)]
    [InlineData("{ \"format\": \"omnimud\", \"formatVersion\": 2, \"kind\": \"cosa del futuro\", \"nuevo\": {} }", ExchangeError.UnsupportedVersion)]
    [InlineData("{ \"format\": \"omnimud\", \"formatVersion\": 999 }", ExchangeError.UnsupportedVersion)]
    [InlineData("{ \"format\": \"omnimud\", \"formatVersion\": 1, \"kind\": \"mud\" }", ExchangeError.InvalidContent)]
    [InlineData("{ \"format\": \"omnimud\", \"formatVersion\": 1, \"kind\": \"character\", \"aliases\": [] }", ExchangeError.InvalidContent)]
    public async Task Analyze_RejectsBadFiles_WithAClearError(string json, ExchangeError expected)
    {
        var characterId = await _target.AddCharacterAsync();

        var analyze = () => _target.Exchange.AnalyzeAsync(json, ImportTarget.ForCharacter(characterId));

        var exception = (await analyze.Should().ThrowAsync<ExchangeFormatException>()).Which;
        exception.Error.Should().Be(expected);
        exception.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Analyze_RejectsOversizedDocuments_AndTooManyElements()
    {
        var characterId = await _target.AddCharacterAsync();
        var huge = "{ \"format\": \"omnimud\", \"formatVersion\": 1, \"kind\": \"aliases\", \"aliases\": [], \"pad\": \""
                   + new string('x', IExchangeService.MaxDocumentLength) + "\" }";
        var tooLarge = () => _target.Exchange.AnalyzeAsync(huge, ImportTarget.ForCharacter(characterId));
        (await tooLarge.Should().ThrowAsync<ExchangeFormatException>()).Which.Error.Should().Be(ExchangeError.TooLarge);

        var many = new StringBuilder("{ \"format\": \"omnimud\", \"formatVersion\": 1, \"kind\": \"aliases\", \"aliases\": [");
        for (var i = 0; i <= IExchangeService.MaxListItems; i++)
            many.Append(i == 0 ? "" : ",").Append("{\"command\":\"a").Append(i).Append("\",\"action\":\"x\"}");
        many.Append("] }");
        var tooMany = () => _target.Exchange.AnalyzeAsync(many.ToString(), ImportTarget.ForCharacter(characterId));
        (await tooMany.Should().ThrowAsync<ExchangeFormatException>()).Which.Error.Should().Be(ExchangeError.InvalidContent);
    }

    [Fact]
    public async Task Analyze_LooseLists_NeedAnExistingCharacter()
    {
        var json = await _source.Exchange.ExportAliasesAsync(_source.AnaId);

        var noTarget = () => _target.Exchange.AnalyzeAsync(json, ImportTarget.None);
        var missing = () => _target.Exchange.AnalyzeAsync(json, ImportTarget.ForCharacter(4242));

        (await noTarget.Should().ThrowAsync<ExchangeFormatException>()).Which.Error.Should().Be(ExchangeError.TargetRequired);
        (await missing.Should().ThrowAsync<ExchangeFormatException>()).Which.Error.Should().Be(ExchangeError.TargetRequired);
    }

    [Fact]
    public async Task InvalidElements_AreReported_AndTheRestIsImported()
    {
        var characterId = await _target.AddCharacterAsync();
        const string json = """
            { "format": "omnimud", "formatVersion": 1, "kind": "lists",
              "aliases": [ { "command": "ok", "action": "bien" }, { "command": "", "action": "sin comando" },
                           { "command": "ok", "action": "repetido" }, null ],
              "paths": [ { "name": "vacío", "path": "" }, { "name": "bueno", "path": "n" } ],
              "movements": [ { "key": 18, "command": "x" } ] }
            """;

        var analysis = await _target.Exchange.AnalyzeAsync(json, ImportTarget.ForCharacter(characterId));
        analysis.Items.Where(i => i.Status == ImportItemStatus.Invalid).Select(i => i.Key)
            .Should().BeEquivalentTo("alias:", "alias:ok#2", "path:vacío", "movements");
        analysis.Items.Where(i => i.Status == ImportItemStatus.Invalid).Should().OnlyContain(i => !string.IsNullOrEmpty(i.Error));

        var summary = await _target.Exchange.ApplyAsync(analysis);

        (summary.Added, summary.Failed).Should().Be((2, 4));
        (await _target.Aliases.GetByCharacterAsync(characterId)).Should().ContainSingle().Which.Action.Should().Be("bien");
        (await _target.Paths.GetByCharacterAsync(characterId)).Should().ContainSingle().Which.Name.Should().Be("bueno");
    }

    [Fact]
    public async Task Apply_ReevaluatesConflicts_WhenTheDatabaseChangedAfterTheAnalysis()
    {
        var characterId = await _target.AddCharacterAsync();
        var json = await _source.Exchange.ExportAliasesAsync(_source.AnaId);
        var analysis = await _target.Exchange.AnalyzeAsync(json, ImportTarget.ForCharacter(characterId));
        analysis.HasConflicts.Should().BeFalse();

        await _target.Aliases.AddAsync(new AliasEntity { CharacterId = characterId, Command = "k", Action = "llegó después" });
        var summary = await _target.Exchange.ApplyAsync(analysis);

        (summary.Added, summary.Skipped).Should().Be((1, 1));
        (await _target.Aliases.GetByCharacterAsync(characterId)).Single(a => a.Command == "k").Action.Should().Be("llegó después");
    }

    [Fact]
    public async Task Files_RoundTrip_AndSizeLimit()
    {
        var directory = Directory.CreateTempSubdirectory("omnimud-exchange-");
        try
        {
            var path = Path.Combine(directory.FullName, "ana" + IExchangeService.FileExtension);
            var json = await _source.Exchange.ExportCharacterAsync(_source.AnaId);

            await _source.Exchange.SaveToFileAsync(path, json);
            (await _source.Exchange.LoadFromFileAsync(path)).Should().Be(json);
            File.ReadAllBytes(path).Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF }, "no BOM");
            json.Should().Contain("ñ", "text is written readable, not \\u-escaped");

            var big = Path.Combine(directory.FullName, "big.omnimud");
            await using (var stream = File.Create(big))
                stream.SetLength(4L * IExchangeService.MaxDocumentLength + 1);
            var load = () => _source.Exchange.LoadFromFileAsync(big);
            (await load.Should().ThrowAsync<ExchangeFormatException>()).Which.Error.Should().Be(ExchangeError.TooLarge);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    // ═════════════════════════════ Helpers ═════════════════════════════

    private static Dictionary<string, ImportDecision> Decide(string key, ImportDecision decision) => new() { [key] = decision };

    /// <summary>A MUD "reinos" (other case) in the target with different data, Ana with a password, and Zoe who is not in the file.</summary>
    private async Task<(int MudId, int AnaId)> SeedConflictingMudAsync()
    {
        var setId = await _target.Rules.AddRuleSetAsync(new MessageRuleSetEntity { Name = "misreglas" });
        await _target.Rules.AddRuleAsync(new MessageRuleEntity { RuleSetId = setId, Pattern = "vieja" });
        var mudId = await _target.Muds.AddAsync(new MudEntity { Name = "reinos", Host = "viejo.example.org", Port = 23, MessageRuleSetId = setId });
        await _target.Directions.AddAsync(new DirectionEntity { MudId = mudId, Direction = "arriba", Abbreviation = "a" });

        var anaId = await _target.Characters.AddAsync(new CharacterEntity { MudId = mudId, Name = "ana", EncryptedPassword = [1, 2, 3] });
        await _target.Aliases.AddAsync(new AliasEntity { CharacterId = anaId, Command = "viejo", Action = "x" });
        await _target.Triggers.AddAsync(new TriggerEntity { Id = "viejo", CharacterId = anaId, Name = "Viejo", Pattern = "p", Action = "a" });

        var zoeId = await _target.Characters.AddAsync(new CharacterEntity { MudId = mudId, Name = "Zoe" });
        await _target.Aliases.AddAsync(new AliasEntity { CharacterId = zoeId, Command = "z", Action = "z" });
        return (mudId, anaId);
    }

    /// <summary>Decisions that blow up with a non-database exception when a given key is asked for.</summary>
    private sealed class ExplodingDecisions(string explodingKey) : IReadOnlyDictionary<string, ImportDecision>
    {
        public bool TryGetValue(string key, [MaybeNullWhen(false)] out ImportDecision value)
        {
            if (key == explodingKey) throw new TimeoutException("boom");
            value = default;
            return false;
        }

        public ImportDecision this[string key] => throw new KeyNotFoundException();
        public IEnumerable<string> Keys => [];
        public IEnumerable<ImportDecision> Values => [];
        public int Count => 0;
        public bool ContainsKey(string key) => false;
        public IEnumerator<KeyValuePair<string, ImportDecision>> GetEnumerator() => Enumerable.Empty<KeyValuePair<string, ImportDecision>>().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>One database with every repository and service on top.</summary>
    private sealed class World
    {
        public InMemoryDatabaseFixture Db { get; } = new();
        public SqliteMudRepository Muds { get; private set; } = null!;
        public SqliteCharacterRepository Characters { get; private set; } = null!;
        public SqliteAliasRepository Aliases { get; private set; } = null!;
        public SqliteTriggerRepository Triggers { get; private set; } = null!;
        public SqlitePathRepository Paths { get; private set; } = null!;
        public SqliteDirectionRepository Directions { get; private set; } = null!;
        public SqliteMovementRepository Movements { get; private set; } = null!;
        public SqliteMessageRuleRepository Rules { get; private set; } = null!;
        public SqliteOptionRepository OptionRows { get; private set; } = null!;
        public OptionsService Options { get; private set; } = null!;
        public ExchangeService Exchange { get; private set; } = null!;
        public int MudId { get; private set; }
        public int AnaId { get; private set; }
        public int BertoId { get; private set; }

        public async Task InitializeAsync()
        {
            await Db.InitializeAsync();
            Muds = new SqliteMudRepository(Db);
            Characters = new SqliteCharacterRepository(Db);
            Aliases = new SqliteAliasRepository(Db);
            Triggers = new SqliteTriggerRepository(Db);
            Paths = new SqlitePathRepository(Db);
            Directions = new SqliteDirectionRepository(Db);
            Movements = new SqliteMovementRepository(Db);
            Rules = new SqliteMessageRuleRepository(Db);
            OptionRows = new SqliteOptionRepository(Db);
            Options = new OptionsService(OptionRows);
            Exchange = new ExchangeService(Db, Options);
        }

        public async Task<int> AddCharacterAsync()
        {
            var mudId = await Muds.AddAsync(new MudEntity { Name = "Destino " + Guid.NewGuid().ToString("N"), Host = "h", Port = 1 });
            return await Characters.AddAsync(new CharacterEntity { MudId = mudId, Name = "Nuevo" });
        }

        public async Task SeedAsync()
        {
            var setId = await Rules.AddRuleSetAsync(new MessageRuleSetEntity { Name = "MisReglas" });
            await Rules.AddRuleAsync(new MessageRuleEntity { RuleSetId = setId, Pattern = @"^(\w+) te dice: (.*)$", Template = "$1: $2", CaseSensitive = true, Channel = "privado" });
            await Rules.AddRuleAsync(new MessageRuleEntity { RuleSetId = setId, Pattern = @"^\[(\w+)\]", Enabled = false });

            MudId = await Muds.AddAsync(new MudEntity
            {
                Name = "Reinos", Host = "rl.example.org", Port = 5001, UseTls = true, ValidateCertificate = false,
                Encoding = "iso-8859-1", SaveCommand = "guardar", QuitCommand = "salir", LoginScript = "%n\n%p",
                SoundDirectory = "reinos", MovementMode = true, MessageRuleSetId = setId
            });
            await Directions.AddAsync(new DirectionEntity { MudId = MudId, Direction = "norte", Abbreviation = "n", OppositeDirection = "sur" });
            await Directions.AddAsync(new DirectionEntity { MudId = MudId, Direction = "sur", Abbreviation = "s", OppositeDirection = "norte" });
            await Movements.SetAsync(MudId, null, 8, "norte");
            await Options.SaveAsync(OptionScope.Mud, MudId, new OmnimudOptions { Volume = 20, LogType = LogMode.PerSession });

            AnaId = await Characters.AddAsync(new CharacterEntity { MudId = MudId, Name = "Ana", EncryptedPassword = Password, IsDefault = true, MovementMode = true });
            await Aliases.AddAsync(new AliasEntity { CharacterId = AnaId, Command = "k", Action = "matar $1" });
            await Aliases.AddAsync(new AliasEntity { CharacterId = AnaId, Command = "h", Action = "curar", Enabled = false });
            await Triggers.AddAsync(new TriggerEntity { Id = Guid.NewGuid().ToString(), CharacterId = AnaId, Name = "Primero", Pattern = "@cmd", Action = "om.send('hola, señor')", ActionType = 2, Priority = 10 });
            await Triggers.AddAsync(new TriggerEntity
            {
                Id = Guid.NewGuid().ToString(), CharacterId = AnaId, Name = "Segundo", Pattern = @"^Vida: (\d+)", PatternType = 1, Action = "",
                ActionType = 3, Sound = "ding.wav", Enabled = false, CaseSensitive = true, Priority = 90, Multiline = true, GagLine = true
            });
            await Triggers.AddAsync(new TriggerEntity { Id = Guid.NewGuid().ToString(), CharacterId = AnaId, Name = "Tercero", Pattern = "%s llega", PatternType = 2, Action = "saludar %1" });
            await Paths.AddAsync(new PathEntity { CharacterId = AnaId, Name = "plaza", Path = "3n2e" });
            await Movements.SetAsync(null, AnaId, 5, "mirar");
            await Options.SaveAsync(OptionScope.Character, AnaId, new OmnimudOptions { Volume = 30, UseRepeatChar = true, RepeatChar = '*', FontSize = 12.5f, LogDirectory = @"D:\logs de Ana" });

            BertoId = await Characters.AddAsync(new CharacterEntity { MudId = MudId, Name = "Berto", EncryptedPassword = Password });
        }
    }
}
