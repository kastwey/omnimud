using System.Diagnostics;
using System.Text;
using FluentAssertions;
using NSubstitute;
using Omnimud.Core.Scripting;
using Omnimud.Core.Session;
using Omnimud.Core.Triggers;

namespace Omnimud.Core.Tests.Session;

/// <summary>A MUD whose rule set is a Lua script: the script sees each received block and decides the messages.</summary>
public sealed class MudSessionMessageScriptTests : IAsyncDisposable
{
    private const byte Iac = 255, Sb = 250, Se = 240, Ga = 249;

    /// <summary>"X dice '..." up to the line that ends in a quote, like the Callandor rule; every message of the block.</summary>
    private const string SayScript = """
        local lines = om.lines
        local i = 1
        while i <= #lines do
          if om.match(lines[i], [[^\w+ dice ']]) then
            local j = i
            while j < #lines and string.sub(lines[j], -1) ~= "'" do j = j + 1 end
            om.message(table.concat(lines, "\n", i, j))
            i = j + 1
          else
            i = i + 1
          end
        end
        """;

    private readonly SessionHarness _h = new();

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private async Task StartAsync(string? script, bool windowActive = true)
    {
        _h.Scripts = new LuaScriptEngine();
        _h.Store.RuleScript = script;
        await _h.StartAsync(windowActive: windowActive);
    }

    private static byte[] GmcpPacket(string content) =>
        new byte[] { Iac, Sb, 201 }.Concat(Encoding.UTF8.GetBytes(content)).Concat(new[] { Iac, Se }).ToArray();

    private IEnumerable<string> ScriptErrors => _h.SystemLines.Where(l => l.Contains("reglas de mensajes"));

    // ── What the script is for ─────────────────────────────────────────────

    [Fact]
    public async Task MessageWrappedOverThreeLines_BecomesOneCompleteMessage()
    {
        await StartAsync(SayScript);

        await _h.ReceiveAsync("Un orco llega.\r\nAna dice 'hola, cuanto tiempo\r\nsin verte por aqui,\r\nque tal todo'\r\nEl orco se va.\r\n");

        _h.AddedMessages.Select(m => m.Text).Should().Equal("Ana dice 'hola, cuanto tiempo\nsin verte por aqui,\nque tal todo'");
        _h.AddedMessages[0].Time.Should().Be(new DateTime(2026, 3, 14, 10, 30, 0));
        _h.MudLines.Should().Equal("Un orco llega.", "Ana dice 'hola, cuanto tiempo", "sin verte por aqui,", "que tal todo'", "El orco se va.");
        _h.SystemLines.Should().BeEmpty();
    }

    [Fact]
    public async Task SeveralMessagesInOneBlock_InOrder_AndOneBlockAtATime()
    {
        await StartAsync(SayScript);

        await _h.ReceiveAsync("Ana dice 'uno'\r\nRuido.\r\nBeto dice 'dos\r\ny medio'\r\n");
        await _h.ReceiveAsync("Nada que ver.\r\n");
        await _h.ReceiveAsync("Carla dice 'tres'\r\n");

        _h.AddedMessages.Select(m => m.Text).Should().Equal("Ana dice 'uno'", "Beto dice 'dos\ny medio'", "Carla dice 'tres'");
        _h.Session.Messages.Select(m => m.Number).Should().Equal(3, 2, 1);
    }

    [Fact]
    public async Task TheScriptSeesTheBlock_WithoutColours_WithoutTheSpeakMarker_WithoutPrompts_WithoutMsp()
    {
        await StartAsync("om.message(#om.lines .. '|' .. om.block)");

        await _h.ReceiveBytesAsync(Encoding.UTF8.GetBytes($"{SessionHarness.Esc}[31mRojo{SessionHarness.Esc}[0m\r\nall_speak:Importante\r\n!!SOUND(ding.wav)\r\n\r\nPV: 10> ")
            .Concat(new[] { Iac, Ga }).ToArray());

        _h.AddedMessages.Select(m => m.Text).Should().Equal(["3|Rojo\nImportante"], "three lines (the last one empty); line breaks at the end of a message are trimmed");
        _h.MudLines.Should().Contain("PV: 10> ");
    }

