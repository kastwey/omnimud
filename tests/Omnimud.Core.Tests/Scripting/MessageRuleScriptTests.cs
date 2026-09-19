using FluentAssertions;
using NSubstitute;
using Omnimud.Core.Messages;
using Omnimud.Core.Scripting;

namespace Omnimud.Core.Tests.Scripting;

/// <summary>om.lines, the "no waiting" context and <see cref="MessageRuleScript"/>: a script as a pure function of a block.</summary>
public sealed class MessageRuleScriptTests : IDisposable
{
    private readonly LuaScriptEngine _engine = new();

    public void Dispose() => _engine.Dispose();

    private Task<MessageScriptResult> RunAsync(string script, params string[] lines)
        => MessageRuleScript.RunAsync(_engine, script, lines, "Reinos", "Zork");

    // ── The two examples of docs/API_LUA.md, section 10 ────────────────────

    [Fact]
    public async Task DocExample_MessagesOfSeveralLines()
    {
        var result = await RunAsync("""
            local lines = om.lines
            local i = 1
            while i <= #lines do
              if om.match(lines[i], [[^\w+ (dice|susurra|grita) ']]) then
                local last = i
                while last < #lines and string.sub(lines[last], -1) ~= "'" do
                  last = last + 1
                end
                om.message(table.concat(lines, "\n", i, last))
                i = last + 1
              else
                i = i + 1
              end
            end
            """, "Un orco llega.", "Ana dice 'hola, cuanto", "tiempo'", "Beto grita 'eh'", "Fin.");

        result.Success.Should().BeTrue(result.Error);
        result.Messages.Should().Equal("Ana dice 'hola, cuanto\ntiempo'", "Beto grita 'eh'");
    }

    [Fact]
    public async Task DocExample_ChannelsAccumulatedInOneMessage()
    {
        var result = await RunAsync("""
            local found = {}
            for _, line in ipairs(om.lines) do
              if om.match(line, [[^\[(Chat|Clan|Novatos)\] ]]) then
                found[#found + 1] = line
              end
            end
            if #found > 0 then om.message(table.concat(found, "\n")) end
            """, "[Chat] Ana: hola", "Un orco llega.", "[Otro] no", "[Clan] Beto: a las armas");

        result.Messages.Should().Equal("[Chat] Ana: hola\n[Clan] Beto: a las armas");
    }

    // ── om.lines ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Lines_IsA1BasedTable_WithTheLinesOfTheContext()
    {
        var context = new ScriptContext { Block = "uno\ndos\n\ncuatro", Lines = ["uno", "dos", "", "cuatro"] };
        var result = await _engine.ExecuteAsync(
            "om.message(#om.lines .. '|' .. om.lines[1] .. '|' .. om.lines[2] .. '|' .. om.lines[3] .. '|' .. om.lines[4] .. '|' .. tostring(om.lines[0]) .. '|' .. tostring(om.lines[5]))",
            context);

        result.Success.Should().BeTrue(result.Error);
        result.Messages.Should().Equal("4|uno|dos||cuatro|nil|nil");
    }

    [Fact]
    public async Task Lines_WithoutExplicitLines_ComeFromTheBlock_OrFromTheLine()
    {
        const string script = "local t = {} for i, l in ipairs(om.lines) do t[#t + 1] = i .. '=' .. l end om.message(table.concat(t, ','))";

        var fromBlock = await _engine.ExecuteAsync(script, new ScriptContext { MatchedLine = "dos", Block = "uno\r\ndos\ntres" });
        fromBlock.Messages.Should().Equal("1=uno,2=dos,3=tres");

        var fromLine = await _engine.ExecuteAsync(script, new ScriptContext { MatchedLine = "sola" });
        fromLine.Messages.Should().Equal("1=sola");
    }

    [Fact]
    public async Task Lines_IsAlsoThereForTriggersWithAHost()
    {
        var host = Substitute.For<IScriptHost>();
        var result = await _engine.ExecuteAsync("om.send(om.lines[2])", new ScriptContext { MatchedLine = "a", Block = "a\nb" }, host);

        result.Success.Should().BeTrue(result.Error);
        host.Received(1).Send("b");
    }

    [Fact]
    public async Task Lines_AndBlock_DescribeTheSameText()
    {
        var result = await RunAsync("om.message(om.block) om.message(table.concat(om.lines, '\\n')) om.message(om.line)", "Ana dice 'hola", "que tal'", "");

        result.Messages.Should().Equal("Ana dice 'hola\nque tal'\n", "Ana dice 'hola\nque tal'\n", "Ana dice 'hola");
    }

