using System.Text;
using FluentAssertions;
using Omnimud.Core.Actions;
using Omnimud.Core.Aliases;
using Omnimud.Core.Paths;
using Omnimud.Core.Session;
using Omnimud.Core.Tests.Actions;

namespace Omnimud.Core.Tests.Session;

/// <summary>GMCP Char.Inventory / Room.Info → <see cref="IMudSession.ActionMenu"/>, and what choosing an action does.</summary>
public sealed class MudSessionActionMenuTests : IAsyncDisposable
{
    private const byte Iac = 255, Will = 251, Do = 253, Sb = 250, Se = 240, Gmcp = 201;
    private const string Inventory = CharInventoryTranslatorTests.RealPayload;
    private const string Room = RoomInfoTranslatorTests.RealPayload;

    private readonly SessionHarness _h = new();
    private int _changes;

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private async Task StartAsync()
    {
        await _h.StartAsync();
        _h.Session.ActionMenuChanged += () => Interlocked.Increment(ref _changes);
    }

    private Task GmcpAsync(string package, string payload)
        => _h.ReceiveBytesAsync([Iac, Sb, Gmcp, .. Encoding.UTF8.GetBytes(package + " " + payload), Iac, Se]);

    private async Task ChooseAsync(ActionMenuNode node)
    {
        await _h.Session.ExecuteActionAsync(node);
        await _h.Session.WhenIdleAsync();
    }

    private ActionMenuNode Section(string label) => _h.Session.ActionMenu.Sections.Single(s => s.Label == label);

    // ── Packages → menu ────────────────────────────────────────────────────