    [Fact]
    public async Task APromptOnItsOwn_DoesNotRunTheScript()
    {
        await StartAsync("om.message('se ejecuto')");

        await _h.ReceiveBytesAsync(Encoding.UTF8.GetBytes("> ").Concat(new[] { Iac, Ga }).ToArray());

        _h.MudLines.Should().Equal("> ");
        _h.AddedMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task OnlyMessagesCount_NothingElseTheScriptDoesReachesTheSession()
    {
        await StartAsync("om.send('matar orco') om.sendraw('x') om.display('inyectado') om.echo('eco') om.say('dicho') om.playsound('a.wav') om.status('s') om.setvar('v', '1') om.message('solo esto')");

        await _h.ReceiveAsync("Una linea.\r\n");

        _h.AddedMessages.Select(m => m.Text).Should().Equal("solo esto");
        _h.SentLines.Should().BeEmpty();
        _h.MudLines.Should().Equal("Una linea.");
        _h.Spoken.Should().Equal("Una linea.");
        _h.Statuses.Should().BeEmpty();
        _h.Sound.DidNotReceiveWithAnyArgs().PlayTriggerSound(default!);
        ((IScriptHost)_h.Session).IsVariableSet("v").Should().BeFalse();
    }

    // ── Speech: never twice ────────────────────────────────────────────────

    [Fact]
    public async Task LinesAlreadySpoken_TheMessageIsNotSpokenAgain()
    {
        await StartAsync(SayScript);

        await _h.ReceiveAsync("Ana dice 'hola\r\nque tal'\r\n");

        _h.AddedMessages.Should().ContainSingle();
        _h.Spoken.Should().Equal("Ana dice 'hola", "que tal'");
    }

    [Fact]
    public async Task LinesNotSpoken_TheMessageIs()
    {
        _h.SetOptions(o => o with { AnnounceMudText = false });
        await StartAsync(SayScript);

        await _h.ReceiveAsync("Ana dice 'hola\r\nque tal'\r\n");

        _h.Spoken.Should().Equal("Ana dice 'hola\nque tal'");
    }

    [Fact]
    public async Task AnnounceMessagesOff_OrInactiveWindow_NothingExtraIsSpoken()
    {
        _h.SetOptions(o => o with { AnnounceMudText = false, AnnounceMessages = false });
        await StartAsync(SayScript);
        await _h.ReceiveAsync("Ana dice 'hola'\r\n");
        _h.AddedMessages.Should().ContainSingle();
        _h.Spoken.Should().BeEmpty();
    }

    [Fact]
    public async Task AMessageTheScriptComposed_GoesByTheBlock_SpokenLinesMeanNoSecondReading()
    {
        await StartAsync("if om.match(om.block, 'te da (\\\\d+) monedas') then om.message('Cobro: ' .. om.match(om.block, 'te da (\\\\d+) monedas')[1]) end");

        await _h.ReceiveAsync("Ana te da 30 monedas.\r\n");

        _h.AddedMessages.Select(m => m.Text).Should().Equal("Cobro: 30");
        _h.Spoken.Should().Equal("Ana te da 30 monedas.");
    }

    [Fact]
    public async Task AGaggedLine_WasNotSpoken_SoItsMessageIs()
    {
        _h.Store.Triggers.Add(SessionHarness.Trigger("Ana dice", gag: true, type: TriggerActionType.PlaySound, sound: "x.wav"));
        await StartAsync(SayScript);

        await _h.ReceiveAsync("Ana dice 'secreto'\r\n");

        _h.MudLines.Should().BeEmpty();
        _h.AddedMessages.Select(m => m.Text).Should().Equal("Ana dice 'secreto'");
        _h.Spoken.Should().Equal("Ana dice 'secreto'");
    }

    // ── GMCP ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GmcpMessage_AndTheScriptMessageOfItsTextCopy_AreOneMessage()
    {
        await StartAsync("for _, l in ipairs(om.lines) do if string.sub(l, 1, 1) == '[' then om.message(l) end end");

        await _h.ReceiveBytesAsync(GmcpPacket("Comm.Channel.Text {\"channel\":\"chat\",\"talker\":\"Bob\",\"text\":\"buenas a todos\"}"));
        await _h.ReceiveAsync("[Chat] Bob: buenas a todos\r\n");

        _h.AddedMessages.Select(m => m.Text).Should().Equal("[chat] Bob: buenas a todos");
        _h.Spoken.Should().Equal("[chat] Bob: buenas a todos");
        _h.MudLines.Should().Equal("[Chat] Bob: buenas a todos");
    }

    [Fact]
    public async Task GmcpCopyInsideAnAccumulatedMessage_OnlyThatLineIsTakenOut()
    {
        await StartAsync("local t = {} for _, l in ipairs(om.lines) do if string.sub(l, 1, 1) == '[' then t[#t + 1] = l end end if #t > 0 then om.message(table.concat(t, '\\n')) end");

        await _h.ReceiveBytesAsync(GmcpPacket("Comm.Channel.Text {\"channel\":\"chat\",\"talker\":\"Bob\",\"text\":\"buenas a todos\"}"));
        await _h.ReceiveAsync("[Chat] Bob: buenas a todos\r\n[Clan] Ana: a las armas\r\n");

        _h.AddedMessages.Select(m => m.Text).Should().Equal("[chat] Bob: buenas a todos", "[Clan] Ana: a las armas");
    }

    [Fact]
    public async Task GmcpMessageWrappedOverTwoTextLines_IsRecognisedAsAWhole()
    {
        await StartAsync(SayScript);

        await _h.ReceiveBytesAsync(GmcpPacket("Comm.Channel.Text {\"channel\":\"decir\",\"talker\":\"Ana\",\"text\":\"hola a todos los presentes en la sala\"}"));
        await _h.ReceiveAsync("Ana dice 'hola a todos los\r\npresentes en la sala'\r\n");

        _h.AddedMessages.Should().ContainSingle().Which.Text.Should().StartWith("[decir] Ana:");
    }

    [Fact]
    public async Task WithoutGmcp_NothingIsDropped()
    {
        await StartAsync("for _, l in ipairs(om.lines) do if string.sub(l, 1, 1) == '[' then om.message(l) end end");

        await _h.ReceiveAsync("[Chat] Bob: buenas a todos\r\n[Chat] Bob: buenas a todos\r\n");

        _h.AddedMessages.Should().HaveCount(2);
    }

    // ── Errors and limits: the game goes on ────────────────────────────────

    [Fact]
    public async Task RuntimeError_OneSystemLineTheFirstTimeOnly_AndTheGameGoesOn()
    {
        await StartAsync("if string.find(om.block, 'peligro', 1, true) then local t = nil om.message(t.campo) end\nom.message('ok: ' .. om.lines[1])");

        await _h.ReceiveAsync("Hay peligro aqui.\r\n");
        await _h.ReceiveAsync("Mas peligro.\r\n");
        await _h.ReceiveAsync("Todo tranquilo.\r\n");
        await _h.ReceiveAsync("Otra vez peligro.\r\n");

        ScriptErrors.Should().ContainSingle().Which.Should().StartWith("Error en el script de reglas de mensajes:").And.Contain("line 1");
        _h.MudLines.Should().Equal("Hay peligro aqui.", "Mas peligro.", "Todo tranquilo.", "Otra vez peligro.");
        _h.AddedMessages.Select(m => m.Text).Should().Equal("ok: Todo tranquilo.");
        _h.Session.State.Should().Be(SessionState.Connected);
    }

    [Fact]
    public async Task SyntaxError_IsReportedWhenTheRulesAreLoaded_AndTheScriptNeverRuns()
    {
        await StartAsync("om.message('a'\nif then");

        ScriptErrors.Should().ContainSingle();
        await _h.ReceiveAsync("Una linea.\r\nOtra.\r\n");

        _h.MudLines.Should().Equal("Una linea.", "Otra.");
        _h.AddedMessages.Should().BeEmpty();
        ScriptErrors.Should().ContainSingle();
    }

    [Fact]
    public async Task InfiniteLoop_DoesNotHoldUpTheNextLines_AndIsSwitchedOffAfterThreeBlocks()
    {
        await StartAsync("while true do end");

        var watch = Stopwatch.StartNew();
        for (var i = 1; i <= 5; i++)
            await _h.ReceiveAsync($"Linea {i}.\r\n");
        watch.Stop();

        _h.MudLines.Should().Equal("Linea 1.", "Linea 2.", "Linea 3.", "Linea 4.", "Linea 5.");
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        ScriptErrors.Should().HaveCount(2, "the error once, and the notice that the script has been switched off");
        ScriptErrors.Last().Should().Contain("desactivado");
        _h.AddedMessages.Should().BeEmpty();
        MudSession.MaxConsecutiveLimitFailures.Should().Be(3);
    }

    [Fact]
    public async Task WaitingInsideTheScript_IsAnError_NotAWait()
    {
        await StartAsync("om.sleep(5) om.message('nunca')");

        var watch = Stopwatch.StartNew();
        await _h.ReceiveAsync("Linea.\r\n");

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
        ScriptErrors.Should().ContainSingle().Which.Should().Contain("om.sleep");
        _h.AddedMessages.Should().BeEmpty();
        _h.MudLines.Should().Equal("Linea.");
    }

    [Fact]
    public async Task AnEngineThatThrowsOrAnswersNothing_NeverBreaksReception()
    {
        _h.Store.RuleScript = "om.message('x')";
        await _h.StartAsync(); // the harness' substitute engine answers null
        await _h.ReceiveAsync("Uno.\r\n");

        _h.Scripts.ExecuteAsync(Arg.Any<string>(), Arg.Any<ScriptContext>(), Arg.Any<ScriptLimits?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ScriptResult>>(_ => throw new InvalidOperationException("motor roto"));
        await _h.ReceiveAsync("Dos.\r\n");
        await _h.ReceiveAsync("Tres.\r\n");

        _h.MudLines.Should().Equal("Uno.", "Dos.", "Tres.");
        ScriptErrors.Should().ContainSingle().Which.Should().Contain("motor roto");
    }

    // ── Patterns and scripts do not mix ────────────────────────────────────

    [Fact]
    public async Task AScriptSet_DoesNotEvaluateItsPatterns()
    {
        _h.Store.Rules.Add(new MessageRule("orco", "PATRON: $0"));
        await StartAsync(SayScript);

        await _h.ReceiveAsync("Un orco llega.\r\nAna dice 'un orco'\r\n");

        _h.AddedMessages.Select(m => m.Text).Should().Equal("Ana dice 'un orco'");
    }

    [Fact]
    public async Task APatternSet_WorksAsBefore_PerLine_AndNeverRunsAScript()
    {
        _h.Store.Rules.Add(new MessageRule(@"^(\w+) dice '(.*)$", "$1: $2"));
        _h.Store.RuleScript = "   ";
        await _h.StartAsync();

        await _h.ReceiveAsync("Ana dice 'hola\r\nque tal'\r\nBeto dice 'adios'\r\n");

        _h.AddedMessages.Select(m => m.Text).Should().Equal("Ana: hola", "Beto: adios'");
        await _h.Scripts.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default!, default(ScriptLimits), default);
        _h.Scripts.DidNotReceiveWithAnyArgs().Validate(default!);
    }

    [Fact]
    public async Task InjectedText_OmDisplay_DoesNotGoThroughTheScript_NorDoSystemLines()
    {
        _h.Store.Triggers.Add(SessionHarness.Trigger("llega un mensajero", "om.display(\"Ana dice 'inyectado'\")", TriggerActionType.Script));
        await StartAsync(SayScript);

        await _h.ReceiveAsync("llega un mensajero\r\n");
        await _h.WaitUntilAsync(() => _h.MudLines.Contains("Ana dice 'inyectado'"));
        await _h.SubmitAsync("triggers"); // a command answered with System lines
        await _h.ReceiveAsync("Ana dice 'de verdad'\r\n");

        _h.AddedMessages.Select(m => m.Text).Should().Equal("Ana dice 'de verdad'");
    }

    // ── Reload ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reload_PicksUpTheNewScript_ReportsItsErrorsAgain_AndCanGoBackToPatterns()
    {
        await StartAsync("error('primero')");
        await _h.ReceiveAsync("a\r\n");
        await _h.ReceiveAsync("b\r\n");
        ScriptErrors.Should().ContainSingle();

        // Same broken script, reloaded: it is reported once more.
        await _h.Session.ReloadAsync();
        await _h.ReceiveAsync("c\r\n");
        ScriptErrors.Should().HaveCount(2);

        // A good script.
        _h.Store.RuleScript = SayScript;
        await _h.Session.ReloadAsync();
        await _h.ReceiveAsync("Ana dice 'hola'\r\n");
        _h.AddedMessages.Select(m => m.Text).Should().Equal("Ana dice 'hola'");
        ScriptErrors.Should().HaveCount(2);

        // Back to patterns.
        _h.Store.RuleScript = null;
        _h.Store.Rules.Add(new MessageRule("^Beto .*$", "$0"));
        await _h.Session.ReloadAsync();
        await _h.ReceiveAsync("Ana dice 'no'\r\nBeto dice 'si'\r\n");
        _h.AddedMessages.Select(m => m.Text).Should().Equal("Ana dice 'hola'", "Beto dice 'si'");
        _h.Store.RuleScriptReads.Should().Be(4);
    }

    [Fact]
    public async Task Reload_SwitchesAScriptThatWasTurnedOffBackOn()
    {
        await StartAsync("while true do end");
        for (var i = 0; i < 3; i++) await _h.ReceiveAsync("x\r\n");
        ScriptErrors.Should().HaveCount(2);

        _h.Store.RuleScript = "om.message(om.lines[1])";
        await _h.Session.ReloadAsync();
        await _h.ReceiveAsync("ya funciona\r\n");

        _h.AddedMessages.Select(m => m.Text).Should().Equal("ya funciona");
    }

    [Fact]
    public async Task ASessionWithoutMud_HasNoScript()
    {
        _h.Profile = _h.Profile with { MudId = null };
        await StartAsync("om.message('x')");

        await _h.ReceiveAsync("linea\r\n");

        _h.AddedMessages.Should().BeEmpty();
        _h.Store.RuleScriptReads.Should().Be(0);
    }

    // ── Performance ────────────────────────────────────────────────────────

    /// <summary>
    /// Every block builds a fresh Lua VM and parses the script again (the engine isolates executions
    /// and MoonSharp cannot share a compiled chunk between VMs). Measured on the development machine,
    /// debug build: about 5-6 ms per block, some 1.1 s for 200 blocks of 10 lines. The bound is the one
    /// the task set (under 2 s), best of three because the other test assemblies run at the same time.
    /// </summary>
    [Fact]
    public async Task TwoHundredBlocksOfTenLines_AreProcessedInLessThanTwoSeconds()
    {
        await StartAsync(SayScript, windowActive: false);
        var block = string.Concat(Enumerable.Range(0, 10).Select(i => i == 4 ? "Ana dice 'hola\r\n" : i == 5 ? "que tal'\r\n" : $"Linea de relleno numero {i} con 'comillas'.\r\n"));
        await _h.ReceiveAsync(block);

        var best = TimeSpan.MaxValue;
        var attempts = 0;
        for (; attempts < 3 && best >= TimeSpan.FromSeconds(2); attempts++)
        {
            var watch = Stopwatch.StartNew();
            for (var i = 0; i < 200; i++)
                await _h.ReceiveAsync(block);
            watch.Stop();
            if (watch.Elapsed < best) best = watch.Elapsed;
        }

        _h.AddedMessages.Should().HaveCount(1 + 200 * attempts);
        _h.SystemLines.Should().BeEmpty();
        best.Should().BeLessThan(TimeSpan.FromSeconds(2), $"200 blocks took {best.TotalMilliseconds:F0} ms");
    }
}