    // ── Pure function: only om.message counts ──────────────────────────────

    [Fact]
    public async Task OnlyMessagesCount_EveryOtherEffectIsIgnored()
    {
        var result = await RunAsync("""
            om.send('matar orco') om.sendraw('x') om.display('pintado') om.echo('eco') print('impreso')
            om.say('hablado', true) om.notify('aviso') om.status('estado') om.log('registro') om.gag()
            om.playsound('ding.wav') om.stopsound('ding.wav') om.canceltimer('nada')
            om.setvar('v', 1)
            om.message('uno')
            om.message('')
            om.message('   ')
            om.message(om.getvar('v') .. ' ' .. om.mud.name .. ' ' .. om.character.name)
            """, "linea");

        result.Success.Should().BeTrue(result.Error);
        result.Messages.Should().Equal("uno", "1 Reinos Zork");
    }

    [Fact]
    public async Task VariablesDoNotSurviveFromOneBlockToTheNext()
    {
        await RunAsync("om.setvar('visto', 'si')", "a");
        var second = await RunAsync("om.message(tostring(om.getvar('visto')))", "b");

        second.Messages.Should().Equal("nil");
    }

    [Fact]
    public async Task EmptyScript_AndEmptyBlock()
    {
        (await RunAsync("   ", "linea")).Should().BeEquivalentTo(new { Success = true, Messages = Array.Empty<string>() });
        MessageRuleScript.IsScript(null).Should().BeFalse();
        MessageRuleScript.IsScript(" \n").Should().BeFalse();
        MessageRuleScript.IsScript("om.message('x')").Should().BeTrue();

        var noLines = await MessageRuleScript.RunAsync(_engine, "om.message(#om.lines .. '')", []);
        noLines.Messages.Should().Equal("0");
    }

    [Fact]
    public void SplitLines_AcceptsEveryLineEnding()
    {
        MessageRuleScript.SplitLines("a\r\nb\nc\rd").Should().Equal("a", "b", "c", "d");
        MessageRuleScript.SplitLines("").Should().Equal("");
        MessageRuleScript.SplitLines("a\n").Should().Equal("a", "");
    }

    // ── No waiting ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("om.sleep(0.01)", "om.sleep")]
    [InlineData("om.get('pv')", "om.get")]
    [InlineData("om.countdown(1)", "om.countdown")]
    [InlineData("om.timer('t', 1, function() end)", "om.timer")]
    public async Task Waiting_IsAnError_InAMessageRulesScript(string call, string function)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = await RunAsync($"om.message('antes') {call} om.message('despues')", "linea");

