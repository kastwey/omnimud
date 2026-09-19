using System.Text;
using FluentAssertions;
using NSubstitute;
using Omnimud.Core.Session;
using Omnimud.Core.Text;

namespace Omnimud.Core.Tests.Session;

public sealed class MudSessionReceiveTests : IAsyncDisposable
{
    private const string Esc = SessionHarness.Esc;
    private const byte Iac = 255, Will = 251, Wont = 252, Do = 253, Dont = 254, Sb = 250, Se = 240, Ga = 249;

    private readonly SessionHarness _h = new();

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    // ── Lines, packets and prompts ─────────────────────────────────────────

    [Fact]
    public async Task CompleteLines_AreEmittedOneByOne_AsMudLines()
    {
        await _h.StartAsync();

        await _h.ReceiveAsync("Estás en una plaza.\r\nHay una fuente.\r\n");

        _h.Lines.Select(l => (l.PlainText, l.Kind)).Should().Equal(
            ("Estás en una plaza.", SessionLineKind.Mud),
            ("Hay una fuente.", SessionLineKind.Mud));
    }

    [Fact]
    public async Task LineSplitAcrossPackets_IsEmittedOnceComplete()
    {
        await _h.StartAsync();

        await _h.ReceiveAsync("Un orco te ");
        _h.Lines.Should().BeEmpty();
        await _h.ReceiveAsync("ataca.\n");

        _h.PlainLines.Should().Equal("Un orco te ataca.");
    }

    [Fact]
    public async Task EmptyLines_AreKept()
    {
        await _h.StartAsync();

        await _h.ReceiveAsync("uno\n\ndos\n");

        _h.PlainLines.Should().Equal("uno", "", "dos");
    }

    [Fact]
    public async Task Utf8CharacterSplitAcrossPackets_IsDecodedCorrectly()
    {
        await _h.StartAsync();
        var bytes = Encoding.UTF8.GetBytes("El ñu corre.\n");
        var cut = Array.IndexOf(bytes, (byte)0xC3) + 1;

        await _h.ReceiveBytesAsync(bytes[..cut]);
        await _h.ReceiveBytesAsync(bytes[cut..]);

        _h.PlainLines.Should().Equal("El ñu corre.");
    }

    [Fact]
    public async Task Latin1Profile_DecodesAndEncodesWithThatEncoding()
    {
        _h.Profile = _h.Profile with { Encoding = "iso-8859-1" };
        await _h.StartAsync();

        await _h.ReceiveBytesAsync(0xF1, (byte)'u', (byte)'\n');
        await _h.SubmitAsync("decir ñ");

        _h.PlainLines.Should().Equal("ñu");
        _h.Connection.SentPackets.Single().Should().Equal(Encoding.Latin1.GetBytes("decir ñ\n"));
    }

    [Fact]
    public async Task TextWithoutNewline_IsFlushedAsPromptAfterTheDelay()
    {
        await _h.StartAsync();

        await _h.ReceiveAsync("Vida: 50> ");
        await _h.AdvanceAsync(149);
        _h.Lines.Should().BeEmpty();
        await _h.AdvanceAsync(1);

        _h.Lines.Should().ContainSingle();
        _h.Lines[0].Kind.Should().Be(SessionLineKind.Prompt);
        _h.Lines[0].PlainText.Should().Be("Vida: 50> ");
    }

    [Fact]
    public async Task PromptFlush_UsesTheConfiguredDelay()
    {
        _h.SetOptions(o => o with { PromptFlushMilliseconds = 1000 });
        await _h.StartAsync();

        await _h.ReceiveAsync("> ");
        await _h.AdvanceAsync(900);
        _h.Lines.Should().BeEmpty();
        await _h.AdvanceAsync(100);

        _h.Lines.Should().ContainSingle();
    }