    [Fact]
    public async Task BeforeAnyPackage_TheMenuIsEmpty_NeverNull()
    {
        await StartAsync();

        _h.Session.ActionMenu.Should().NotBeNull();
        _h.Session.ActionMenu.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task CharInventoryAndRoomInfo_BuildTheMenu_AndRaiseTheEvent()
    {
        await StartAsync();

        await GmcpAsync("Char.Inventory", Inventory);
        _changes.Should().Be(1);
        await GmcpAsync("Room.Info", Room);

        _changes.Should().Be(2);
        _h.Session.ActionMenu.Sections.Select(s => s.Label).Should().Equal("Inventario", "Salidas");
        Section("Inventario").Children[1].Children[2].Children.Single().Command.Should().Be("beber pocion 3");
        Section("Salidas").Children.Select(n => n.Command).Should().Equal("norte", "sur", "arriba");
    }

    [Fact]
    public async Task PackageNames_AreNotCaseSensitive()
    {
        await StartAsync();

        await GmcpAsync("char.inventory", Inventory);
        await GmcpAsync("ROOM.INFO", Room);

        _h.Session.ActionMenu.Sections.Should().HaveCount(2);
    }

    [Fact]
    public async Task ThePackages_ShowNothing_SayNothing_AndAreNotMessages()
    {
        await StartAsync();

        await GmcpAsync("Char.Inventory", Inventory);
        await GmcpAsync("Room.Info", Room);

        _h.Lines.Should().BeEmpty();
        _h.Spoken.Should().BeEmpty("a menu that changes must never interrupt the reading of the game");
        _h.AddedMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task ANewPackage_ReplacesThePreviousOne()
    {
        await StartAsync();
        await GmcpAsync("Room.Info", Room);

        await GmcpAsync("Room.Info", """{ "exits": [ "abajo" ] }""");

        Section("Salidas").Children.Select(n => n.Command).Should().Equal("abajo");
    }

    [Theory]
    [InlineData("Char.Inventory", "{ esto no es json")]
    [InlineData("Char.Inventory", """{ "items": "muchos" }""")]
    [InlineData("Room.Info", "[1,2,3]")]
    [InlineData("Char.Vitals", """{ "hp": 1 }""")]
    public async Task BadOrUnknownPackages_AreIgnored_WithoutEventOrError(string package, string payload)
    {
        await StartAsync();
        await GmcpAsync("Room.Info", Room);
        var before = _h.Session.ActionMenu;

        await GmcpAsync(package, payload);

        _h.Session.ActionMenu.Should().BeSameAs(before);
        _changes.Should().Be(1);
        _h.SystemLines.Should().BeEmpty();
    }

    [Fact]
    public async Task ChannelMessages_StillWork_NextToTheNewPackages()
    {
        await StartAsync();

        await GmcpAsync("Room.Info", Room);
        await GmcpAsync("Comm.Channel.Text", """{ "channel": "chat", "talker": "Bob", "text": "hola" }""");

        _h.AddedMessages.Single().Text.Should().Be("[chat] Bob: hola");
        _changes.Should().Be(1);
    }

    [Fact]
    public async Task TheSnapshot_IsImmutable_AndSafeToKeep()
    {
        await StartAsync();
        await GmcpAsync("Room.Info", Room);
        var snapshot = _h.Session.ActionMenu;

        await GmcpAsync("Room.Info", """{ "exits": [] }""");

        snapshot.Sections.Single().Children.Should().HaveCount(3);
        _h.Session.ActionMenu.IsEmpty.Should().BeTrue();
    }

    // ── Negotiation ────────────────────────────────────────────────────────

    [Fact]
    public async Task WillGmcp_AnnouncesChannelsInventoryAndRoom()
    {
        await StartAsync();

        await _h.ReceiveBytesAsync(Iac, Will, Gmcp);

        _h.SentTelnet[0].Should().Equal(Iac, Do, Gmcp);
        var packets = _h.SentTelnet.Skip(1).Select(p => Encoding.UTF8.GetString(p[3..^2])).ToArray();
        packets.Should().Contain("Core.Supports.Set [\"Comm.Channel 1\",\"Char.Inventory 1\",\"Room.Info 1\"]");
        packets.Should().Contain(p => p.StartsWith("Core.Hello "));
    }

    // ── Choosing an action ─────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAction_SendsTheCommand_AsOneLine()
    {
        await StartAsync();
        await GmcpAsync("Char.Inventory", Inventory);

        await ChooseAsync(Section("Inventario").Children[0].Children[1]);

        _h.SentLines.Should().Equal("examinar espada");
    }

    [Fact]
    public async Task ExecuteAction_GoesThroughAliases_LikeTypedText()
    {
        _h.Store.Aliases.Add(new AliasDefinition("norte", "abrir puerta norte"));
        await StartAsync();
        await GmcpAsync("Room.Info", Room);

        await ChooseAsync(Section("Salidas").Children[0]);

        _h.SentLines.Should().Equal("abrir puerta norte");
    }

    [Fact]
    public async Task ExecuteAction_NeverEntersTheHistory_NorBecomesTheLastCommand()
    {
        await StartAsync();
        await GmcpAsync("Room.Info", Room);
        await _h.SubmitAsync("mirar");
        _h.Connection.ClearSent();

        await ChooseAsync(Section("Salidas").Children[1]);
        await _h.SubmitAsync(string.Empty);   // Enter on an empty box repeats the last typed command

        _h.Session.History.Should().Equal("mirar");
        _h.SentLines.Should().Equal("sur", "mirar");
    }

    [Fact]
    public async Task NodesWithoutCommand_SendNothing()
    {
        await StartAsync();
        await GmcpAsync("Char.Inventory", """{ "items": [ { "short": "piedra" }, { "short": "pan", "actions": [ { "label": "Comer", "cmd": "comer pan" } ] } ] }""");
        var section = Section("Inventario");

        await ChooseAsync(section);               // a section
        await ChooseAsync(section.Children[1]);   // a submenu
        await ChooseAsync(section.Children[0]);   // information

        section.Children[0].IsAction.Should().BeFalse();
        _h.SentLines.Should().BeEmpty();
    }

    [Theory]
    [InlineData("calias norte abandonar")]
    [InlineData("uncalias m")]
    [InlineData("-triggers")]
    [InlineData("-trigger saludo")]
    [InlineData("cls")]
    [InlineData("callate")]
    [InlineData("paths iniciar")]
    [InlineData("triggers")]
    public async Task AnAction_CanOnlySendTextToTheMud_TheClientsOwnCommandsAreNotObeyed(string command)
    {
        _h.Store.Aliases.Add(new AliasDefinition("m", "mirar"));
        _h.Store.Triggers.Add(SessionHarness.Trigger("te saluda", "saludar", name: "saludo"));
        await StartAsync();

        await ChooseAsync(ActionMenuNode.Action("Parece inofensivo", command)!);

        _h.SentLines.Should().Equal([command], "the text goes to the MUD as it is");
        _h.Store.Aliases.Select(a => a.Command).Should().Equal("m");
        _h.Store.TriggerWrites.Should().BeEmpty();
        _h.Session.TriggersEnabled.Should().BeTrue();
        _h.Session.SilentMode.Should().BeFalse();
        _h.Session.IsRecordingPath.Should().BeFalse();
        _h.Clears.Should().Be(0);
        _h.Windows.Should().BeEmpty();
        _h.SystemLines.Should().BeEmpty();
    }

    [Fact]
    public async Task ClientCommandsHiddenBehindTheConcatenationCharacter_AreNotObeyedEither()
    {
        _h.SetOptions(o => o with { UseConcatChar = true, ConcatChar = ';' });
        await StartAsync();

        await ChooseAsync(ActionMenuNode.Action("Ir al norte", "norte;calias sur abandonar;cls")!);

        _h.SentLines.Should().Equal("norte", "calias sur abandonar", "cls");
        _h.Store.Aliases.Should().BeEmpty();
        _h.Clears.Should().Be(0);
    }

    [Fact]
    public async Task WhatBelongsToTheUser_StillApplies_RepetitionAndPathRecording()
    {
        _h.Store.Directions.AddRange([new DirectionEntry("norte", 'n', "sur"), new DirectionEntry("sur", 's', "norte")]);
        _h.SetOptions(o => o with { UseRepeatChar = true, RepeatChar = '#' });
        await StartAsync();
        await _h.SubmitAsync("paths iniciar");
        _h.ClearOutput();

        await ChooseAsync(ActionMenuNode.Action("norte", "norte")!);
        await ChooseAsync(ActionMenuNode.Action("Llamar dos veces", "2#llamar")!);
        await _h.SubmitAsync("paths grabado");

        _h.SentLines.Should().Equal("norte", "llamar", "llamar");
        _h.UiSounds.Should().Contain("pop", "walking through the menu while recording a path records the step");
        _h.SystemLines.Should().EndWith("n");
    }

    [Fact]
    public async Task TheSameCommands_TypedByTheUser_StillWork()
    {
        await StartAsync();

        await _h.SubmitAsync("cls");

        _h.Clears.Should().Be(1);
        _h.SentLines.Should().BeEmpty();
    }

    [Fact]
    public async Task ACommandWithLineBreaks_LeavesAsOneSingleLine()
    {
        await StartAsync();
        await GmcpAsync("Room.Info", """{ "exits": [ "norte\nabandonar\r\nborrar" ] }""");

        await ChooseAsync(Section("Salidas").Children.Single());

        _h.SentLines.Should().Equal("norte abandonar borrar");
        _h.Connection.SentPackets.Single().Count(b => b == (byte)'\n').Should().Be(1, "only the line terminator");
    }

    [Fact]
    public async Task Offline_AnActionSendsNothing_AndTheUserIsTold()
    {
        await _h.StartAsync(connect: false);
        _h.Session.EnterOfflineMode();

        await ChooseAsync(ActionMenuNode.Action("Norte", "norte")!);

        _h.SentLines.Should().BeEmpty();
        _h.SystemLines.Should().ContainSingle();
    }

    // ── Disconnection ──────────────────────────────────────────────────────

    [Fact]
    public async Task WhenTheServerCloses_TheMenuIsEmptied_AndTheEventRaised()
    {
        await StartAsync();
        await GmcpAsync("Char.Inventory", Inventory);
        await GmcpAsync("Room.Info", Room);

        await _h.Connection.RaiseServerClosed();
        await _h.Session.WhenIdleAsync();

        _h.Session.ActionMenu.IsEmpty.Should().BeTrue();
        _changes.Should().Be(3);
    }

    [Fact]
    public async Task Reconnecting_StartsWithAnEmptyMenu_UntilTheMudSendsItAgain()
    {
        await StartAsync();
        await GmcpAsync("Room.Info", Room);
        await _h.Connection.RaiseServerClosed();
        await _h.Session.WhenIdleAsync();

        await _h.Session.ConnectAsync();
        await _h.Session.WhenIdleAsync();

        _h.Session.ActionMenu.IsEmpty.Should().BeTrue();
        await GmcpAsync("Room.Info", Room);
        Section("Salidas").Children.Should().HaveCount(3);
    }

    [Fact]
    public async Task Closing_EmptiesTheMenu()
    {
        await StartAsync();
        await GmcpAsync("Room.Info", Room);

        await _h.Session.CloseAsync(sendSaveAndQuit: false);

        _h.Session.ActionMenu.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task WithoutChanges_DisconnectingRaisesNoMenuEvent()
    {
        await StartAsync();

        await _h.Connection.RaiseServerClosed();
        await _h.Session.WhenIdleAsync();

        _changes.Should().Be(0);
    }

    // ── Another MUD ────────────────────────────────────────────────────────

    private sealed class SpellsTranslator : IActionMenuTranslator
    {
        public string Package => "Otro.Hechizos";
        public string SupportedModule => "Otro.Hechizos 2";
        public string SectionLabel => "Hechizos";
        public IReadOnlyList<ActionMenuNode>? Translate(string payload)
            => [ActionMenuNode.Action("Luz", "formular luz")!];
    }

    [Fact]
    public async Task AnotherTranslatorInTheSettings_IsAllItTakes_ToSupportAnotherPackage()
    {
        _h.Translators = [new SpellsTranslator()];
        await StartAsync();

        await _h.ReceiveBytesAsync(Iac, Will, Gmcp);
        await GmcpAsync("Otro.Hechizos", "{}");

        _h.SentTelnet.Skip(1).Select(p => Encoding.UTF8.GetString(p[3..^2])).Should().Contain("Core.Supports.Set [\"Comm.Channel 1\",\"Otro.Hechizos 2\"]");
        Section("Hechizos").Children.Single().Command.Should().Be("formular luz");
    }
}
