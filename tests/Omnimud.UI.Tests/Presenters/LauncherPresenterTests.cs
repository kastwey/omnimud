using System.Globalization;
using NSubstitute;
using Omnimud.Core.Security;
using Omnimud.Core.Session;
using Omnimud.Data.Entities;
using Omnimud.Data.Exchange;
using Omnimud.Data.Repositories;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Tests.Presenters;

/// <summary>Flows of the start window against a real SQLite file, with the dialogs answered by code.</summary>
public sealed class LauncherPresenterTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private readonly ScriptedPrompts _prompts = new();
    private readonly ScriptedLauncherDialogs _dialogs = new();
    private readonly FakeProtector _protector = new();
    private readonly List<SessionProfile> _opened = [];
    private readonly LauncherPresenter _presenter;
    private int _repaints;

    public LauncherPresenterTests()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("es");
        _presenter = new LauncherPresenter(_db.Muds, _db.Characters, _db.Rules, _protector, _prompts, _dialogs,
            new ImportExportPresenter(_db.Exchange, _prompts, new ScriptedConflicts(ImportDecision.Overwrite)), _opened.Add);
        _presenter.Changed += () => _repaints++;
    }

    public void Dispose() => _db.Dispose();

    /// <summary>Alfa (Ana, Berto, Carla), Beta (no characters), Gamma (Dani).</summary>
    private async Task<(MudEntity Alfa, MudEntity Beta, MudEntity Gamma, CharacterEntity[] AlfaChars)> SeedAsync()
    {
        var alfa = await _db.AddMudAsync("Alfa");
        var beta = await _db.AddMudAsync("Beta");
        var gamma = await _db.AddMudAsync("Gamma");
        var chars = new[]
        {
            await _db.AddCharacterAsync(alfa.Id, "Ana", _protector.Protect("clave-ana")),
            await _db.AddCharacterAsync(alfa.Id, "Berto"),
            await _db.AddCharacterAsync(alfa.Id, "Carla"),
        };
        await _db.AddCharacterAsync(gamma.Id, "Dani");
        await _presenter.LoadAsync();
        return (alfa, beta, gamma, chars);
    }

    // ── Loading ────────────────────────────────────────────────────────────

    [Fact]
    public async Task EmptyDatabase_NothingSelected_OnlyAddingAMudMakesSense()
    {
        await _presenter.LoadAsync();

        _presenter.Nodes.Should().BeEmpty();
        _presenter.Selection.Should().BeNull();
        (_presenter.CanConnect, _presenter.CanEdit, _presenter.CanRemove, _presenter.CanAddCharacter, _presenter.CanSetDefault)
            .Should().Be((false, false, false, false, false));
        _presenter.Status.Should().Be(string.Format(Strings.Launcher_MudsConfigured, 0));
    }

    [Fact]
    public async Task Load_BuildsTheTree_AndSelectsTheFirstMud_NeverNothing()
    {
        var (alfa, _, _, _) = await SeedAsync();

        _presenter.Nodes.Select(n => n.Mud.Name).Should().Equal("Alfa", "Beta", "Gamma");
        _presenter.Nodes[0].Characters.Select(c => c.Name).Should().Equal("Ana", "Berto", "Carla");
        _presenter.Nodes[0].Text.Should().Be("Alfa (mud.example.org:4000)");
        _presenter.Selection.Should().Be(new LauncherSelection(alfa.Id));
        _repaints.Should().Be(1);
    }

    [Fact]
    public async Task AddCharacter_WithoutAnyMud_ExplainsThatAMudComesFirst()
    {
        await _presenter.LoadAsync();
        _dialogs.OnEditCharacter = _ => throw new InvalidOperationException("The dialog must not open.");

        await _presenter.AddCharacterAsync();

        _prompts.Infos.Should().Equal(Strings.Launcher_NeedsMudFirst);
    }

    // ── Selection kept after add / edit / remove ───────────────────────────

    [Fact]
    public async Task AddMud_SelectsTheNewMud()
    {
        await SeedAsync();
        _dialogs.OnEditMud = m => { m.IsNew.Should().BeTrue(); m.Name = "Delta"; m.Host = "delta.org"; m.Port = 23; return true; };

        await _presenter.AddMudAsync();

        _presenter.SelectedMud!.Name.Should().Be("Delta");
        _presenter.Selection!.CharacterId.Should().BeNull();
        _presenter.Status.Should().Be(string.Format(Strings.Launcher_StatusMudAdded, "Delta"));
    }

    [Fact]
    public async Task AddMud_Cancelled_KeepsSelection_AndDoesNotReload()
    {
        var (_, _, _, chars) = await SeedAsync();
        _presenter.Select(new LauncherSelection(chars[1].MudId, chars[1].Id));
        _repaints = 0;

        await _presenter.AddMudAsync();

        _presenter.SelectedCharacter!.Name.Should().Be("Berto");
        _repaints.Should().Be(0);
    }

    [Fact]
    public async Task AddMud_DuplicateName_IsRejectedByTheDialogModel_AndNothingChanges()
    {
        await SeedAsync();
        _dialogs.OnEditMud = m => { m.Name = "ALFA"; m.Host = "x.org"; return true; };

        await _presenter.AddMudAsync();

        _presenter.Nodes.Should().HaveCount(3);
    }

    [Fact]
    public async Task AddCharacter_GoesToTheMudOfTheSelectedNode_ExpandsIt_AndSelectsTheCharacter()
    {
        var (_, beta, _, _) = await SeedAsync();
        _presenter.Select(new LauncherSelection(beta.Id));
        _presenter.SetExpanded(beta.Id, false);
        _dialogs.OnEditCharacter = m => { m.MudId.Should().Be(beta.Id); m.Name = "Eva"; m.Password = "clave"; return true; };

        await _presenter.AddCharacterAsync();

        _presenter.SelectedCharacter!.Name.Should().Be("Eva");
        _presenter.Selection!.MudId.Should().Be(beta.Id);
        _presenter.IsExpanded(beta.Id).Should().BeTrue();
        _protector.Unprotect(_presenter.SelectedCharacter.EncryptedPassword!).Should().Be("clave");
    }

    [Fact]
    public async Task Insert_AddsASiblingOfWhatIsSelected()
    {
        var (alfa, _, _, chars) = await SeedAsync();
        var asked = new List<string>();
        _dialogs.OnEditMud = _ => { asked.Add("mud"); return false; };
        _dialogs.OnEditCharacter = _ => { asked.Add("character"); return false; };

        _presenter.Select(new LauncherSelection(alfa.Id));
        await _presenter.AddForSelectionAsync();
        _presenter.Select(new LauncherSelection(alfa.Id, chars[0].Id));
        await _presenter.AddForSelectionAsync();

        asked.Should().Equal("mud", "character");
    }

    [Fact]
    public async Task EditMud_KeepsItSelected_AndShowsTheNewName()
    {
        var (_, beta, _, _) = await SeedAsync();
        _presenter.Select(new LauncherSelection(beta.Id));
        _dialogs.OnEditMud = m => { m.IsNew.Should().BeFalse(); m.Name = "Beta 2"; m.UseTls = true; m.ValidateCertificate = false; return true; };

        await _presenter.EditAsync();

        _presenter.Selection.Should().Be(new LauncherSelection(beta.Id));
        _presenter.SelectedMud.Should().BeEquivalentTo(new { Name = "Beta 2", UseTls = true, ValidateCertificate = false });
        _presenter.Status.Should().Be(string.Format(Strings.Launcher_StatusMudSaved, "Beta 2"));
    }

    [Fact]
    public async Task EditCharacter_KeepsItSelected()
    {
        var (alfa, _, _, chars) = await SeedAsync();
        _presenter.Select(new LauncherSelection(alfa.Id, chars[1].Id));
        _dialogs.OnEditCharacter = m => { m.Name = "Bertín"; return true; };

        await _presenter.EditAsync();

        _presenter.Selection.Should().Be(new LauncherSelection(alfa.Id, chars[1].Id));
        _presenter.SelectedCharacter!.Name.Should().Be("Bertín");
    }

    [Fact]
    public async Task EditCharacter_UncheckingRemember_DeletesTheStoredPassword()
    {
        var (alfa, _, _, chars) = await SeedAsync();
        _presenter.Select(new LauncherSelection(alfa.Id, chars[0].Id));
        _dialogs.OnEditCharacter = m => { m.HasStoredPassword.Should().BeTrue(); m.Password.Should().BeEmpty(); m.RememberPassword = false; return true; };

        await _presenter.EditAsync();

        (await _db.Characters.GetByIdAsync(chars[0].Id))!.EncryptedPassword.Should().BeNull();
    }

    [Theory]
    [InlineData(0, "Berto")]
    [InlineData(1, "Carla")]
    [InlineData(2, "Berto")]
    public async Task RemoveCharacter_SelectsTheNeighbour(int remove, string expected)
    {
        var (alfa, _, _, chars) = await SeedAsync();
        _presenter.Select(new LauncherSelection(alfa.Id, chars[remove].Id));

        await _presenter.RemoveAsync();

        _prompts.Confirms.Should().Equal(string.Format(Strings.Launcher_ConfirmRemoveCharacter, chars[remove].Name, "Alfa"));
        _presenter.SelectedCharacter!.Name.Should().Be(expected);
        _presenter.Nodes[0].Characters.Should().HaveCount(2);
    }

    [Fact]
    public async Task RemoveTheOnlyCharacter_SelectsItsMud()
    {
        var (_, _, gamma, _) = await SeedAsync();
        var dani = _presenter.Nodes[2].Characters[0];
        _presenter.Select(new LauncherSelection(gamma.Id, dani.Id));

        await _presenter.RemoveAsync();

        _presenter.Selection.Should().Be(new LauncherSelection(gamma.Id));
    }

    [Theory]
    [InlineData("Alfa", "Beta")]
    [InlineData("Beta", "Gamma")]
    [InlineData("Gamma", "Beta")]
    public async Task RemoveMud_SelectsTheNeighbour(string remove, string expected)
    {
        await SeedAsync();
        _presenter.Select(new LauncherSelection(_presenter.Nodes.Single(n => n.Mud.Name == remove).Mud.Id));

        await _presenter.RemoveAsync();

        _prompts.Confirms.Should().Equal(string.Format(Strings.Launcher_ConfirmRemoveMud, remove));
        _presenter.SelectedMud!.Name.Should().Be(expected);
        _presenter.Nodes.Should().HaveCount(2);
    }

    [Fact]
    public async Task Remove_AnsweringNo_RemovesNothing()
    {
        await SeedAsync();
        _prompts.DefaultConfirm = false;
        _repaints = 0;

        await _presenter.RemoveAsync();

        _presenter.Nodes.Should().HaveCount(3);
        _repaints.Should().Be(0);
    }

    [Fact]
    public async Task Expansion_IsRemembered_AcrossReloads()
    {
        var (alfa, beta, _, _) = await SeedAsync();
        _presenter.IsExpanded(alfa.Id).Should().BeTrue("MUDs start expanded");
        _presenter.SetExpanded(alfa.Id, false);

        await _presenter.LoadAsync();

        _presenter.IsExpanded(alfa.Id).Should().BeFalse();
        _presenter.IsExpanded(beta.Id).Should().BeTrue();
    }

    // ── Commands and context menu ──────────────────────────────────────────

    [Fact]
    public async Task ContextMenu_DependsOnTheSelectedNode()
    {
        await _presenter.LoadAsync();
        _presenter.GetContextMenu().Select(e => e.Command).Should().Equal(LauncherCommand.AddMud, LauncherCommand.Import);

        var (alfa, _, _, chars) = await SeedAsync();
        _presenter.Select(new LauncherSelection(alfa.Id));
        _presenter.GetContextMenu().Select(e => e.Command).Should().Equal(LauncherCommand.Connect, LauncherCommand.Edit,
            LauncherCommand.Remove, LauncherCommand.AddCharacter, LauncherCommand.ExportMud);

        _presenter.Select(new LauncherSelection(alfa.Id, chars[0].Id));
        _presenter.GetContextMenu().Select(e => e.Command).Should().Equal(LauncherCommand.Connect, LauncherCommand.Edit,
            LauncherCommand.Remove, LauncherCommand.SetDefault, LauncherCommand.ExportCharacter);
        _presenter.GetEmptyAreaContextMenu().Select(e => e.Command).Should().Equal(LauncherCommand.AddMud, LauncherCommand.Import);
    }

    [Fact]
    public async Task ContextMenu_EnabledState_IsTheSameAsCanExecute_AndTheDefaultIsChecked()
    {
        var (alfa, _, _, chars) = await SeedAsync();
        _presenter.Select(new LauncherSelection(alfa.Id, chars[0].Id));
        await _presenter.SetDefaultAsync();

        var entries = _presenter.GetContextMenu();

        entries.Should().OnlyContain(e => e.Enabled == _presenter.CanExecute(e.Command));
        entries.Single(e => e.Command == LauncherCommand.SetDefault).Should().BeEquivalentTo(new { Enabled = false, Checked = true });
    }

    [Fact]
    public async Task Execute_RunsTheCommand_AndIgnoresTheOnesThatCannotRunNow()
    {
        var (alfa, _, _, _) = await SeedAsync();
        _presenter.Select(new LauncherSelection(alfa.Id));

        await _presenter.ExecuteAsync(LauncherCommand.SetDefault);
        await _presenter.ExecuteAsync(LauncherCommand.ExportCharacter);
        _prompts.Infos.Should().BeEmpty("a MUD is selected: those commands are disabled");

        await _presenter.ExecuteAsync(LauncherCommand.Connect);
        _opened.Should().ContainSingle();

        await _presenter.ExecuteAsync(LauncherCommand.Remove);
        _presenter.Nodes.Select(n => n.Mud.Name).Should().Equal("Beta", "Gamma");
    }

    // ── Default character ──────────────────────────────────────────────────

    [Fact]
    public async Task SetDefault_MarksOnlyThatCharacter_ShowsItInTheNode_AndKeepsTheSelection()
    {
        var (alfa, _, _, chars) = await SeedAsync();
        _presenter.Select(new LauncherSelection(alfa.Id, chars[1].Id));
        _presenter.CanSetDefault.Should().BeTrue();

        await _presenter.SetDefaultAsync();

        _presenter.Selection.Should().Be(new LauncherSelection(alfa.Id, chars[1].Id));
        _presenter.Nodes[0].Characters.Select(LauncherMudNode.CharacterText).Should().Equal("Ana", "Berto (predeterminado)", "Carla");
        _presenter.CanSetDefault.Should().BeFalse();
        _presenter.CanClearDefault.Should().BeTrue();
        _presenter.Status.Should().Be(string.Format(Strings.Launcher_StatusDefaultSet, "Berto", "Alfa"));

        _presenter.Select(new LauncherSelection(alfa.Id, chars[2].Id));
        await _presenter.SetDefaultAsync();
        _presenter.Nodes[0].Characters.Where(c => c.IsDefault).Select(c => c.Name).Should().Equal("Carla");
    }

    [Fact]
    public async Task ClearDefault_LeavesTheMudWithoutDefault()
    {
        var (alfa, _, _, chars) = await SeedAsync();
        _presenter.Select(new LauncherSelection(alfa.Id, chars[0].Id));
        await _presenter.SetDefaultAsync();

        await _presenter.ClearDefaultAsync();

        _presenter.Nodes[0].Characters.Should().OnlyContain(c => !c.IsDefault);
        _presenter.SelectedCharacter!.Name.Should().Be("Ana");
    }

    [Fact]
    public async Task SetDefault_OnAMud_IsNotPossible()
    {
        await SeedAsync();

        _presenter.CanSetDefault.Should().BeFalse();
        await _presenter.SetDefaultAsync();

        _presenter.Nodes[0].Characters.Should().OnlyContain(c => !c.IsDefault);
    }

    [Theory]
    [InlineData("en", "Ana (default)")]
    [InlineData("es", "Ana (predeterminado)")]
    public void DefaultMark_IsLocalized(string culture, string expected)
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);

        LauncherMudNode.CharacterText(new CharacterEntity { Name = "Ana", IsDefault = true }).Should().Be(expected);
    }

    // ── Connect ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Connect_OnACharacter_OpensItsSession_WithThePasswordDecrypted()
    {
        var (alfa, _, _, chars) = await SeedAsync();
        _presenter.Select(new LauncherSelection(alfa.Id, chars[0].Id));

        _presenter.Connect();

        _opened.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Title = "Ana - Alfa", Host = "mud.example.org", Port = 4000, MudId = (int?)alfa.Id, MudName = "Alfa",
            CharacterId = (int?)chars[0].Id, CharacterName = "Ana", CharacterPassword = "clave-ana",
        });
        _presenter.Status.Should().Be(string.Format(Strings.Launcher_ConnectingTo, "Ana - Alfa"));
    }

    [Fact]
    public async Task Connect_OnAMud_UsesItsDefaultCharacter()
    {
        var (alfa, _, _, chars) = await SeedAsync();
        await _db.Characters.SetDefaultAsync(alfa.Id, chars[2].Id);
        await _presenter.LoadAsync();
        _presenter.Select(new LauncherSelection(alfa.Id));

        _presenter.Connect();

        _opened.Single().CharacterName.Should().Be("Carla");
    }

    [Fact]
    public async Task Connect_OnAMudWithoutDefault_ConnectsWithoutCharacter()
    {
        var (alfa, _, _, _) = await SeedAsync();
        _presenter.Select(new LauncherSelection(alfa.Id));

        _presenter.Connect();

        _opened.Single().Should().BeEquivalentTo(new { Title = "Alfa", CharacterId = (int?)null, CharacterPassword = (string?)null });
    }

    [Fact]
    public void BuildProfile_CarriesEveryConnectionFieldOfTheMud()
    {
        var mud = new MudEntity
        {
            Id = 4, Name = "Reinos", Host = "rlmud.org", Port = 5001, UseTls = true, ValidateCertificate = false, Encoding = "iso-8859-1",
            LoginScript = "login", SaveCommand = "salvar", QuitCommand = "abandonar", SoundDirectory = @"C:\s",
        };

        _presenter.BuildProfile(mud, null).Should().BeEquivalentTo(new SessionProfile
        {
            Title = "Reinos", Host = "rlmud.org", Port = 5001, UseTls = true, ValidateCertificate = false, Encoding = "iso-8859-1",
            LoginScript = "login", SaveCommand = "salvar", QuitCommand = "abandonar", SoundDirectory = @"C:\s", MudId = 4, MudName = "Reinos",
        });
    }

    [Fact]
    public void BuildProfile_UnreadablePassword_IsSimplyNotSent()
    {
        var protector = Substitute.For<IPasswordProtector>();
        protector.Unprotect(Arg.Any<byte[]>()).Returns(_ => throw new System.Security.Cryptography.CryptographicException());
        var presenter = new LauncherPresenter(Substitute.For<IMudRepository>(), Substitute.For<ICharacterRepository>(),
            Substitute.For<IMessageRuleRepository>(), protector, _prompts, _dialogs,
            new ImportExportPresenter(Substitute.For<IExchangeService>(), _prompts, new ScriptedConflicts(null)), _ => { });

        var profile = presenter.BuildProfile(new MudEntity { Id = 1, Name = "M", Host = "h", Port = 1 },
            new CharacterEntity { Id = 2, Name = "C", EncryptedPassword = [1, 2, 3] });

        profile.CharacterPassword.Should().BeNull();
        profile.CharacterName.Should().Be("C");
    }

    [Fact]
    public async Task QuickConnect_OpensTheProfileOfTheDialog_OrNothingWhenCancelled()
    {
        await _presenter.LoadAsync();
        _presenter.QuickConnect();
        _opened.Should().BeEmpty();

        var profile = new SessionProfile { Title = "x:1", Host = "x", Port = 1, UseTls = true, Encoding = "ascii" };
        _dialogs.QuickConnectProfile = profile;
        _presenter.QuickConnect();

        _opened.Should().Equal(profile);
    }

    // ── Import and export from the tree ────────────────────────────────────

    [Fact]
    public async Task ExportMud_Then_Import_AfterRemovingIt_BringsTheMudBack_AndReloadsTheTree()
    {
        var (alfa, _, _, _) = await SeedAsync();
        _prompts.SaveFile = _prompts.OpenFile = _db.FilePath("alfa.omnimud");
        _presenter.Select(new LauncherSelection(alfa.Id));

        await _presenter.ExportMudAsync();
        _presenter.Status.Should().Be(string.Format(Strings.Launcher_StatusExported, "Alfa"));
        await _presenter.RemoveAsync();
        _presenter.Nodes.Select(n => n.Mud.Name).Should().Equal("Beta", "Gamma");

        await _presenter.ImportAsync();

        var back = _presenter.Nodes.Single(n => n.Mud.Name == "Alfa");
        back.Characters.Select(c => c.Name).Should().BeEquivalentTo("Ana", "Berto", "Carla");
        back.Characters.Should().OnlyContain(c => c.EncryptedPassword == null, "passwords never travel in a file");
        _presenter.SelectedMud!.Name.Should().Be("Beta", "the selection survives the import");
        _presenter.Status.Should().StartWith(Strings.Launcher_StatusImported.Split('{')[0]);
    }

    [Fact]
    public async Task ExportCharacter_NeedsACharacterSelected()
    {
        await SeedAsync();

        await _presenter.ExportCharacterAsync();

        _presenter.CanExportCharacter.Should().BeFalse();
        _prompts.Infos.Should().Equal(Strings.Launcher_SelectCharacterFirst);
    }

    [Fact]
    public async Task ImportCharacterFile_GoesIntoTheMudOfTheSelectedNode()
    {
        var (alfa, beta, _, chars) = await SeedAsync();
        _prompts.SaveFile = _prompts.OpenFile = _db.FilePath("ana.omnimud");
        _presenter.Select(new LauncherSelection(alfa.Id, chars[0].Id));
        await _presenter.ExportCharacterAsync();

        _presenter.Select(new LauncherSelection(beta.Id));
        await _presenter.ImportAsync();

        _presenter.Nodes.Single(n => n.Mud.Id == beta.Id).Characters.Select(c => c.Name).Should().Equal("Ana");
    }
}