    [Fact]
    public async Task SlowLine_DataBeforeTheDelay_IsNotSplitIntoAPrompt()
    {
        await _h.StartAsync();

        await _h.ReceiveAsync("Un texto que ");
        await _h.AdvanceAsync(100);
        await _h.ReceiveAsync("llega despacio.\n");
        await _h.AdvanceAsync(500);

        _h.Lines.Select(l => (l.PlainText, l.Kind)).Should().Equal(("Un texto que llega despacio.", SessionLineKind.Mud));
    }

    [Fact]
    public async Task GoAhead_FlushesThePromptImmediately()
    {
        await _h.StartAsync();
        var data = Encoding.UTF8.GetBytes("Línea\n> ").Concat(new[] { Iac, Ga }).ToArray();

        await _h.ReceiveBytesAsync(data);

        _h.Lines.Select(l => (l.PlainText, l.Kind)).Should().Equal(
            ("Línea", SessionLineKind.Mud),
            ("> ", SessionLineKind.Prompt));
    }

    [Fact]
    public async Task BackspacesAndCarriageReturns_AreCleaned()
    {
        await _h.StartAsync();

        await _h.ReceiveAsync("holx\ba mundo\r\n");

        _h.PlainLines.Should().Equal("hola mundo");
    }

    // ── Telnet ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task TelnetSequenceSplitAcrossPackets_NeverReachesTheText()
    {
        await _h.StartAsync();

        await _h.ReceiveBytesAsync((byte)'a', Iac);
        await _h.ReceiveBytesAsync(Will, 1, (byte)'b', (byte)'\n');

        _h.PlainLines.Should().Equal("ab");
        _h.Session.PasswordMode.Should().BeTrue();
    }

    [Fact]
    public async Task WillEcho_EntersPasswordModeAndAnswersDo_WontEchoLeavesIt()
    {
        await _h.StartAsync();

        await _h.ReceiveBytesAsync(Iac, Will, 1);
        _h.Session.PasswordMode.Should().BeTrue();
        await _h.ReceiveBytesAsync(Iac, Wont, 1);

        _h.Session.PasswordMode.Should().BeFalse();
        _h.PasswordModes.Should().Equal(true, false);
        _h.SentTelnet.Should().HaveCount(2);
        _h.SentTelnet[0].Should().Equal(Iac, Do, 1);
        _h.SentTelnet[1].Should().Equal(Iac, Dont, 1);
    }

    [Fact]
    public async Task UnsupportedOptions_AreRefused_OncePerConnection()
    {
        await _h.StartAsync();

        await _h.ReceiveBytesAsync(Iac, Will, 86, Iac, Do, 31, Iac, Do, 24);
        await _h.ReceiveBytesAsync(Iac, Will, 86);

        _h.SentTelnet.Should().HaveCount(3);
        _h.SentTelnet[0].Should().Equal(Iac, Dont, 86);
        _h.SentTelnet[1].Should().Equal(Iac, Wont, 31);
        _h.SentTelnet[2].Should().Equal(Iac, Wont, 24);
    }

    [Fact]
    public async Task WillGmcp_IsAcceptedAndChannelsAreRequested()
    {
        await _h.StartAsync();

        await _h.ReceiveBytesAsync(Iac, Will, 201);

        _h.SentTelnet[0].Should().Equal(Iac, Do, 201);
        var packets = _h.SentTelnet.Skip(1).Select(p => Encoding.UTF8.GetString(p[3..^2])).ToArray();
        packets.Should().Contain("Core.Supports.Set [\"Comm.Channel 1\",\"Char.Inventory 1\",\"Room.Info 1\"]");
    }

    [Fact]
    public async Task NegotiationOptionOff_SequencesAreOnlyStripped()
    {
        _h.SetOptions(o => o with { TelnetNegotiation = false });
        await _h.StartAsync();

        await _h.ReceiveBytesAsync((byte)'a', Iac, Will, 1, Iac, Do, 31, (byte)'b', (byte)'\n');

        _h.PlainLines.Should().Equal("ab");
        _h.Connection.SentPackets.Should().BeEmpty();
        _h.Session.PasswordMode.Should().BeFalse();
    }