        result.Success.Should().BeFalse();
        result.ErrorKind.Should().Be(ScriptErrorKind.Runtime);
        result.LimitExceeded.Should().BeFalse();
        result.Error.Should().Contain(function).And.Contain("not available");
        result.Messages.Should().BeEmpty("a failed script produces nothing");
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        _engine.ActiveTimerCount.Should().Be(0);
    }

    [Fact]
    public async Task Waiting_CanBeCaughtWithPcall_AndTheScriptGoesOn()
    {
        var result = await RunAsync("local ok, err = pcall(om.sleep, 1) om.message(tostring(ok)) om.message('sigue')", "linea");

        result.Success.Should().BeTrue(result.Error);
        result.Messages.Should().Equal("false", "sigue");
    }

    [Fact]
    public async Task Waiting_StillWorks_InOrdinaryScripts()
    {
        var result = await _engine.ExecuteAsync("om.sleep(0) om.message('ok')", new ScriptContext());

        result.Success.Should().BeTrue(result.Error);
        result.Messages.Should().Equal("ok");
        new ScriptContext().AllowWaiting.Should().BeTrue();
    }

    // ── Limits and errors ──────────────────────────────────────────────────

    [Fact]
    public void Limits_AreTight()
    {
        MessageRuleScript.Limits.MaxExecutionTime.Should().Be(TimeSpan.FromMilliseconds(100));
        MessageRuleScript.Limits.MaxInstructions.Should().Be(50_000);
        MessageRuleScript.Limits.MaxTotalTime.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task InfiniteLoop_IsCutQuickly_AndReportedAsALimit()
    {
        await MessageRuleScript.WarmUpAsync(_engine, "om.message('x')");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = await RunAsync("om.message('antes') while true do end", "linea");

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        result.Success.Should().BeFalse();
        result.LimitExceeded.Should().BeTrue();
        result.ErrorKind.Should().BeOneOf(ScriptErrorKind.InstructionLimit, ScriptErrorKind.Timeout);
        result.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task SlowNativeCall_IsCutByTheClock()
    {
        await MessageRuleScript.WarmUpAsync(_engine, "om.message('x')");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        // Few instructions, a lot of time: each om.match burns up to its own 100 ms on a catastrophic pattern.
        var result = await RunAsync("for i = 1, 50 do om.match(string.rep('a', 40) .. '!', '^(a+)+$') end om.message('nunca')", "linea");

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
        result.Success.Should().BeFalse();
        result.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task RuntimeAndSyntaxErrors_ComeBackWithTheirLine_NeverAsExceptions()
    {
        var runtime = await RunAsync("local a = 1\nlocal b = nil\nom.message(b.campo)", "linea");
        runtime.Success.Should().BeFalse();
        runtime.ErrorKind.Should().Be(ScriptErrorKind.Runtime);
        runtime.ErrorLine.Should().Be(3);
        runtime.Error.Should().Contain("line 3");

        var syntax = await RunAsync("om.message('a')\nif then", "linea");
        syntax.Success.Should().BeFalse();
        syntax.ErrorKind.Should().Be(ScriptErrorKind.Syntax);
        syntax.ErrorLine.Should().Be(2);
    }

    [Fact]
    public async Task ABrokenEngine_IsAFailedResult_NotAnException()
    {
        var throwing = Substitute.For<IScriptEngine>();
        throwing.ExecuteAsync(Arg.Any<string>(), Arg.Any<ScriptContext>(), Arg.Any<ScriptLimits?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ScriptResult>>(_ => throw new InvalidOperationException("roto"));
        var failed = await MessageRuleScript.RunAsync(throwing, "om.message('x')", ["a"]);
        failed.Success.Should().BeFalse();
        failed.Error.Should().Be("roto");

        var nothing = Substitute.For<IScriptEngine>();
        nothing.ExecuteAsync(Arg.Any<string>(), Arg.Any<ScriptContext>(), Arg.Any<ScriptLimits?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ScriptResult>(null!));
        (await MessageRuleScript.RunAsync(nothing, "om.message('x')", ["a"])).Should().BeEquivalentTo(new { Success = true, Messages = Array.Empty<string>() });

        _engine.Dispose();
        var disposed = await RunAsync("om.message('x')", "a");
        disposed.ErrorKind.Should().Be(ScriptErrorKind.EngineDisposed);
        await MessageRuleScript.WarmUpAsync(_engine, "om.message('x')"); // does not throw either
    }

    [Fact]
    public async Task TheContextGivenToTheEngine_IsAPureOne()
    {
        var engine = Substitute.For<IScriptEngine>();
        ScriptContext? seen = null;
        ScriptLimits? limits = null;
        engine.ExecuteAsync(Arg.Any<string>(), Arg.Do<ScriptContext>(c => seen = c), Arg.Do<ScriptLimits?>(l => limits = l), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ScriptResult { Success = true, Messages = ["m"], CommandsToSend = ["ignorado"] }));

        var result = await MessageRuleScript.RunAsync(engine, "script", ["a", "b"], "Mud", "Pj");

        result.Messages.Should().Equal("m");
        seen!.AllowWaiting.Should().BeFalse();
        seen.Lines.Should().Equal("a", "b");
        seen.Block.Should().Be("a\nb");
        seen.MudName.Should().Be("Mud");
        seen.CharacterName.Should().Be("Pj");
        limits.Should().BeSameAs(MessageRuleScript.Limits);
        await engine.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default!, default(IScriptHost)!, default, default);
    }

    [Fact]
    public async Task OmMatch_CachesItsPatterns_AndStillReportsBadOnes()
    {
        var result = await RunAsync("""
            for i = 1, 3 do
              local m = om.match('Ana dice hola', [[^(?<quien>\w+) dice (.+)$]])
              om.message(m.quien .. ':' .. m[1])
            end
            local ok, err = pcall(om.match, 'x', '(')
            om.message(tostring(ok))
            """, "linea");

        result.Success.Should().BeTrue(result.Error);
        result.Messages.Should().Equal("Ana:hola", "Ana:hola", "Ana:hola", "false");
    }
}
