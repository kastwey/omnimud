using System.Globalization;
using Omnimud.Data.Entities;
using Omnimud.Data.Exchange;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Tests.Presenters;

/// <summary>End to end against a real SQLite file and real .omnimud files on disk.</summary>
public sealed class ImportExportPresenterTests : IDisposable
{
    private const string Secret = "contraseña-muy-secreta";

    private readonly TempDatabase _db = new();
    private readonly ScriptedPrompts _prompts = new();
    private readonly FakeProtector _protector = new();

    public ImportExportPresenterTests() => Strings.Culture = CultureInfo.GetCultureInfo("es");

    public void Dispose() => _db.Dispose();

    private ImportExportPresenter Presenter(ImportDecision? onConflict = ImportDecision.Skip) =>
        new(_db.Exchange, _prompts, _conflicts = new ScriptedConflicts(onConflict));

    private ScriptedConflicts _conflicts = null!;

    /// <summary>A MUD with two characters; the first has password, aliases, triggers and a path.</summary>
    private async Task<(MudEntity Mud, CharacterEntity Aldara, CharacterEntity Borin)> SeedAsync()
    {
        var ruleSet = (await _db.Rules.GetRuleSetsAsync())[0];
        var mud = new MudEntity
        {
            Name = "Reinos", Host = "rlmud.org", Port = 5001, UseTls = true, ValidateCertificate = false, Encoding = "iso-8859-1",
            SaveCommand = "salvar", QuitCommand = "abandonar", LoginScript = "%character\n%password", MessageRuleSetId = ruleSet.Id,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        mud.Id = await _db.Muds.AddAsync(mud);

        var aldara = await _db.AddCharacterAsync(mud.Id, "Aldara", _protector.Protect(Secret));
        var borin = await _db.AddCharacterAsync(mud.Id, "Borin");
        await _db.Characters.SetDefaultAsync(mud.Id, aldara.Id);

        await _db.Aliases.AddAsync(new AliasEntity { CharacterId = aldara.Id, Command = "k", Action = "matar %1" });
        await _db.Aliases.AddAsync(new AliasEntity { CharacterId = aldara.Id, Command = "cur", Action = "formular curar", Enabled = false });
        await _db.Triggers.AddAsync(new TriggerEntity
        {
            Id = Guid.NewGuid().ToString("N"), CharacterId = aldara.Id, Name = "hambre", Pattern = "Tienes hambre", Action = "comer pan",
            Priority = 10, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await _db.Triggers.AddAsync(new TriggerEntity
        {
            Id = Guid.NewGuid().ToString("N"), CharacterId = aldara.Id, Name = "lua", Pattern = "^(.+) llega", PatternType = 1,
            Action = "om.send('saludar ' .. matches[1])", ActionType = 2, GagLine = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await _db.Paths.AddAsync(new PathEntity { CharacterId = aldara.Id, Name = "plaza", Path = "2n3e" });
        return (mud, aldara, borin);
    }

    private sealed record Snapshot(object Mud, List<object> Characters);

    private async Task<Snapshot> SnapshotAsync(string mudName)
    {
        var mud = (await _db.Muds.GetByNameAsync(mudName))!;
        var characters = new List<object>();
        foreach (var c in await _db.Characters.GetByMudAsync(mud.Id))
        {
            characters.Add(new
            {
                c.Name, c.IsDefault,
                Aliases = (await _db.Aliases.GetByCharacterAsync(c.Id)).Select(a => (a.Command, a.Action, a.Enabled)).OrderBy(a => a.Command).ToList(),
                Triggers = (await _db.Triggers.GetByCharacterAsync(c.Id))
                    .Select(t => (t.Name, t.Pattern, t.PatternType, t.Action, t.ActionType, t.Priority, t.GagLine, t.Enabled)).ToList(),
                Paths = (await _db.Paths.GetByCharacterAsync(c.Id)).Select(p => (p.Name, p.Path)).ToList(),
            });
        }
        var ruleSet = mud.MessageRuleSetId is { } id ? (await _db.Rules.GetRuleSetByIdAsync(id))?.Name : null;
        return new Snapshot(new
        {
            mud.Name, mud.Host, mud.Port, mud.UseTls, mud.ValidateCertificate, mud.Encoding, mud.SaveCommand, mud.QuitCommand,
            mud.LoginScript, RuleSet = ruleSet,
        }, characters);
    }

    // ── Round trip ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportMudWithCharacters_DeleteIt_ImportIt_EverythingComesBack()
    {
        var (mud, _, _) = await SeedAsync();
        var before = await SnapshotAsync("Reinos");
        var file = _db.FilePath("reinos.omnimud");
        _prompts.SaveFile = file;
        _prompts.OpenFile = file;
        var presenter = Presenter();

        (await presenter.ExportMudAsync(mud.Id, mud.Name)).Should().BeTrue();
        await _db.Muds.DeleteAsync(mud.Id);
        (await _db.Muds.GetByNameAsync("Reinos")).Should().BeNull();

        var summary = await presenter.ImportAsync(ImportTarget.None);

        summary.Should().NotBeNull();
        summary!.Failed.Should().Be(0);
        summary.Skipped.Should().Be(0);
        summary.Added.Should().BeGreaterThanOrEqualTo(3, "the MUD and its two characters");
        (await SnapshotAsync("Reinos")).Should().BeEquivalentTo(before);
        _conflicts.Asked.Should().BeEmpty("nothing existed any more");
        _prompts.Infos.Last().Should().Be(ImportExportPresenter.FormatSummary(summary));
    }

    [Fact]
    public async Task Passwords_AreNeverExported_AndImportedCharactersHaveNone()
    {
        var (mud, aldara, _) = await SeedAsync();
        var mudFile = _db.FilePath("mud.omnimud");
        var characterFile = _db.FilePath("character.omnimud");
        var presenter = Presenter();

        _prompts.SaveFile = mudFile;
        await presenter.ExportMudAsync(mud.Id, mud.Name);
        _prompts.SaveFile = characterFile;
        await presenter.ExportCharacterAsync(aldara.Id, aldara.Name);

        foreach (var file in new[] { mudFile, characterFile })
        {
            var text = await File.ReadAllTextAsync(file);
            text.Should().NotContain(Secret).And.NotContain("enc:").And.NotContainEquivalentOf("password\":");
            text.Should().NotContain(Convert.ToBase64String(_protector.Protect(Secret)));
        }

        await _db.Muds.DeleteAsync(mud.Id);
        _prompts.OpenFile = mudFile;
        await presenter.ImportAsync(ImportTarget.None);

        var imported = await _db.Characters.GetByMudAsync((await _db.Muds.GetByNameAsync("Reinos"))!.Id);
        imported.Should().HaveCount(2).And.OnlyContain(c => c.EncryptedPassword == null);
    }

    [Fact]
    public async Task ExportMud_AnsweringNoToCharacters_ExportsTheMudAlone()
    {
        var (mud, _, _) = await SeedAsync();
        _prompts.SaveFile = _prompts.OpenFile = _db.FilePath("solo.omnimud");
        _prompts.ConfirmAnswers.Enqueue(false);
        var presenter = Presenter();

        await presenter.ExportMudAsync(mud.Id, mud.Name);
        await _db.Muds.DeleteAsync(mud.Id);
        await presenter.ImportAsync(ImportTarget.None);

        _prompts.Confirms.Should().Equal(string.Format(Strings.Exchange_IncludeCharacters, "Reinos"));
        (await _db.Characters.GetByMudAsync((await _db.Muds.GetByNameAsync("Reinos"))!.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task ExportCharacter_ImportIntoAnotherMud_BringsItsLists()
    {
        var (_, aldara, _) = await SeedAsync();
        var other = await _db.AddMudAsync("Otro");
        _prompts.SaveFile = _prompts.OpenFile = _db.FilePath("aldara.omnimud");
        var presenter = Presenter();

        (await presenter.ExportCharacterAsync(aldara.Id, aldara.Name)).Should().BeTrue();
        var summary = await presenter.ImportAsync(ImportTarget.ForMud(other.Id));

        summary!.Failed.Should().Be(0);
        var copy = (await _db.Characters.GetByMudAsync(other.Id)).Should().ContainSingle().Subject;
        copy.Name.Should().Be("Aldara");
        (await _db.Aliases.GetByCharacterAsync(copy.Id)).Select(a => a.Command).Should().BeEquivalentTo("k", "cur");
        (await _db.Triggers.GetByCharacterAsync(copy.Id)).Select(t => t.Name).Should().Equal("hambre", "lua");
        (await _db.Paths.GetByCharacterAsync(copy.Id)).Select(p => p.Name).Should().Equal("plaza");
    }

    [Fact]
    public void SuggestedFileName_IsTheNameWithoutForbiddenCharacters()
    {
        ImportExportPresenter.SafeFileName(" Reinos: de/Leyenda? ").Should().Be("Reinos_ de_Leyenda_");
        ImportExportPresenter.SafeFileName("  ").Should().Be("omnimud");
    }

    [Fact]
    public async Task Export_SuggestsTheNameWithTheOmnimudExtension_AndCancellingWritesNothing()
    {
        var (mud, _, _) = await SeedAsync();
        _prompts.SaveFile = null;

        (await Presenter().ExportMudAsync(mud.Id, mud.Name)).Should().BeFalse();

        _prompts.SuggestedFileName.Should().Be("Reinos.omnimud");
        _prompts.Infos.Should().BeEmpty();
        _prompts.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Export_ToAnImpossiblePath_IsAnErrorMessage_NotAnException()
    {
        var (mud, _, _) = await SeedAsync();
        _prompts.SaveFile = Path.Combine(_db.FilePath("no-existe"), "sub", "x.omnimud");

        (await Presenter().ExportMudAsync(mud.Id, mud.Name)).Should().BeFalse();

        _prompts.Errors.Should().ContainSingle().Which.Should().Contain("x.omnimud");
    }

    // ── Conflicts ──────────────────────────────────────────────────────────

    private async Task<string> ExportThenChangeAsync()
    {
        var (mud, aldara, _) = await SeedAsync();
        var file = _db.FilePath("conflict.omnimud");
        _prompts.SaveFile = _prompts.OpenFile = file;
        await Presenter().ExportMudAsync(mud.Id, mud.Name);

        // The database moves on after the export.
        mud.Host = "cambiado.example.org";
        await _db.Muds.UpdateAsync(mud);
        foreach (var alias in await _db.Aliases.GetByCharacterAsync(aldara.Id))
            await _db.Aliases.DeleteAsync(alias.Id);
        await _db.Aliases.AddAsync(new AliasEntity { CharacterId = aldara.Id, Command = "local", Action = "solo aquí" });
        return file;
    }

    [Fact]
    public async Task Conflicts_Skip_LeavesTheDatabaseAsItIs()
    {
        await ExportThenChangeAsync();
        var presenter = Presenter(ImportDecision.Skip);

        var summary = await presenter.ImportAsync(ImportTarget.None);

        _conflicts.Asked.Should().ContainSingle();
        _conflicts.Asked[0].Where(i => i.Status == ImportItemStatus.Conflict).Select(i => (i.Kind, i.Name))
            .Should().Contain([(ImportItemKind.Mud, "Reinos"), (ImportItemKind.Character, "Aldara"), (ImportItemKind.Character, "Borin")]);
        summary!.Overwritten.Should().Be(0);
        summary.Skipped.Should().BeGreaterThanOrEqualTo(3);
        (await _db.Muds.GetByNameAsync("Reinos"))!.Host.Should().Be("cambiado.example.org");
        var aldara = (await _db.Characters.GetByMudAsync((await _db.Muds.GetByNameAsync("Reinos"))!.Id)).Single(c => c.Name == "Aldara");
        (await _db.Aliases.GetByCharacterAsync(aldara.Id)).Select(a => a.Command).Should().Equal("local");
    }

    [Fact]
    public async Task Conflicts_Overwrite_RestoresWhatTheFileSays_KeepingTheStoredPassword()
    {
        await ExportThenChangeAsync();
        var presenter = Presenter(ImportDecision.Overwrite);

        var summary = await presenter.ImportAsync(ImportTarget.None);

        summary!.Overwritten.Should().BeGreaterThanOrEqualTo(3);
        summary.Failed.Should().Be(0);
        (await _db.Muds.GetByNameAsync("Reinos"))!.Host.Should().Be("rlmud.org");
        var aldara = (await _db.Characters.GetByMudAsync((await _db.Muds.GetByNameAsync("Reinos"))!.Id)).Single(c => c.Name == "Aldara");
        (await _db.Aliases.GetByCharacterAsync(aldara.Id)).Select(a => a.Command).Should().BeEquivalentTo("k", "cur");
        _protector.Unprotect(aldara.EncryptedPassword!).Should().Be(Secret, "overwriting a character keeps the password stored here");
    }

    [Fact]
    public async Task Conflicts_Cancelled_ImportsNothing_AndShowsNoSummary()
    {
        await ExportThenChangeAsync();
        _prompts.Infos.Clear();
        var presenter = Presenter(onConflict: null);

        (await presenter.ImportAsync(ImportTarget.None)).Should().BeNull();

        (await _db.Muds.GetByNameAsync("Reinos"))!.Host.Should().Be("cambiado.example.org");
        _prompts.Infos.Should().BeEmpty();
    }

    // ── Rejected files ─────────────────────────────────────────────────────

    [Fact]
    public async Task Import_Cancelled_AtTheFilePicker_DoesNothing()
    {
        _prompts.OpenFile = null;

        (await Presenter().ImportAsync(ImportTarget.None)).Should().BeNull();

        _prompts.Errors.Should().BeEmpty();
    }

    public static TheoryData<string, ExchangeError> BadFiles => new()
    {
        { "esto no es json", ExchangeError.InvalidJson },
        { "{ \"format\": \"omnimud\", \"formatVersion\": 1, \"kind\": \"mud\"", ExchangeError.InvalidJson },
        { "{ \"hola\": 1 }", ExchangeError.NotAnOmnimudFile },
        { "{ \"format\": \"omnimud\", \"formatVersion\": 999, \"kind\": \"mud\" }", ExchangeError.UnsupportedVersion },
    };

    [Theory]
    [MemberData(nameof(BadFiles))]
    public async Task BadFile_IsAClearLocalizedMessage_NotAnException(string content, ExchangeError expected)
    {
        var file = _db.FilePath("bad.omnimud");
        await File.WriteAllTextAsync(file, content);
        _prompts.OpenFile = file;

        (await Presenter().ImportAsync(ImportTarget.None)).Should().BeNull();

        _prompts.Errors.Should().Equal(ImportExportPresenter.DescribeError(expected));
    }

    [Fact]
    public async Task CharacterFile_WithoutAMud_ExplainsThatTheMudMustBeSelected()
    {
        var (_, aldara, _) = await SeedAsync();
        _prompts.SaveFile = _prompts.OpenFile = _db.FilePath("aldara.omnimud");
        var presenter = Presenter();
        await presenter.ExportCharacterAsync(aldara.Id, aldara.Name);

        (await presenter.ImportAsync(ImportTarget.None)).Should().BeNull();

        _prompts.Errors.Should().Equal(Strings.Exchange_ErrorTargetRequired);
    }

    [Fact]
    public async Task MissingFile_IsAnErrorMessage()
    {
        (await Presenter().ImportFileAsync(_db.FilePath("no-existe.omnimud"), ImportTarget.None)).Should().BeNull();

        _prompts.Errors.Should().ContainSingle().Which.Should().StartWith(Strings.Exchange_ReadFailed.Split('{')[0]);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void EveryFormatError_HasItsOwnMessage_InEveryLanguage(string culture)
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);

        var messages = Enum.GetValues<ExchangeError>().Select(ImportExportPresenter.DescribeError).ToList();

        messages.Should().OnlyContain(m => !string.IsNullOrWhiteSpace(m)).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Summary_CountsFirst()
    {
        var (mud, _, _) = await SeedAsync();
        _prompts.SaveFile = _prompts.OpenFile = _db.FilePath("s.omnimud");
        var presenter = Presenter();
        await presenter.ExportMudAsync(mud.Id, mud.Name);
        await _db.Muds.DeleteAsync(mud.Id);

        var summary = await presenter.ImportAsync(ImportTarget.None);

        var lines = ImportExportPresenter.FormatSummary(summary!).Split(Environment.NewLine);
        lines.Should().StartWith([
            Strings.Exchange_SummaryHeader,
            string.Format(Strings.Exchange_SummaryAdded, summary!.Added),
            string.Format(Strings.Exchange_SummaryOverwritten, 0),
            string.Format(Strings.Exchange_SummarySkipped, 0),
            string.Format(Strings.Exchange_SummaryFailed, 0)]);
    }
}
