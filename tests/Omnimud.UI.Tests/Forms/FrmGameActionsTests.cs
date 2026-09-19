using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Omnimud.Core.Actions;
using Omnimud.Core.Options;
using Omnimud.Core.Session;
using Omnimud.Core.Sound;
using Omnimud.UI.Controls;
using Omnimud.UI.Forms;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;
using Omnimud.UI.Services.Accessibility;
using Omnimud.UI.Tests.Accessibility;

namespace Omnimud.UI.Tests.Forms;

/// <summary>The "Actions" menu fed by the MUD (GMCP) and the context menu of the Received and Messages boxes.</summary>
public sealed class FrmGameActionsTests
{
    private const string Inventory = """
        { "items": [
            { "id": "espada", "short": "una espada larga", "actions": [
                { "action": "dejar", "label": "Dejar", "cmd": "dejar espada" },
                { "action": "examinar", "label": "Examinar", "cmd": "examinar espada" } ] },
            { "id": "pocion", "short": "poción (2)", "children": [
                { "id": "pocion", "short": "poción (1)", "actions": [ { "action": "beber", "label": "Beber", "cmd": "beber pocion 1" } ] },
                { "id": "pocion", "short": "poción (2)", "actions": [ { "action": "beber", "label": "Beber", "cmd": "beber pocion 2" } ] } ] },
            { "id": "pico", "short": "pico & pala", "actions": [ { "action": "usar", "label": "Usar & guardar", "cmd": "usar pico" } ] },
            { "id": "piedra", "short": "una piedra" }
        ] }
        """;
    private const string Room = """{ "id": "/room/plaza", "short": "Plaza", "exits": [ "norte", "sur" ], "items": [] }""";

    private readonly IMudSession _session = Substitute.For<IMudSession>();
    private readonly ISessionSound _sound = Substitute.For<ISessionSound>();
    private readonly ISessionDialogs _dialogs = Substitute.For<ISessionDialogs>();
    private readonly IAnnouncer _announcer = Substitute.For<IAnnouncer>();
    private readonly FakeTimeProvider _time = new();
    private readonly ActionMenuState _state = new();

