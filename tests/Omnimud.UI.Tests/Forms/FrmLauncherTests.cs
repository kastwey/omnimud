using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Omnimud.Core.Security;
using Omnimud.Core.Session;
using Omnimud.Data.Exchange;
using Omnimud.Data.Repositories;
using Omnimud.UI.Forms;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;
using Omnimud.UI.Tests.Accessibility;
using Omnimud.UI.Tests.Presenters;

namespace Omnimud.UI.Tests.Forms;

public sealed class FrmLauncherTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private readonly ScriptedPrompts _prompts = new();
    private readonly ScriptedLauncherDialogs _dialogs = new();
    private readonly FakeProtector _protector = new();
    private readonly IGameWindowFactory _windows = Substitute.For<IGameWindowFactory>();
    private readonly IOptionRepository _options = Substitute.For<IOptionRepository>();

    /// <summary>Stands in for the game window: off screen and never takes the focus from whoever runs the tests.</summary>
    private sealed class QuietForm : Form
    {
        public QuietForm()
        {
            ShowInTaskbar = false;
            Opacity = 0;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-3000, -3000);
            Size = new Size(10, 10);
        }

        protected override bool ShowWithoutActivation => true;
    }

    public FrmLauncherTests() => Strings.Culture = CultureInfo.GetCultureInfo("es");

    public void Dispose() => _db.Dispose();

    private FrmLauncher Create()
    {
        var form = new FrmLauncher(_db.Muds, _db.Characters, _protector, _db.Rules, _db.Exchange, _options, _prompts,
            () => _windows, _dialogs, new ScriptedConflicts(ImportDecision.Skip));
        UiPump.Wait(form.LoadAsync());
        return form;
    }

    private (int Alfa, int Beta, int Ana, int Berto) Seed()
    {
        var alfa = _db.AddMudAsync("Alfa").GetAwaiter().GetResult();
        var beta = _db.AddMudAsync("Beta").GetAwaiter().GetResult();
        var ana = _db.AddCharacterAsync(alfa.Id, "Ana").GetAwaiter().GetResult();
        var berto = _db.AddCharacterAsync(alfa.Id, "Berto").GetAwaiter().GetResult();
        return (alfa.Id, beta.Id, ana.Id, berto.Id);
    }

    private static T Get<T>(Form form, string name) where T : Control => (T)form.Controls.Find(name, true).Single();

    private static IEnumerable<ToolStripMenuItem> MenuItems(Form form) =>
        form.MainMenuStrip!.Items.OfType<ToolStripMenuItem>().SelectMany(top => top.DropDownItems.OfType<ToolStripMenuItem>());

    // ── Accessibility ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("es", false)]
    [InlineData("en", false)]
    [InlineData("es", true)]
    [InlineData("en", true)]
    public void Window_PassesTheAccessibilityAudit_InEveryLanguage(string culture, bool withData) => Sta.Run(() =>
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);
        if (withData) Seed();
        using var form = Create();

        AccessibilityAudit.Check(form, isDialog: false).Should().BeEmpty();
    });

    [Fact]
    public void Tree_HasLabelWithMnemonic_AccessibleName_AndNoDescription() => Sta.Run(() =>
    {
        using var form = Create();

        Get<Label>(form, "_lblTree").Text.Should().Be("&MUDs y personajes:");
        var tree = Get<TreeView>(form, "_tree");
        tree.AccessibleName.Should().Be("MUDs y personajes");
        tree.AccessibleDescription.Should().BeNullOrEmpty("a description is read aloud on every focus");
        tree.HideSelection.Should().BeFalse();
        tree.TabIndex.Should().Be(Get<Label>(form, "_lblTree").TabIndex + 1);
    });

    [Fact]
    public void Menu_HasFileToolsAndHelp_WithTheRequestedEntries_AndDiscoverableShortcuts() => Sta.Run(() =>
    {
        using var form = Create();

        form.MainMenuStrip!.Items.Cast<ToolStripItem>().Select(i => i.Text).Should().Equal("&Archivo", "&Herramientas", "Ay&uda");
        var texts = MenuItems(form).Select(i => i.Text).ToList();
        texts.Should().Contain(["&Salir", "&Opciones globales...", "&Reglas de mensajes...", "&Importar...", "Exportar &MUD...",
            "Exportar &personaje...", Strings.Menu_HelpManual, Strings.Menu_HelpLua, Strings.Menu_HelpAbout]);

        ToolStripMenuItem Item(string text) => MenuItems(form).Single(i => i.Text == text);
        Item(Strings.Menu_HelpManual).ShortcutKeys.Should().Be(Keys.F1);
        Item("&Importar...").ShortcutKeys.Should().Be(Keys.Control | Keys.I);
        Item("&Editar...").ShortcutKeyDisplayString.Should().Be("F2");
        Item("&Quitar").ShortcutKeyDisplayString.Should().Be("Supr");
        Item("&Conectar").ShortcutKeyDisplayString.Should().Be("Intro");
    });

    [Fact]
    public void TheContainerConstructor_StillWorks_ResolvingWhatIsMissingFromTheProvider() => Sta.Run(() =>
    {
        var services = new ServiceCollection()
            .AddSingleton(_db.Rules).AddSingleton(_db.Exchange).AddSingleton(_options).AddSingleton(_windows)
            .AddSingleton(_db.Muds).AddSingleton(_db.Characters).AddSingleton<IPasswordProtector>(_protector)
            .AddTransient<FrmLauncher>()
            .BuildServiceProvider();

        using var form = services.GetRequiredService<FrmLauncher>();

        form.Should().NotBeNull();
        AccessibilityAudit.Check(form, isDialog: false).Should().BeEmpty();
    });

    [Fact]
    public void TheContainer_PicksTheLongConstructor_OnceIUserPromptsIsRegistered_WithoutAmbiguity() => Sta.Run(() =>
    {
        var services = new ServiceCollection()
            .AddSingleton(_db.Rules).AddSingleton(_db.Exchange).AddSingleton(_options).AddSingleton(_windows)
            .AddSingleton(_db.Muds).AddSingleton(_db.Characters).AddSingleton<IPasswordProtector>(_protector)
            .AddSingleton<IUserPrompts>(_prompts)
            .AddTransient<FrmLauncher>()
            .BuildServiceProvider();

        using var form = services.GetRequiredService<FrmLauncher>();

        form.Should().NotBeNull();
    });

    // ── Tree ───────────────────────────────────────────────────────────────

    [Fact]
    public void EmptyDatabase_OnlyAddMudAndQuickConnectAreEnabled() => Sta.Run(() =>
    {
        using var form = Create();

        Get<TreeView>(form, "_tree").Nodes.Count.Should().Be(0);
        foreach (var name in new[] { "_btnConnect", "_btnAddChar", "_btnEdit", "_btnRemove", "_btnSetDefault" })
            Get<Button>(form, name).Enabled.Should().BeFalse(name);
        Get<Button>(form, "_btnAddMud").Enabled.Should().BeTrue();
        Get<Button>(form, "_btnQuickConnect").Enabled.Should().BeTrue();
        Get<Label>(form, "_lblStatus").Text.Should().Be(string.Format(Strings.Launcher_MudsConfigured, 0));
    });

    [Fact]
    public void Tree_ShowsMudsWithTheirCharacters_Expanded_WithTheFirstSelected() => Sta.Run(() =>
    {
        Seed();
        using var form = Create();
        var tree = Get<TreeView>(form, "_tree");

        tree.Nodes.Cast<TreeNode>().Select(n => n.Text).Should().Equal("Alfa (mud.example.org:4000)", "Beta (mud.example.org:4000)");
        tree.Nodes[0].Nodes.Cast<TreeNode>().Select(n => n.Text).Should().Equal("Ana", "Berto");
        tree.Nodes[0].IsExpanded.Should().BeTrue();
        tree.SelectedNode.Should().BeSameAs(tree.Nodes[0]);
        Get<Button>(form, "_btnSetDefault").Enabled.Should().BeFalse("a MUD is selected, not a character");
    });

    [Fact]
    public void SetDefault_ShowsTheMarkInTheNode_AndKeepsTheNodeSelected() => Sta.Run(() =>
    {
        var ids = Seed();
        using var form = Create();
        form.Presenter.Select(new LauncherSelection(ids.Alfa, ids.Berto));

        UiPump.Wait(form.Presenter.SetDefaultAsync());

        var tree = Get<TreeView>(form, "_tree");
        tree.Nodes[0].Nodes.Cast<TreeNode>().Select(n => n.Text).Should().Equal("Ana", "Berto (predeterminado)");
        tree.SelectedNode!.Text.Should().Be("Berto (predeterminado)");
        Get<Button>(form, "_btnSetDefault").Enabled.Should().BeFalse("it already is the default");
        Get<Label>(form, "_lblStatus").Text.Should().Be(string.Format(Strings.Launcher_StatusDefaultSet, "Berto", "Alfa"));
    });

    [Fact]
    public void AfterAdding_TheNewNodeIsSelected_AndCollapsedMudsStayCollapsed() => Sta.Run(() =>
    {
        var ids = Seed();
        using var form = Create();
        form.Presenter.SetExpanded(ids.Alfa, false);
        form.Presenter.Select(new LauncherSelection(ids.Beta));
        _dialogs.OnEditCharacter = m => { m.Name = "Eva"; m.RememberPassword = false; return true; };

        UiPump.Wait(form.Presenter.AddCharacterAsync());

        var tree = Get<TreeView>(form, "_tree");
        tree.SelectedNode!.Text.Should().Be("Eva");
        tree.SelectedNode.Parent!.Text.Should().StartWith("Beta");
        tree.Nodes[0].IsExpanded.Should().BeFalse("expansion survives the reload");
        tree.Nodes[1].IsExpanded.Should().BeTrue();
    });

    [Fact]
    public void AfterRemoving_TheNeighbourIsSelected() => Sta.Run(() =>
    {
        var ids = Seed();
        using var form = Create();
        form.Presenter.Select(new LauncherSelection(ids.Alfa, ids.Ana));

        UiPump.Wait(form.Presenter.RemoveAsync());

        var tree = Get<TreeView>(form, "_tree");
        tree.SelectedNode!.Text.Should().Be("Berto");
        tree.Nodes[0].Nodes.Count.Should().Be(1);
        Get<Label>(form, "_lblStatus").Text.Should().Be(string.Format(Strings.Launcher_StatusCharacterRemoved, "Ana"));
    });

    [Fact]
    public void AfterEditing_TheSameNodeStaysSelected_WithItsNewText() => Sta.Run(() =>
    {
        var ids = Seed();
        using var form = Create();
        form.Presenter.Select(new LauncherSelection(ids.Beta));
        _dialogs.OnEditMud = m => { m.Name = "Beta 2"; return true; };

        UiPump.Wait(form.Presenter.EditAsync());

        Get<TreeView>(form, "_tree").SelectedNode!.Text.Should().Be("Beta 2 (mud.example.org:4000)");
    });

    // ── Context menu of the tree ───────────────────────────────────────────

    private static List<ToolStripMenuItem> Context(FrmLauncher form, bool emptyArea = false)
    {
        form.FillTreeContextMenu(emptyArea);
        return form.TreeContextMenu.Items.OfType<ToolStripMenuItem>().ToList();
    }

    [Fact]
    public void ContextMenu_OnAMud_OffersTheMudCommands() => Sta.Run(() =>
    {
        var ids = Seed();
        using var form = Create();
        form.Presenter.Select(new LauncherSelection(ids.Alfa));

        var items = Context(form);

        items.Select(i => i.Text).Should().Equal("&Conectar", "&Editar MUD...", "E&liminar MUD", "&Añadir personaje...", "E&xportar MUD...");
        items.Select(i => (LauncherCommand)i.Tag!).Should().Equal(LauncherCommand.Connect, LauncherCommand.Edit, LauncherCommand.Remove,
            LauncherCommand.AddCharacter, LauncherCommand.ExportMud);
        items.Should().OnlyContain(i => i.Enabled);
    });

    [Fact]
    public void ContextMenu_OnACharacter_OffersTheCharacterCommands_AndShowsTheDefaultAsCheckedAndDisabled() => Sta.Run(() =>
    {
        var ids = Seed();
        using var form = Create();
        form.Presenter.Select(new LauncherSelection(ids.Alfa, ids.Ana));

        var items = Context(form);
        items.Select(i => i.Text).Should().Equal("&Conectar", "&Editar personaje...", "E&liminar personaje", "&Marcar como predeterminado", "E&xportar personaje...");
        var setDefault = items.Single(i => (LauncherCommand)i.Tag! == LauncherCommand.SetDefault);
        (setDefault.Enabled, setDefault.Checked).Should().Be((true, false));

        UiPump.Wait(form.Presenter.SetDefaultAsync());

        setDefault = Context(form).Single(i => (LauncherCommand)i.Tag! == LauncherCommand.SetDefault);
        (setDefault.Enabled, setDefault.Checked).Should().Be((false, true), "it already is the default: said, not only shown");
    });

    [Fact]
    public void ContextMenu_WithoutNode_OrOnTheEmptyArea_OffersAddMudAndImport() => Sta.Run(() =>
    {
        using (var empty = Create())
            Context(empty).Select(i => i.Text).Should().Equal("&Añadir MUD...", "&Importar...");

        Seed();
        using var form = Create();
        var items = Context(form, emptyArea: true);
        items.Select(i => (LauncherCommand)i.Tag!).Should().Equal(LauncherCommand.AddMud, LauncherCommand.Import);
        items.Should().OnlyContain(i => i.Enabled);
    });

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void ContextMenu_Mnemonics_AreUniqueWithinEachMenu_InEveryLanguage(string culture) => Sta.Run(() =>
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);
        var ids = Seed();
        using var form = Create();

        var menus = new List<List<ToolStripMenuItem>>();
        form.Presenter.Select(new LauncherSelection(ids.Alfa));
        menus.Add(Context(form));
        form.Presenter.Select(new LauncherSelection(ids.Alfa, ids.Ana));
        menus.Add(Context(form));
        menus.Add(Context(form, emptyArea: true));

        foreach (var menu in menus)
        {
            var mnemonics = menu.Select(i => AccessibilityAudit.Mnemonic(i.Text)).ToList();
            mnemonics.Should().NotContainNulls().And.OnlyHaveUniqueItems();
        }
    });

    [Fact]
    public void EveryContextEntry_RunsTheSameCommandAsItsButton_AndAsTheMainMenu() => Sta.Run(() =>
    {
        var ids = Seed();
        using var form = Create();
        var buttons = form.Controls.OfType<Button>().ToDictionary(b => (LauncherCommand)b.Tag!);
        var mainMenu = MenuItems(form).Where(i => i.Tag is LauncherCommand).ToDictionary(i => (LauncherCommand)i.Tag!);

        buttons.Keys.Should().BeEquivalentTo([LauncherCommand.Connect, LauncherCommand.QuickConnect, LauncherCommand.AddMud,
            LauncherCommand.AddCharacter, LauncherCommand.Edit, LauncherCommand.Remove, LauncherCommand.SetDefault]);
        mainMenu.Keys.Should().BeEquivalentTo(Enum.GetValues<LauncherCommand>(), "every command is reachable from the main menu");

        foreach (var selection in new[] { new LauncherSelection(ids.Alfa), new LauncherSelection(ids.Alfa, ids.Ana) })
        {
            form.Presenter.Select(selection);
            foreach (var item in Context(form))
            {
                var command = (LauncherCommand)item.Tag!;
                mainMenu.Should().ContainKey(command);
                item.Enabled.Should().Be(form.Presenter.CanExecute(command));
                if (buttons.TryGetValue(command, out var button))
                    button.Tag.Should().Be(item.Tag);
            }
        }
    });

    [Fact]
    public void ContextEntry_Button_AndMainMenu_DoTheSame_RemoveAsExample() => Sta.Run(() =>
    {
        foreach (var via in new[] { "context", "menu", "button" })
        {
            var mud = _db.AddMudAsync("Borrar-" + via).GetAwaiter().GetResult();
            using var form = Create();
            form.Presenter.Select(new LauncherSelection(mud.Id));

            switch (via)
            {
                case "context": Context(form).Single(i => (LauncherCommand)i.Tag! == LauncherCommand.Remove).PerformClick(); break;
                case "menu": MenuItems(form).Single(i => i.Tag is LauncherCommand.Remove).PerformClick(); break;
                default: form.Execute((LauncherCommand)Get<Button>(form, "_btnRemove").Tag!); break;
            }
            Application.DoEvents();

            form.Presenter.Nodes.Should().NotContain(n => n.Mud.Id == mud.Id, via);
        }
        _prompts.Confirms.Should().HaveCount(3);
    });

    [Fact]
    public void FromTheKeyboard_TheMenuOpensNextToTheSelectedNode_NotAtTheMouse() => Sta.Run(() =>
    {
        var ids = Seed();
        using var form = Create();
        var tree = Get<TreeView>(form, "_tree");
        _ = tree.Handle;
        form.Presenter.Select(new LauncherSelection(ids.Alfa, ids.Berto));
        UiPump.Wait(form.LoadAsync());
        var node = tree.SelectedNode!;
        node.Text.Should().Be("Berto");

        var location = form.KeyboardMenuLocation();

        node.Bounds.IsEmpty.Should().BeFalse();
        location.Should().Be(new Point(node.Bounds.Left, node.Bounds.Bottom));
        tree.ContextMenuStrip.Should().BeNull("the tree asks for the menu itself, so WinForms never centres it or puts it at the mouse");
    });

    [Fact]
    public void RightClick_SelectsTheNodeUnderTheMouseFirst_AndTheEmptyAreaGetsTheGeneralMenu() => Sta.Run(() =>
    {
        Seed();
        using var form = Create();
        var tree = Get<TreeView>(form, "_tree");
        _ = tree.Handle;
        UiPump.Wait(form.LoadAsync());
        var beta = tree.Nodes[1];
        tree.SelectedNode.Should().NotBeSameAs(beta);

        var onEmptyArea = form.PrepareMouseMenu(new Point(beta.Bounds.Left + 2, beta.Bounds.Top + 2));

        onEmptyArea.Should().BeFalse();
        tree.SelectedNode.Should().BeSameAs(beta);
        form.Presenter.SelectedMud!.Name.Should().Be("Beta");
        Context(form).Select(i => (LauncherCommand)i.Tag!).Should().Contain(LauncherCommand.ExportMud);

        form.PrepareMouseMenu(new Point(5, tree.ClientSize.Height - 5)).Should().BeTrue();
        tree.SelectedNode.Should().BeSameAs(beta, "clicking the empty area does not lose the selection");
    });

    // ── Connect ────────────────────────────────────────────────────────────

    [Fact]
    public void Connect_CreatesTheGameWindowThroughTheFactory_AndShowsTheStatus() => Sta.Run(() =>
    {
        var ids = Seed();
        using var window = new QuietForm();
        SessionProfile? asked = null;
        _windows.Create(Arg.Do<SessionProfile>(p => asked = p)).Returns(window);
        using var form = Create();
        form.Presenter.Select(new LauncherSelection(ids.Alfa, ids.Ana));

        form.Presenter.Connect();

        asked.Should().BeEquivalentTo(new { Title = "Ana - Alfa", CharacterId = (int?)ids.Ana, MudId = (int?)ids.Alfa });
        Get<Label>(form, "_lblStatus").Text.Should().Be(string.Format(Strings.Launcher_ConnectingTo, "Ana - Alfa"));
        window.Close();
    });

    [Fact]
    public void QuickConnect_UsesTheWholeProfileOfTheDialog() => Sta.Run(() =>
    {
        using var window = new QuietForm();
        SessionProfile? asked = null;
        _windows.Create(Arg.Do<SessionProfile>(p => asked = p)).Returns(window);
        var profile = new SessionProfile { Title = "x:992", Host = "x", Port = 992, UseTls = true, ValidateCertificate = false, Encoding = "windows-1252" };
        _dialogs.QuickConnectProfile = profile;
        using var form = Create();

        form.Presenter.QuickConnect();

        asked.Should().Be(profile);
        window.Close();
    });
}
