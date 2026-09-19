using FluentAssertions;
using NSubstitute;
using Omnimud.Core.Scripting;

namespace Omnimud.Core.Tests.Scripting;

/// <summary>Every om.* function against a fake <see cref="IScriptHost"/>.</summary>
public sealed class LuaScriptEngineApiTests : IDisposable
{
    private readonly LuaScriptEngine _sut = new();
    private readonly IScriptHost _host = Substitute.For<IScriptHost>();
    private readonly ScriptContext _context = new()
    {
        ScriptName = "mi trigger",
        MatchedLine = "Gandalf te dice: 'hola'",
        Block = "linea uno\nGandalf te dice: 'hola'",
        Captures = ["Gandalf", "hola"],
        MudName = "Reinos de Leyenda",
        CharacterName = "Frodo"
    };

    public void Dispose() => _sut.Dispose();

    private async Task<ScriptResult> RunAsync(string script, ScriptContext? context = null)
    {
        var result = await _sut.ExecuteAsync(script, context ?? _context, _host);
        return result;
    }

    private async Task RunOkAsync(string script, ScriptContext? context = null)
    {
        var result = await RunAsync(script, context);
        result.Success.Should().BeTrue(result.Error);
    }

    [Fact]
    public async Task Send_GoesThroughTheHostPipeline()
    {
        await RunOkAsync("om.send('mirar') om.send(5) om.send('')");

        Received.InOrder(() =>
        {
            _host.Send("mirar");
            _host.Send("5");
        });
        _host.DidNotReceive().Send("");
        _host.DidNotReceiveWithAnyArgs().SendRaw(default!);
    }

    [Fact]
    public async Task SendRaw_GoesStraightToTheMud()
    {
        await RunOkAsync("om.sendraw('n')");

        _host.Received(1).SendRaw("n");
        _host.DidNotReceiveWithAnyArgs().Send(default!);
    }