    public FrmGameActionsTests()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("es");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es");
        _session.Profile.Returns(new SessionProfile
        {
            Title = "Reinos", Host = "mud.example.org", Port = 23,
            MudId = 1, MudName = "Reinos", CharacterId = 7, CharacterName = "Aldara",
            SaveCommand = "salvar", QuitCommand = "abandonar",
        });
        _session.Options.Returns(OmnimudOptions.Default with { ConfirmBeforeExit = false });
        _session.State.Returns(SessionState.Connected);
        _session.Messages.Returns(new List<SessionMessage>());
        _session.History.Returns(new List<string>());
        _session.ActionMenu.Returns(_ => _state.Current);
        _session.ExecuteActionAsync(Arg.Any<ActionMenuNode>()).Returns(Task.CompletedTask);
    }

    private FrmGame Create() => new(_session, _sound, _dialogs, _time, _announcer);

    /// <summary>What the session does when the package arrives: new snapshot, then the event.</summary>
    private void MudSends(string package, string payload)
    {
        _state.Update(package, payload).Should().BeTrue();
        _session.ActionMenuChanged += Raise.Event<Action>();
    }

    private void MudDisconnects()
    {
        _state.Clear();
        _session.ActionMenuChanged += Raise.Event<Action>();
    }

    private static T Get<T>(Form form, string name) where T : Control =>
        (T)form.Controls.Find(name, searchAllChildren: true).Single();

    private static ToolStripMenuItem ActionsMenu(Form form) => (ToolStripMenuItem)form.MainMenuStrip!.Items["_miActions"]!;

    /// <summary>What WinForms does right before showing the drop-down, without showing anything.</summary>
    private static void RaiseDropDownOpening(ToolStripMenuItem item) =>
        typeof(ToolStripDropDownItem).GetMethod("OnDropDownShow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(item, [EventArgs.Empty]);

    private static ToolStripMenuItem[] Children(ToolStripItem item) =>
        ((ToolStripMenuItem)item).DropDownItems.OfType<ToolStripMenuItem>().ToArray();

    private static ToolStripMenuItem Child(ToolStripItem item, string text) => Children(item).Single(i => i.Text == text);

    // ── Menu bar ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("es", "&Acciones", "&Partida", "&Edición")]
    [InlineData("en", "&Actions", "&Game", "&Edit")]
    public void TheBar_StartsWithActions_ThenGame_ThenTheRest(string culture, string actions, string game, string edit) => Sta.Run(() =>
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);
        using var form = Create();

        form.MainMenuStrip!.Items.Cast<ToolStripItem>().Take(3).Select(i => i.Text).Should().Equal(actions, game, edit);
        form.MainMenuStrip.Items.Count.Should().Be(6);
    });

    [Theory]
    [InlineData("es", "&Salvar partida", "&Abandonar la partida", "&Reconectar", "&Cerrar ventana")]
    [InlineData("en", "&Save game", "&Quit the game", "&Reconnect", "&Close window")]
    public void WhatUsedToBeActions_IsNowTheGameMenu_WithTheSameCommandsAndShortcuts(string culture, string save, string quit, string reconnect, string close) => Sta.Run(() =>
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);
        using var form = Create();

        var game = Children(form.MainMenuStrip!.Items["_miGame"]!);

        game.Select(i => i.Text).Should().Equal(save, quit, reconnect, close);
        game[0].ShortcutKeys.Should().Be(Keys.F3);
        game[1].ShortcutKeys.Should().Be(Keys.F4);
        game[0].PerformClick();
        _session.Received(1).SendSaveCommandAsync();
    });

    [Fact]
    public void WithoutActions_TheMenuIsHidden() => Sta.Run(() =>
    {
        using var form = Create();

        ActionsMenu(form).Available.Should().BeFalse();
    });

    [Fact]
    public void WhenTheMudSendsActions_TheMenuAppears_AndWhenTheyAreGone_ItHidesAgain() => Sta.Run(() =>
    {
        using var form = Create();

        MudSends("Room.Info", Room);
        ActionsMenu(form).Available.Should().BeTrue();

        MudSends("Room.Info", """{ "exits": [] }""");
        ActionsMenu(form).Available.Should().BeFalse();

        MudSends("Char.Inventory", Inventory);
        ActionsMenu(form).Available.Should().BeTrue();

        MudDisconnects();
        ActionsMenu(form).Available.Should().BeFalse();
    });

    [Fact]
    public void ASessionThatAlreadyHasActions_ShowsTheMenuFromTheStart() => Sta.Run(() =>
    {
        _state.Update("Room.Info", Room);

        using var form = Create();

        ActionsMenu(form).Available.Should().BeTrue();
    });

    [Fact]
    public void TheMenuAppearingOrDisappearing_IsNeverAnnounced() => Sta.Run(() =>
    {
        using var form = Create();

        MudSends("Char.Inventory", Inventory);
        MudSends("Room.Info", Room);
        MudDisconnects();
        _time.Advance(TimeSpan.FromSeconds(2));

        _announcer.DidNotReceiveWithAnyArgs().Announce(default!, default);
    });

    [Fact]
    public void AVisibleMenu_AlwaysHasSomethingInside_SoItsMnemonicOpensIt() => Sta.Run(() =>
    {
        using var form = Create();

        MudSends("Room.Info", Room);

        ActionsMenu(form).HasDropDownItems.Should().BeTrue("WinForms does not open a top-level item without children, so DropDownOpening would never run");
    });

    // ── Content ────────────────────────────────────────────────────────────

    [Fact]
    public void Opening_BuildsInventoryAndExits_WithSubmenusAndGroups() => Sta.Run(() =>
    {
        using var form = Create();
        MudSends("Char.Inventory", Inventory);
        MudSends("Room.Info", Room);
        var menu = ActionsMenu(form);

        RaiseDropDownOpening(menu);

        Children(menu).Select(i => i.Text).Should().Equal("Inventario", "Salidas");
        var inventory = Child(menu, "Inventario");
        Children(inventory).Select(i => i.Text).Should().Equal("una espada larga", "poción (2)", "pico && pala", "una piedra");
        Children(Child(inventory, "una espada larga")).Select(i => i.Text).Should().Equal("Dejar", "Examinar");
        var potions = Child(inventory, "poción (2)");
        Children(potions).Select(i => i.Text).Should().Equal("poción (1)", "poción (2)");
        Children(Child(potions, "poción (2)")).Select(i => i.Text).Should().Equal("Beber");
        Children(Child(menu, "Salidas")).Select(i => i.Text).Should().Equal("norte", "sur");
    });

    [Fact]
    public void AnItemWithoutActions_IsListedButDisabled() => Sta.Run(() =>
    {
        using var form = Create();
        MudSends("Char.Inventory", Inventory);
        form.FillActionsMenu();

        var stone = Child(Child(ActionsMenu(form), "Inventario"), "una piedra");

        stone.Enabled.Should().BeFalse();
        stone.HasDropDownItems.Should().BeFalse();
    });

    [Fact]
    public void Ampersands_FromTheServer_AreShownLiterally_NeverAsMnemonics() => Sta.Run(() =>
    {
        using var form = Create();
        MudSends("Char.Inventory", Inventory);
        form.FillActionsMenu();

        var pick = Child(Child(ActionsMenu(form), "Inventario"), "pico && pala");

        AccessibilityAudit.Mnemonic(pick.Text).Should().BeNull();
        Children(pick).Single().Text.Should().Be("Usar && guardar");
    });

    [Fact]
    public void TheMenuIsOnlyRebuiltWhenItOpens_NeverWhenAPackageArrives() => Sta.Run(() =>
    {
        using var form = Create();
        MudSends("Room.Info", Room);
        var menu = ActionsMenu(form);
        RaiseDropDownOpening(menu);
        var shown = menu.DropDownItems.Cast<ToolStripItem>().ToArray();
        var disposed = 0;
        foreach (var item in shown) item.Disposed += (_, _) => disposed++;

        MudSends("Room.Info", """{ "exits": [ "abajo" ] }""");
        MudSends("Char.Inventory", Inventory);

        menu.DropDownItems.Cast<ToolStripItem>().Should().Equal(shown, "what the user has under the cursor does not move");
        Children(Child(menu, "Salidas")).Select(i => i.Text).Should().Equal("norte", "sur");

        RaiseDropDownOpening(menu);

        Children(menu).Select(i => i.Text).Should().Equal("Inventario", "Salidas");
        Children(Child(menu, "Salidas")).Select(i => i.Text).Should().Equal("abajo");
        disposed.Should().Be(shown.Length, "the previous items are not leaked");
    });

    // ── Choosing ───────────────────────────────────────────────────────────

    [Fact]
    public void ChoosingAnAction_ExecutesExactlyThatNode_AndTheFocusGoesBackToTheInputBox() => Sta.Run(() =>
    {
        using var form = Create();
        var terminal = Get<AnsiTerminalBox>(form, "_terminal");
        _ = terminal.Handle;
        form.ActiveControl = terminal;
        MudSends("Char.Inventory", Inventory);
        MudSends("Room.Info", Room);
        form.FillActionsMenu();
        var potions = Child(Child(ActionsMenu(form), "Inventario"), "poción (2)");

        Children(Child(potions, "poción (2)")).Single().PerformClick();
        Application.DoEvents();

        _session.Received(1).ExecuteActionAsync(Arg.Is<ActionMenuNode>(n => n.Command == "beber pocion 2" && n.Label == "Beber"));
        _session.Received(1).ExecuteActionAsync(Arg.Any<ActionMenuNode>());
        _session.DidNotReceiveWithAnyArgs().SubmitInputAsync(default!);
        form.ActiveControl.Should().BeSameAs(Get<TextBox>(form, "_txtInput"));
    });

    [Fact]
    public void ChoosingAnExit_SendsThatExit() => Sta.Run(() =>
    {
        using var form = Create();
        MudSends("Room.Info", Room);
        form.FillActionsMenu();

        Child(Child(ActionsMenu(form), "Salidas"), "sur").PerformClick();

        _session.Received(1).ExecuteActionAsync(Arg.Is<ActionMenuNode>(n => n.Command == "sur"));
    });

    // ── Accessibility ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("es", false)]
    [InlineData("es", true)]
    [InlineData("en", false)]
    [InlineData("en", true)]
    public void Window_PassesTheAccessibilityAudit_WithAndWithoutActions(string culture, bool withActions) => Sta.Run(() =>
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        using var form = Create();
        if (withActions)
        {
            MudSends("Char.Inventory", Inventory);
            MudSends("Room.Info", Room);
            AccessibilityAudit.Check(form, isDialog: false).Should().BeEmpty("the menu is visible but not opened yet");
            form.FillActionsMenu();
            Children(ActionsMenu(form)).Should().HaveCount(2);
        }

        AccessibilityAudit.Check(form, isDialog: false).Should().BeEmpty();
    });

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void TopLevelMnemonics_AreUnique_AmongMenusLabelsAndButtons(string culture) => Sta.Run(() =>
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);
        using var form = Create();
        var texts = form.MainMenuStrip!.Items.Cast<ToolStripItem>().Select(i => i.Text)
            .Concat([Strings.Client_InputLabel, Strings.Client_OutputLabel, Strings.Client_MessagesLabel, Strings.Client_BtnReconnect,
                Strings.Client_BtnCancel, Strings.Client_BtnDisconnect]);

        var mnemonics = texts.Select(AccessibilityAudit.Mnemonic).ToArray();

        mnemonics.Should().NotContainNulls();
        mnemonics.Should().OnlyHaveUniqueItems();
    });

    // ── Context menu of the boxes ──────────────────────────────────────────

    private static string[] Texts(ContextMenuStrip menu) =>
        menu.Items.Cast<ToolStripItem>().Select(i => i is ToolStripSeparator ? "-" : i.Text ?? string.Empty).ToArray();

    [Fact]
    public void BoxMenu_OfReceived_HasTheEditCommands_ThenTheSameSectionsAsTheActionsMenu() => Sta.Run(() =>
    {
        using var form = Create();
        MudSends("Char.Inventory", Inventory);
        MudSends("Room.Info", Room);

        form.PrepareBoxMenu(Get<AnsiTerminalBox>(form, "_terminal"));

        Texts(form.BoxMenu).Should().Equal("&Copiar", "Seleccionar &todo", "&Buscar...", "Buscar &siguiente", "-", "Inventario", "Salidas");
        form.FillActionsMenu();
        var fromBar = Children(Child(ActionsMenu(form), "Inventario")).Select(i => i.Text);
        Children(form.BoxMenu.Items[5]).Select(i => i.Text).Should().Equal(fromBar);
    });

    [Fact]
    public void BoxMenu_WithoutActions_HasOnlyTheEditCommands_AndNoSeparator() => Sta.Run(() =>
    {
        using var form = Create();

        form.PrepareBoxMenu(Get<AnsiTerminalBox>(form, "_terminal"));

        Texts(form.BoxMenu).Should().Equal("&Copiar", "Seleccionar &todo", "&Buscar...", "Buscar &siguiente");
    });

    [Fact]
    public void BoxMenu_OfMessages_NeverHasActions() => Sta.Run(() =>
    {
        using var form = Create();
        MudSends("Room.Info", Room);

        form.PrepareBoxMenu(Get<AnsiTerminalBox>(form, "_rtbMessages"));

        Texts(form.BoxMenu).Should().Equal("&Copiar", "Seleccionar &todo", "&Buscar...", "Buscar &siguiente");
    });

    [Fact]
    public void BoxMenu_IsRebuiltEveryTime_WithoutPilingUp() => Sta.Run(() =>
    {
        using var form = Create();
        var terminal = Get<AnsiTerminalBox>(form, "_terminal");
        MudSends("Room.Info", Room);

        form.PrepareBoxMenu(terminal);
        form.PrepareBoxMenu(terminal);
        Texts(form.BoxMenu).Should().Equal("&Copiar", "Seleccionar &todo", "&Buscar...", "Buscar &siguiente", "-", "Salidas");

        MudDisconnects();
        form.PrepareBoxMenu(terminal);
        Texts(form.BoxMenu).Should().HaveCount(4);
    });

    [Fact]
    public void BoxMenu_EditCommands_AreEnabledLikeTheEditMenu() => Sta.Run(() =>
    {
        using var form = Create();
        var terminal = Get<AnsiTerminalBox>(form, "_terminal");
        terminal.AppendPlainLine("Estás en una plaza.");
        var items = form.BoxMenu.Items.OfType<ToolStripMenuItem>().ToArray();
        var edit = form.MainMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "&Edición").DropDownItems.OfType<ToolStripMenuItem>().ToArray();

        terminal.Select(0, 0);
        form.PrepareBoxMenu(terminal);
        items.Select(i => i.Enabled).Should().Equal([false, true, true, false], "nothing selected and nothing searched yet");

        terminal.Select(0, 5);
        form.PrepareBoxMenu(terminal);
        items.Select(i => i.Enabled).Should().Equal(true, true, true, false);

        foreach (var item in items)
            item.Enabled.Should().Be(edit.Single(e => e.Text == item.Text).Enabled, $"'{item.Text}' follows the Edit menu");
    });

    [Fact]
    public void BoxMenu_ShowsTheShortcuts_ButDoesNotOwnThem() => Sta.Run(() =>
    {
        using var form = Create();
        var converter = TypeDescriptor.GetConverter(typeof(Keys));

        var items = form.BoxMenu.Items.OfType<ToolStripMenuItem>().ToArray();

        items.Should().OnlyContain(i => i.ShortcutKeys == Keys.None, "the keys belong to the Edit menu; twice would run the command twice");
        items.Select(i => i.ShortcutKeyDisplayString).Should().Equal(
            converter.ConvertToString(Keys.Control | Keys.C), converter.ConvertToString(Keys.Control | Keys.E),
            converter.ConvertToString(Keys.Control | Keys.B), converter.ConvertToString(Keys.Control | Keys.S));
    });

    [Fact]
    public void BoxMenu_Copy_CopiesTheSelectionOfTheBoxItWasOpenedOn() => Sta.Run(() =>
    {
        using var form = Create();
        var terminal = Get<AnsiTerminalBox>(form, "_terminal");
        terminal.AppendPlainLine("Estás en una plaza.");
        terminal.Select(0, 5);
        form.ActiveControl = Get<TextBox>(form, "_txtInput");

        form.PrepareBoxMenu(terminal);

        form.ActiveControl.Should().BeSameAs(terminal, "the edit commands act on the active box");
        form.BoxMenu.Items.OfType<ToolStripMenuItem>().First().Enabled.Should().BeTrue();
    });

    [Fact]
    public void BoxMenu_ChoosingAnAction_ExecutesIt() => Sta.Run(() =>
    {
        using var form = Create();
        MudSends("Room.Info", Room);
        form.PrepareBoxMenu(Get<AnsiTerminalBox>(form, "_terminal"));

        Child(form.BoxMenu.Items[5], "norte").PerformClick();

        _session.Received(1).ExecuteActionAsync(Arg.Is<ActionMenuNode>(n => n.Command == "norte"));
    });

    [Theory]
    [InlineData("_terminal", true)]
    [InlineData("_rtbMessages", false)]
    public void AskedFromTheKeyboard_TheBoxMenuOpensUnderTheCaret_NotAtTheMouse(string boxName, bool hasActions) => Sta.Run(() =>
    {
        using var form = Create();
        var box = Get<AnsiTerminalBox>(form, boxName);
        _ = box.Handle;
        box.AppendPlainLine("primera línea");
        box.AppendPlainLine("segunda línea");
        box.Select(16, 0);
        MudSends("Room.Info", Room);
        Point? openedAt = null;
        Control? source = null;
        string[] texts = [];
        // Cancelled right after it is built: nothing is ever painted on the user's screen.
        form.BoxMenu.Opening += (_, e) =>
        {
            openedAt = form.BoxMenu.Location;
            source = form.BoxMenu.SourceControl;
            texts = Texts(form.BoxMenu);
            e.Cancel = true;
        };

        box.RequestContextMenu(null);   // what the Applications key and Shift+F10 end up as

        source.Should().BeSameAs(box);
        openedAt.Should().Be(box.PointToScreen(box.KeyboardMenuLocation()));
        texts.Should().HaveCount(hasActions ? 6 : 4);
        form.BoxMenu.Visible.Should().BeFalse();
    });

    [Fact]
    public void RightClick_OpensTheBoxMenuWhereTheMouseIs() => Sta.Run(() =>
    {
        using var form = Create();
        var terminal = Get<AnsiTerminalBox>(form, "_terminal");
        _ = terminal.Handle;
        Point? openedAt = null;
        form.BoxMenu.Opening += (_, e) => { openedAt = form.BoxMenu.Location; e.Cancel = true; };

        terminal.RequestContextMenu(new Point(40, 30));

        openedAt.Should().Be(terminal.PointToScreen(new Point(40, 30)));
    });

    [Fact]
    public void TheBoxes_AskForTheMenuThemselves_SoWinFormsNeverCentresIt() => Sta.Run(() =>
    {
        using var form = Create();

        Get<AnsiTerminalBox>(form, "_terminal").ContextMenuStrip.Should().BeNull();
        Get<AnsiTerminalBox>(form, "_rtbMessages").ContextMenuStrip.Should().BeNull();
    });
}