    [Fact]
    public async Task EscapedIac_IsALiteralByte()
    {
        _h.Profile = _h.Profile with { Encoding = "iso-8859-1" };
        await _h.StartAsync();

        await _h.ReceiveBytesAsync((byte)'a', Iac, Iac, (byte)'\n');

        _h.PlainLines.Should().Equal("aÿ");
    }

    // ── ANSI ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Color_ContinuesAcrossLinesAndPackets()
    {
        await _h.StartAsync();

        await _h.ReceiveAsync($"{Esc}[31mrojo\n");
        await _h.ReceiveAsync($"sigue\n{Esc}[0mnormal\n");

        _h.Lines[0].Segments.Single().Style.Foreground.Should().Be(AnsiColor.Red);
        _h.Lines[1].Segments.Single().Style.Foreground.Should().Be(AnsiColor.Red);
        _h.Lines[2].Segments.Single().Style.Should().Be(AnsiStyle.Default);
        _h.PlainLines.Should().Equal("rojo", "sigue", "normal");
    }

    [Fact]
    public async Task NonSgrSequences_AreDiscarded()
    {
        await _h.StartAsync();

        await _h.ReceiveAsync($"{Esc}[2J{Esc}[HBienvenido al mundo\n");

        _h.PlainLines.Should().Equal("Bienvenido al mundo");
    }