    [Fact]
    public async Task WithHost_ResultListsStayEmpty_BecauseEffectsWereApplied()
    {
        var result = await RunAsync("om.send('mirar') om.display('x') om.notify('y')");

        result.Success.Should().BeTrue();
        result.CommandsToSend.Should().BeEmpty();
        result.DisplayMessages.Should().BeEmpty();
        result.Notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task DisplayEchoAndPrint_ReachTheHost()
    {
        await RunOkAsync("om.display('del mud') om.echo('solo pintar') print('a', 1)");

        _host.Received(1).Display("del mud");
        _host.Received(1).Echo("solo pintar");
        _host.Received(1).Echo(Arg.Is<string>(s => s.StartsWith('a') && s.Contains('1')));
    }

    [Fact]
    public async Task SayAndNotify_MapToSayWithInterruptFlag()
    {
        await RunOkAsync("om.say('uno') om.say('dos', true) om.say('tres', false) om.notify('cuatro')");

        Received.InOrder(() =>
        {
            _host.Say("uno", false);
            _host.Say("dos", true);
            _host.Say("tres", false);
            _host.Say("cuatro", false);
        });
    }

    [Fact]
    public async Task MessageStatusLogAndGag_ReachTheHost()
    {
        await RunOkAsync("om.message('al cuadro') om.status('Luchando') om.log('al registro') om.gag()");

        _host.Received(1).AddMessage("al cuadro");
        _host.Received(1).SetStatus("Luchando");
        _host.Received(1).Log("al registro");
        _host.Received(1).Gag();
    }

    [Fact]
    public async Task PlaySound_UsesDefaultsOptionsAndTheLoopShorthand()
    {
        await RunOkAsync("""
            om.playsound('ding.wav')
            om.playsound('musica.mp3', { loop = -1, volume = 40, priority = 80 })
            om.playsound('tres.wav', 3)
            om.playsound('raro.wav', { loop = 0, volume = 500, priority = -3 })
            """);

        Received.InOrder(() =>
        {
            _host.PlaySound("ding.wav", 1, 100, 50);
            _host.PlaySound("musica.mp3", -1, 40, 80);
            _host.PlaySound("tres.wav", 3, 100, 50);
            _host.PlaySound("raro.wav", 1, 100, 0);
        });
    }

    [Fact]
    public async Task StopSound_ReturnsWhatTheHostSays()
    {
        _host.StopSound("musica.mp3").Returns(true);

        await RunOkAsync("om.send(tostring(om.stopsound('musica.mp3')) .. ' ' .. tostring(om.stopsound('otro.wav')))");

        _host.Received(1).Send("true false");
    }

    [Fact]
    public async Task Variables_LiveInTheHost()
    {
        _host.GetVariable("objetivo").Returns("orco");
        _host.GetVariable("nada").Returns((string?)null); // NSubstitute would answer "" by default
        _host.IsVariableSet("objetivo").Returns(true);
        _host.RemoveVariable("objetivo").Returns(true);

        await RunOkAsync("""
            om.setvar('pv', 100)
            om.setvar('activo', true)
            om.setvar('vacio')
            om.send(om.getvar('objetivo') .. ' ' .. tostring(om.getvar('nada')))
            om.send(tostring(om.isset('objetivo')) .. ' ' .. tostring(om.isset('nada')))
            om.send(tostring(om.removevar('objetivo')) .. ' ' .. tostring(om.removevar('nada')))
            """);

        _host.Received(1).SetVariable("pv", "100");
        _host.Received(1).SetVariable("activo", "true");
        _host.Received(1).SetVariable("vacio", "");
        _host.Received(1).Send("orco nil");
        _host.Received(2).Send("true false");
        _context.Variables.Should().BeEmpty("with a host, variables do not live in the context");
    }

    [Fact]
    public async Task LastActivity_ReturnsSeconds()
    {
        _host.SecondsSinceLastActivity.Returns(12.5);

        await RunOkAsync("om.send(tostring(om.lastactivity()))");

        _host.Received(1).Send("12.5");
    }

    [Fact]
    public async Task RemoveColors_StripsAnsiSequences()
    {
        await RunOkAsync("om.send(om.removecolors(om.ANSI_RED .. 'rojo' .. om.ANSI_RESET .. '\\27[1;37;44m y azul\\27[0m'))");

        _host.Received(1).Send("rojo y azul");
    }

    [Fact]
    public async Task Match_ReturnsCapturesOrNil()
    {
        await RunOkAsync("""
            local m = om.match('Pv: 50/120 Pg: 7', [[(\d+)/(\d+)]])
            om.send(m[0] .. ' ' .. m[1] .. ' ' .. m[2])
            local named = om.match('Gandalf llega', [[^(?<quien>\w+) llega]])
            om.send(named.quien .. ' ' .. named[1])
            om.send(tostring(om.match('nada', [[\d+]])))
            om.send(tostring(om.match('HOLA', '(?i)hola') ~= nil))
            """);

        Received.InOrder(() =>
        {
            _host.Send("50/120 50 120");
            _host.Send("Gandalf Gandalf");
            _host.Send("nil");
            _host.Send("true");
        });
    }

    [Fact]
    public async Task Match_InvalidOrCatastrophicPattern_IsALuaError()
    {
        var invalid = await RunAsync("om.match('x', '(')");
        var slow = await RunAsync("om.match(string.rep('a', 40) .. '!', '^(a+)+$')");

        invalid.Success.Should().BeFalse();
        invalid.Error.Should().Contain("om.match").And.Contain("invalid pattern");
        slow.Success.Should().BeFalse();
        slow.Error.Should().Contain("om.match").And.Contain("too long");
    }

    [Fact]
    public async Task ContextData_IsExposed()
    {
        await RunOkAsync("""
            om.send(om.line)
            om.send(om.block)
            om.send(#om.captures .. ' ' .. om.captures[1] .. ' ' .. om.captures[2])
            om.send(tostring(om.args == om.captures) .. ' ' .. tostring(om.command))
            om.send(om.mud.name .. ' / ' .. om.character.name)
            """);

        Received.InOrder(() =>
        {
            _host.Send("Gandalf te dice: 'hola'");
            _host.Send("linea uno\nGandalf te dice: 'hola'");
            _host.Send("2 Gandalf hola");
            _host.Send("true nil");
            _host.Send("Reinos de Leyenda / Frodo");
        });
    }

    [Fact]
    public async Task CommandTrigger_ExposesArgsAndFullCommand()
    {
        var context = new ScriptContext { Captures = ["orco", "rapido"], FullCommand = "@matar orco rapido" };

        await RunOkAsync("om.send(om.command .. '|' .. om.args[1] .. '|' .. om.args[2] .. '|' .. om.block .. '|' .. om.mud.name)", context);

        _host.Received(1).Send("@matar orco rapido|orco|rapido||");
    }

    [Theory]
    [InlineData("ANSI_RESET", 0)]
    [InlineData("ANSI_BOLD", 1)]
    [InlineData("ANSI_DIM", 2)]
    [InlineData("ANSI_ITALIC", 3)]
    [InlineData("ANSI_UNDERLINE", 4)]
    [InlineData("ANSI_BLINK", 5)]
    [InlineData("ANSI_INVERSE", 7)]
    [InlineData("ANSI_HIDDEN", 8)]
    [InlineData("ANSI_STRIKE", 9)]
    [InlineData("ANSI_BOLD_OFF", 22)]
    [InlineData("ANSI_ITALIC_OFF", 23)]
    [InlineData("ANSI_UNDERLINE_OFF", 24)]
    [InlineData("ANSI_BLINK_OFF", 25)]
    [InlineData("ANSI_INVERSE_OFF", 27)]
    [InlineData("ANSI_HIDDEN_OFF", 28)]
    [InlineData("ANSI_STRIKE_OFF", 29)]
    [InlineData("ANSI_BLACK", 30)]
    [InlineData("ANSI_RED", 31)]
    [InlineData("ANSI_GREEN", 32)]
    [InlineData("ANSI_YELLOW", 33)]
    [InlineData("ANSI_BLUE", 34)]
    [InlineData("ANSI_MAGENTA", 35)]
    [InlineData("ANSI_CYAN", 36)]
    [InlineData("ANSI_WHITE", 37)]
    [InlineData("ANSI_DEFAULT", 39)]
    [InlineData("ANSI_BG_BLACK", 40)]
    [InlineData("ANSI_BG_RED", 41)]
    [InlineData("ANSI_BG_GREEN", 42)]
    [InlineData("ANSI_BG_YELLOW", 43)]
    [InlineData("ANSI_BG_BLUE", 44)]
    [InlineData("ANSI_BG_MAGENTA", 45)]
    [InlineData("ANSI_BG_CYAN", 46)]
    [InlineData("ANSI_BG_WHITE", 47)]
    [InlineData("ANSI_BG_DEFAULT", 49)]
    public async Task AnsiConstants_AreCompleteEscapeSequences(string name, int code)
    {
        await RunOkAsync($"om.echo(om.{name})");

        _host.Received(1).Echo($"[{code}m");
    }

    [Theory]
    [InlineData("om.send()", "om.send: argument #1 must be a string (got nil)")]
    [InlineData("om.send({})", "om.send: argument #1 must be a string (got table)")]
    [InlineData("om.sendraw(nil)", "om.sendraw: argument #1")]
    [InlineData("om.display(true)", "om.display: argument #1 must be a string (got boolean)")]
    [InlineData("om.echo()", "om.echo: argument #1")]
    [InlineData("om.say()", "om.say: argument #1")]
    [InlineData("om.message(print)", "om.message: argument #1 must be a string (got function)")]
    [InlineData("om.playsound('')", "om.playsound: argument #1 must not be empty")]
    [InlineData("om.playsound('a.wav', 'fuerte')", "om.playsound: argument #2 must be a table of options")]
    [InlineData("om.playsound('a.wav', { volume = 'alto' })", "om.playsound: option 'volume' must be a number")]
    [InlineData("om.stopsound()", "om.stopsound: argument #1")]
    [InlineData("om.setvar('x', {})", "om.setvar: argument #2")]
    [InlineData("om.getvar()", "om.getvar: argument #1")]
    [InlineData("om.match('x')", "om.match: argument #2")]
    [InlineData("om.sleep('mucho')", "om.sleep: argument #1 must be a number")]
    [InlineData("om.sleep(-1)", "om.sleep: seconds must be zero or positive")]
    [InlineData("om.get()", "om.get: argument #1")]
    [InlineData("om.get('pv', { timeout = 0 })", "om.get: timeout must be a positive number")]
    [InlineData("om.get('pv', { pattern = '(' })", "om.get: invalid pattern")]
    [InlineData("om.countdown()", "om.countdown: argument #1 must be a number")]
    [InlineData("om.countdown(3, { unit = 'h' })", "om.countdown: invalid unit 'h'")]
    [InlineData("om.countdown(3, 's')", "om.countdown: argument #2 must be a table of options")]
    [InlineData("om.timer('t', 1)", "om.timer: argument #3 must be a function")]
    [InlineData("om.timer('', 1, print)", "om.timer: argument #1 must not be empty")]
    [InlineData("om.timer('t', 'pronto', print)", "om.timer: argument #2 must be a number")]
    [InlineData("om.canceltimer()", "om.canceltimer: argument #1")]
    public async Task BadArguments_GiveClearLuaErrors(string script, string expected)
    {
        var result = await RunAsync(script);

        result.Success.Should().BeFalse();
        result.ErrorKind.Should().Be(ScriptErrorKind.Runtime);
        result.Error.Should().Contain(expected);
        result.ErrorLine.Should().Be(1);
    }

    [Fact]
    public async Task BadArguments_CanBeCaughtWithPcall()
    {
        await RunOkAsync("local ok, err = pcall(om.send) om.send(tostring(ok) .. ' ' .. tostring(err))");

        _host.Received(1).Send(Arg.Is<string>(s => s.StartsWith("false") && s.Contains("om.send: argument #1")));
    }

    // ---- errors ----

    [Fact]
    public async Task RuntimeError_FailsWithLineNumberAndIsReportedToTheHost()
    {
        const string script = "om.send('uno')\nlocal t = nil\nom.send(t.campo)\nom.send('nunca')";

        var result = await RunAsync(script);

        result.Success.Should().BeFalse();
        result.ErrorKind.Should().Be(ScriptErrorKind.Runtime);
        result.ErrorLine.Should().Be(3);
        result.Error.Should().Contain("line 3").And.Contain("attempt to index a nil value");
        _host.Received(1).Send("uno");
        _host.DidNotReceive().Send("nunca");
        _host.Received(1).ReportScriptError("mi trigger", Arg.Is<string>(e => e.Contains("line 3")));
    }

    [Fact]
    public async Task ErrorAfterMultilineStringAndTable_ReportsTheRightLine()
    {
        const string script = "local s = [[uno\ndos\ntres]]\nlocal t = {\n  1,\n  2\n}\nerror('en la ocho')";

        var result = await RunAsync(script);

        result.ErrorLine.Should().Be(8);
        result.Error.Should().Contain("line 8").And.Contain("en la ocho");
    }

    [Fact]
    public async Task SyntaxError_FailsWithLineNumberAndIsReportedToTheHost()
    {
        var result = await RunAsync("om.send('uno')\nif x then\nom.send('dos')");

        result.Success.Should().BeFalse();
        result.ErrorKind.Should().Be(ScriptErrorKind.Syntax);
        result.ErrorLine.Should().NotBeNull();
        result.Error.Should().Contain("line ");
        _host.DidNotReceiveWithAnyArgs().Send(default!);
        _host.Received(1).ReportScriptError("mi trigger", Arg.Any<string>());
    }

    [Fact]
    public async Task LimitExceeded_IsReportedToTheHost()
    {
        var result = await _sut.ExecuteAsync("while true do end", _context, _host, new ScriptLimits { MaxInstructions = 5_000 });

        result.ErrorKind.Should().Be(ScriptErrorKind.InstructionLimit);
        _host.Received(1).ReportScriptError("mi trigger", result.Error!);
    }

    [Fact]
    public async Task HostThatThrows_BecomesALuaErrorAndNeverEscapes()
    {
        _host.When(h => h.Send("boom")).Do(_ => throw new InvalidOperationException("sin conexion"));

        var result = await RunAsync("om.send('boom')");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("om.send: sin conexion");
        _host.Received(1).ReportScriptError("mi trigger", Arg.Any<string>());
    }

    [Fact]
    public async Task HostWhoseErrorReportThrows_StillReturnsAResult()
    {
        _host.When(h => h.ReportScriptError(Arg.Any<string>(), Arg.Any<string>())).Do(_ => throw new InvalidOperationException());

        var result = await RunAsync("error('x')");

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task EmptyScript_SucceedsWithoutTouchingTheHost()
    {
        var result = await RunAsync("   ");

        result.Success.Should().BeTrue();
        _host.ReceivedCalls().Should().BeEmpty();
    }

    // ---- Validate ----

    [Theory]
    [InlineData("")]
    [InlineData("om.send('x')")]
    [InlineData("local t = {\n 1,\n 2\n}\nwhile true do end")]
    [InlineData("io.open('x')")] // valid Lua; it only fails when run
    public void Validate_ValidScript_ReturnsNull(string script)
    {
        _sut.Validate(script).Should().BeNull();
    }

    [Fact]
    public void Validate_InvalidScript_ReturnsErrorWithLine_AndDoesNotRunAnything()
    {
        var error = _sut.Validate("om.send('se ejecuta?')\nlocal x = = 2");

        error.Should().NotBeNull();
        error.Should().Contain("line 2");
    }

    // ---- concurrency ----

    [Fact]
    public async Task ManyScriptsAtOnce_DoNotShareLuaState()
    {
        var host = new RecordingHost();
        var tasks = Enumerable.Range(1, 40).Select(i => _sut.ExecuteAsync(
            $"counter = (counter or 0) + {i} local s = 0 for k = 1, 200 do s = s + k end om.send(counter .. ':' .. s)",
            new ScriptContext(), host)).ToArray();

        var results = await Task.WhenAll(tasks);

        results.Should().OnlyContain(r => r.Success);
        host.Sent.Should().BeEquivalentTo(Enumerable.Range(1, 40).Select(i => $"{i}:20100"));
    }
}