    // ── MSP ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MspLine_GoesToSoundAndDisappearsFromText()
    {
        await _h.StartAsync();

        await _h.ReceiveAsync("Antes\n!!SOUND(espada.wav V=80 L=2)\nDespués\n!!MUSIC(off)\n");

        _h.PlainLines.Should().Equal("Antes", "Después");
        await _h.Sound.Received(1).HandleMspAsync(
            Arg.Is<SoundCommand>(c => c.Type == SoundType.Sound && c.FileName == "espada.wav" && c.Volume == 80 && c.Loop == 2),
            Arg.Any<CancellationToken>());
        await _h.Sound.Received(1).HandleMspAsync(
            Arg.Is<SoundCommand>(c => c.Type == SoundType.Music && c.IsStop), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MspLine_IsNeitherLoggedAnnouncedNorTriggered()
    {
        _h.Store.Triggers.Add(SessionHarness.Trigger("SOUND", "no"));
        await _h.StartAsync();

        await _h.ReceiveAsync("!!SOUND(a.wav)\n");

        _h.Spoken.Should().BeEmpty();
        _h.SentLines.Should().BeEmpty();
    }

    [Fact]
    public async Task MspInTheMiddleOfALine_IsOrdinaryText()
    {
        await _h.StartAsync();

        await _h.ReceiveAsync("Dice: !!SOUND(a.wav)\n");

        _h.PlainLines.Should().Equal("Dice: !!SOUND(a.wav)");
        await _h.Sound.DidNotReceive().HandleMspAsync(Arg.Any<SoundCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SoundFailure_DoesNotBreakReception()
    {
        _h.Sound.HandleMspAsync(Arg.Any<SoundCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("no hay fichero")));
        await _h.StartAsync();

        await _h.ReceiveAsync("!!SOUND(a.wav)\nsigue\n");

        _h.PlainLines.Should().Equal("sigue");
    }

    // ── Announce, silent mode, flash ───────────────────────────────────────

    [Fact]
    public async Task MudText_IsAnnouncedQueued_WithoutAnsi()
    {
        await _h.StartAsync();

        await _h.ReceiveAsync($"{Esc}[1;33mUn dragón{Esc}[0m ruge.\n");

        _h.Announcements.Should().Equal(("Un dragón ruge.", AnnouncePriority.Queue));
    }

    [Fact]
    public async Task InactiveWindow_NothingIsAnnounced_AndFlashIsRequestedOncePerBlock()
    {
        await _h.StartAsync(windowActive: false);

        await _h.ReceiveAsync("uno\ndos\n");

        _h.Spoken.Should().BeEmpty();
        _h.Flashes.Should().Be(1);
        _h.PlainLines.Should().Equal("uno", "dos");
    }

    [Fact]
    public async Task ActiveWindow_NoFlash()
    {
        await _h.StartAsync();

        await _h.ReceiveAsync("uno\n");

        _h.Flashes.Should().Be(0);
    }

    [Fact]
    public async Task FlashOptionOff_NoFlash()
    {
        _h.SetOptions(o => o with { FlashWindow = false });
        await _h.StartAsync(windowActive: false);

        await _h.ReceiveAsync("uno\n");

        _h.Flashes.Should().Be(0);
    }

    [Fact]
    public async Task AnnounceMudTextOff_TextIsShownButNotSpoken()
    {
        _h.SetOptions(o => o with { AnnounceMudText = false });
        await _h.StartAsync();

        await _h.ReceiveAsync("uno\n");

        _h.PlainLines.Should().Equal("uno");
        _h.Spoken.Should().BeEmpty();
    }

    [Fact]
    public async Task SpeakMarker_OutsideSilentMode_IsJustRemoved()
    {
        await _h.StartAsync();

        await _h.ReceiveAsync("Aviso: all_speak:te atacan\n");

        _h.PlainLines.Should().Equal("Aviso: te atacan");
        string.Concat(_h.Lines[0].Segments.Select(s => s.Text)).Should().Be("Aviso: te atacan");
        _h.Spoken.Should().Equal("Aviso: te atacan");
    }

    [Fact]
    public async Task SilentMode_OnlyTextAfterTheMarkerIsSpoken()
    {
        await _h.StartAsync();
        _h.Session.SilentMode = true;

        await _h.ReceiveAsync("ruido\nAviso: all_speak:te atacan\n");

        _h.PlainLines.Should().Equal("ruido", "Aviso: te atacan");
        _h.Spoken.Should().Equal("te atacan");
    }

    [Fact]
    public async Task SpeakMarker_SplitAcrossColorSegments_IsRemovedFromSegments()
    {
        await _h.StartAsync();

        await _h.ReceiveAsync($"{Esc}[31mall_{Esc}[32mspeak:hola\n");

        _h.PlainLines.Should().Equal("hola");
        string.Concat(_h.Lines[0].Segments.Select(s => s.Text)).Should().Be("hola");
    }

    // ── Messages ───────────────────────────────────────────────────────────

    [Fact]
    public async Task MessageRule_AddsMessage_TextStaysInReceived_AndIsNotSpokenTwice()
    {
        _h.Store.Rules.Add(new MessageRule(@"^(\w+) te dice: '(.*)'$", "$1: $2", Channel: "decir"));
        await _h.StartAsync();

        await _h.ReceiveAsync("Ana te dice: 'hola'\n");

        _h.PlainLines.Should().Equal("Ana te dice: 'hola'");
        _h.AddedMessages.Should().ContainSingle();
        _h.AddedMessages[0].Text.Should().Be("Ana: hola");
        _h.AddedMessages[0].Channel.Should().Be("decir");
        _h.AddedMessages[0].Time.Should().Be(new DateTime(2026, 3, 14, 10, 30, 0));
        _h.Spoken.Should().Equal("Ana te dice: 'hola'");
    }

    [Fact]
    public async Task MessageRule_WhenLineWasNotSpoken_TheMessageIs()
    {
        _h.SetOptions(o => o with { AnnounceMudText = false });
        _h.Store.Rules.Add(new MessageRule(@"^(\w+) te dice: '(.*)'$", "$1: $2"));
        await _h.StartAsync();

        await _h.ReceiveAsync("Ana te dice: 'hola'\n");

        _h.Spoken.Should().Equal("Ana: hola");
    }

    [Fact]
    public async Task MessageRule_AnnounceMessagesOff_NothingSpoken()
    {
        _h.SetOptions(o => o with { AnnounceMudText = false, AnnounceMessages = false });
        _h.Store.Rules.Add(new MessageRule("te dice", "$0"));
        await _h.StartAsync();

        await _h.ReceiveAsync("Ana te dice: 'hola'\n");

        _h.AddedMessages.Should().ContainSingle();
        _h.Spoken.Should().BeEmpty();
    }

    [Fact]
    public async Task MessageRule_IsEvaluatedPerLine_OnlyFirstRuleApplies()
    {
        _h.Store.Rules.Add(new MessageRule(".*grita.*", "GRITO: $0"));
        _h.Store.Rules.Add(new MessageRule("Ana.*", "ANA: $0"));
        await _h.StartAsync();

        await _h.ReceiveAsync("Ana grita\nnada\nAna susurra\n");

        _h.AddedMessages.Select(m => m.Text).Should().Equal("GRITO: Ana grita", "ANA: Ana susurra");
    }

    [Fact]
    public async Task GmcpChannelText_BecomesAMessage()
    {
        await _h.StartAsync();
        var payload = Encoding.UTF8.GetBytes("Comm.Channel.Text {\"channel\":\"chat\",\"talker\":\"Bob\",\"text\":\"\\u001b[31mbuenas\\u001b[0m\"}");
        var packet = new byte[] { Iac, Sb, 201 }.Concat(payload).Concat(new[] { Iac, Se }).ToArray();

        await _h.ReceiveBytesAsync(packet);

        _h.AddedMessages.Should().ContainSingle();
        _h.AddedMessages[0].Should().BeEquivalentTo(new { Text = "[chat] Bob: buenas", Channel = "chat", Sender = "Bob", Number = 1 });
        _h.Spoken.Should().Equal("[chat] Bob: buenas");
        _h.Lines.Should().BeEmpty();
    }

    private static byte[] GmcpPacket(string content) =>
        new byte[] { Iac, Sb, 201 }.Concat(Encoding.UTF8.GetBytes(content)).Concat(new[] { Iac, Se }).ToArray();

    [Fact]
    public async Task GmcpMessage_FollowedByItsTextCopy_IsSpokenOnce_ShownInBothBoxes()
    {
        await _h.StartAsync();

        await _h.ReceiveBytesAsync(GmcpPacket("Comm.Channel.Text {\"channel\":\"chat\",\"talker\":\"Bob\",\"text\":\"buenas a todos\"}"));
        await _h.ReceiveAsync("[Chat] Bob: buenas a todos\r\n");

        _h.Spoken.Should().Equal("[chat] Bob: buenas a todos");
        _h.AddedMessages.Should().ContainSingle();
        _h.Lines.Select(l => l.PlainText).Should().Contain("[Chat] Bob: buenas a todos", "the text copy is still shown in Received");
    }

    [Fact]
    public async Task TextLine_FollowedByItsGmcpCopy_IsSpokenOnce()
    {
        await _h.StartAsync();

        await _h.ReceiveAsync("[Chat] Bob: buenas a todos\r\n");
        await _h.ReceiveBytesAsync(GmcpPacket("Comm.Channel.Text {\"channel\":\"chat\",\"talker\":\"Bob\",\"text\":\"buenas a todos\"}"));

        _h.Spoken.Should().Equal("[Chat] Bob: buenas a todos");
        _h.AddedMessages.Should().ContainSingle("it still goes to the Messages box");
    }

    [Fact]
    public async Task GmcpCopy_DoesNotAlsoProduceARuleMessage()
    {
        _h.Store.Rules.Add(new MessageRule(@"^\[Chat\] (.+)$", "$1"));
        await _h.StartAsync();

        await _h.ReceiveBytesAsync(GmcpPacket("Comm.Channel.Text {\"channel\":\"chat\",\"talker\":\"Bob\",\"text\":\"buenas a todos\"}"));
        await _h.ReceiveAsync("[Chat] Bob: buenas a todos\r\n");

        _h.AddedMessages.Should().ContainSingle("GMCP and the MUD's message rule describe the same message");
    }

    [Fact]
    public async Task WithMessageAnnouncementsOff_TheTextCopyIsStillSpoken()
    {
        _h.SetOptions(o => o with { AnnounceMessages = false });
        await _h.StartAsync();

        await _h.ReceiveBytesAsync(GmcpPacket("Comm.Channel.Text {\"channel\":\"chat\",\"talker\":\"Bob\",\"text\":\"buenas a todos\"}"));
        await _h.ReceiveAsync("[Chat] Bob: buenas a todos\r\n");

        _h.Spoken.Should().Equal("[Chat] Bob: buenas a todos");
    }

    [Fact]
    public async Task GmcpChannelText_WithSenderField_AndMalformedJson()
    {
        await _h.StartAsync();
        byte[] Packet(string s) => new byte[] { Iac, Sb, 201 }.Concat(Encoding.UTF8.GetBytes(s)).Concat(new[] { Iac, Se }).ToArray();

        await _h.ReceiveBytesAsync(Packet("Comm.Channel.Text {\"channel\":\"gremio\",\"sender\":\"Eva\",\"text\":\"hola\"}"));
        await _h.ReceiveBytesAsync(Packet("Comm.Channel.Text {esto no es json"));
        await _h.ReceiveBytesAsync(Packet("Char.Vitals {\"hp\":1}"));

        _h.AddedMessages.Select(m => m.Text).Should().Equal("[gremio] Eva: hola");
    }

    [Fact]
    public async Task Messages_AreCappedAtOneThousand_AndNumberOneIsTheNewest()
    {
        _h.SetOptions(o => o with { AnnounceMudText = false, AnnounceMessages = false });
        _h.Store.Rules.Add(new MessageRule(@"^msg (\d+)$", "$1"));
        await _h.StartAsync();

        var sb = new StringBuilder();
        for (var i = 1; i <= 1003; i++) sb.Append("msg ").Append(i).Append('\n');
        await _h.ReceiveAsync(sb.ToString());

        _h.Session.Messages.Should().HaveCount(1000);
        _h.Session.Messages[0].Text.Should().Be("4");
        _h.Session.Messages[^1].Should().BeEquivalentTo(new { Text = "1003", Number = 1 });
        _h.Session.GetMessage(1)!.Text.Should().Be("1003");
        _h.Session.GetMessage(1001).Should().BeNull();
    }

    [Fact]
    public async Task SilentMode_MessagesAreNotSpoken()
    {
        _h.Store.Rules.Add(new MessageRule("te dice", "$0"));
        await _h.StartAsync();
        _h.Session.SilentMode = true;

        await _h.ReceiveAsync("Ana te dice algo\n");

        _h.AddedMessages.Should().ContainSingle();
        _h.Spoken.Should().BeEmpty();
    }

    // ── Robustness ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SubscriberThatThrows_DoesNotStopTheSession()
    {
        await _h.StartAsync();
        _h.Session.LineReceived += _ => throw new InvalidOperationException("vista rota");

        await _h.ReceiveAsync("uno\n");
        await _h.ReceiveAsync("dos\n");

        _h.PlainLines.Should().Equal("uno", "dos");
    }

    [Fact]
    public async Task DataAfterDisconnection_IsIgnored()
    {
        await _h.StartAsync();
        await _h.Session.CloseAsync(false);
        _h.ClearOutput();

        await _h.ReceiveAsync("fantasma\n");

        _h.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task ManyPacketsFromManyThreads_AreProcessedWithoutLosingLines()
    {
        _h.SetOptions(o => o with { AnnounceMudText = false });
        await _h.StartAsync();

        // Order between threads is undefined, but every line must arrive whole.
        await Task.WhenAll(Enumerable.Range(0, 8).Select(t => Task.Run(async () =>
        {
            for (var i = 0; i < 50; i++)
                await _h.Connection.RaiseData(Encoding.UTF8.GetBytes($"hilo{t}-linea{i}\n"));
        })));
        await _h.Session.WhenIdleAsync();

        _h.PlainLines.Should().HaveCount(400);
        _h.PlainLines.Should().OnlyContain(l => l.StartsWith("hilo") && l.Contains("-linea"));
    }
}
