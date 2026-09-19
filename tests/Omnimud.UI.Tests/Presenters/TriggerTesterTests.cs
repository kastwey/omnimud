using System.Diagnostics;
using Omnimud.Core.Scripting;
using Omnimud.Core.Triggers;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Tests.Presenters;

public sealed class TriggerTesterTests : IDisposable
{
    private readonly LuaScriptEngine _engine = new();
    private readonly TriggerTester _sut;

    public TriggerTesterTests() => _sut = new TriggerTester(_engine);

    public void Dispose() => _engine.Dispose();

    private static TriggerDefinition Trigger(string pattern, PatternType type = PatternType.Literal, string action = "hacer algo",
        TriggerActionType actionType = TriggerActionType.SendCommand, bool caseSensitive = false, bool multiline = false,
        bool gag = false, string? sound = null) => new()
    {
        Id = "t", Name = "prueba", Pattern = pattern, PatternType = type, Action = action, ActionType = actionType,
        CaseSensitive = caseSensitive, Multiline = multiline, GagLine = gag, Sound = sound,
    };

    [Fact]
    public async Task EmptySample_AsksForOne()
    {
        var result = await _sut.TestAsync(Trigger("x"), "");
        result.Matched.Should().BeFalse();
        result.Describe().Should().Be(Strings.TrigTest_ErrNoSample);
    }

    [Fact]
    public async Task Literal_MatchesAnywhere_IgnoringCaseByDefault()
    {
        var result = await _sut.TestAsync(Trigger("tienes hambre", action: "comer pan"), "Notas que TIENES HAMBRE de verdad");
        result.Matched.Should().BeTrue();
        result.Captures.Should().BeEmpty();
        result.Command.Should().Be("comer pan");
        result.Describe().Should().Contain(Strings.TrigTest_Matches).And.Contain(Strings.TrigTest_NoCaptures)
            .And.Contain(string.Format(Strings.TrigTest_Command, "comer pan"));
    }

    [Fact]
    public async Task Literal_CaseSensitive_DoesNotMatchOtherCase()
    {
        var result = await _sut.TestAsync(Trigger("Hambre", caseSensitive: true), "tienes hambre");
        result.Matched.Should().BeFalse();
        result.Describe().Should().Be(Strings.TrigTest_NoMatch);
    }

    [Theory]
    [InlineData("^Hola", "Hola mundo", true)]
    [InlineData("^Hola", "Digo Hola", false)]
    [InlineData("mundo$", "Hola mundo", true)]
    [InlineData("mundo$", "mundo cruel", false)]
    [InlineData("^Hola mundo$", "Hola mundo", true)]
    [InlineData("^Hola mundo$", "Hola mundo!", false)]
    public async Task Literal_Anchors(string pattern, string sample, bool expected)
    {
        (await _sut.TestAsync(Trigger(pattern), sample)).Matched.Should().Be(expected);
    }

    [Fact]
    public async Task Regex_GroupsBecomeCaptures_AndAreSubstitutedInTheCommand()
    {
        var trigger = Trigger(@"^(\w+) te da (\d+) monedas", PatternType.Regex, action: "decir gracias %1 por las %2");
        var result = await _sut.TestAsync(trigger, "Bilbo te da 25 monedas de oro.");

        result.Matched.Should().BeTrue();
        result.Captures.Should().Equal("Bilbo", "25");
        result.Command.Should().Be("decir gracias Bilbo por las 25");
        result.Describe().Should().Contain(string.Format(Strings.TrigTest_Capture, 1, "Bilbo")).And.Contain(string.Format(Strings.TrigTest_Capture, 2, "25"));
    }

    [Fact]
    public async Task Regex_Invalid_IsAnErrorNotAnException()
    {
        var result = await _sut.TestAsync(Trigger("(abc", PatternType.Regex), "abc");
        result.Matched.Should().BeFalse();
        result.Describe().Should().Be(Strings.TrigTest_ErrBadRegex);
    }

    [Fact]
    public async Task Wildcards_CaptureWordsAndNumbers()
    {
        var trigger = Trigger("%s te golpea por %d puntos", PatternType.Sscanf, action: "huir de %1");
        var result = await _sut.TestAsync(trigger, "Orco te golpea por 12 puntos");

        result.Matched.Should().BeTrue();
        result.Captures.Should().Equal("Orco", "12");
        result.Command.Should().Be("huir de Orco");
    }

    [Fact]
    public async Task LineTrigger_LooksAtEachLineOfTheSample_MultilineAtTheWholeBlock()
    {
        const string sample = "primera linea\nsegunda linea";
        (await _sut.TestAsync(Trigger("^segunda"), sample)).Matched.Should().BeTrue();
        (await _sut.TestAsync(Trigger(@"primera linea\nsegunda", PatternType.Regex), sample)).Matched.Should().BeFalse();
        (await _sut.TestAsync(Trigger(@"primera linea\nsegunda", PatternType.Regex, multiline: true), sample)).Matched.Should().BeTrue();
    }

    [Fact]
    public async Task Sound_Both_AndHideLine_AreReported()
    {
        var result = await _sut.TestAsync(Trigger("alarma", actionType: TriggerActionType.SendCommandAndPlaySound, sound: "sirena.wav", gag: true), "suena la alarma");
        result.Command.Should().Be("hacer algo");
        result.Sound.Should().Be("sirena.wav");
        result.HidesLine.Should().BeTrue();
        result.Describe().Should().Contain(string.Format(Strings.TrigTest_Sound, "sirena.wav")).And.Contain(Strings.TrigTest_HidesLine);

        var soundOnly = await _sut.TestAsync(Trigger("alarma", actionType: TriggerActionType.PlaySound, sound: "sirena.wav"), "alarma");
        soundOnly.Command.Should().BeNull();
        soundOnly.Sound.Should().Be("sirena.wav");
    }

    [Fact]
    public async Task CommandTrigger_MatchesTheFirstWord_AndTheRestAreTheCaptures()
    {
        var trigger = Trigger("@curar", action: "formular curar heridas %1 %2");
        var result = await _sut.TestAsync(trigger, "CURAR frodo rapido");

        result.Matched.Should().BeTrue();
        result.Captures.Should().Equal("frodo", "rapido");
        result.Command.Should().Be("formular curar heridas frodo rapido");

        (await _sut.TestAsync(trigger, "curarme frodo")).Matched.Should().BeFalse();
        (await _sut.TestAsync(Trigger("@curar", caseSensitive: true), "CURAR frodo")).Matched.Should().BeFalse();
    }

    [Fact]
    public async Task Lua_EffectsAreCollected_InOrder_AndNothingIsReallyDone()
    {
        const string script = """
            om.send('mirar ' .. om.captures[1])
            om.sendraw('crudo')
            om.display('mostrado')
            om.echo('pintado')
            om.say('dicho')
            om.message('mensaje')
            om.playsound('ding.wav')
            om.setvar('vida', '10')
            om.send('vida ' .. om.getvar('vida'))
            """;
        var trigger = Trigger(@"llega (\w+)", PatternType.Regex, script, TriggerActionType.Script);

        var result = await _sut.TestAsync(trigger, "llega Gimli");

        result.Matched.Should().BeTrue();
        result.ScriptRan.Should().BeTrue();
        result.Error.Should().BeNull();
        result.ScriptEffects.Should().Equal(
            string.Format(Strings.TrigTest_FxSend, "mirar Gimli"),
            string.Format(Strings.TrigTest_FxSendRaw, "crudo"),
            string.Format(Strings.TrigTest_FxDisplay, "mostrado"),
            string.Format(Strings.TrigTest_FxEcho, "pintado"),
            string.Format(Strings.TrigTest_FxSay, "dicho"),
            string.Format(Strings.TrigTest_FxMessage, "mensaje"),
            string.Format(Strings.TrigTest_FxPlaySound, "ding.wav"),
            string.Format(Strings.TrigTest_FxSetVar, "vida", "10"),
            string.Format(Strings.TrigTest_FxSend, "vida 10"));
        result.Describe().Should().Contain(string.Format(Strings.TrigTest_FxSay, "dicho"));
    }

    [Fact]
    public async Task Lua_CommandTrigger_SeesArgsAndTheFullCommand()
    {
        var trigger = Trigger("@eco", action: "om.send(om.command .. '|' .. om.args[2])", actionType: TriggerActionType.Script);
        var result = await _sut.TestAsync(trigger, "eco uno dos");
        result.ScriptEffects.Should().Equal(string.Format(Strings.TrigTest_FxSend, "eco uno dos|dos"));
    }

    [Fact]
    public async Task Lua_Gag_IsReportedAsHidingTheLine()
    {
        var result = await _sut.TestAsync(Trigger("spam", action: "om.gag()", actionType: TriggerActionType.Script), "spam spam");
        result.HidesLine.Should().BeTrue();
    }

    [Fact]
    public async Task Lua_ScriptThatDoesNothing_SaysSo()
    {
        var result = await _sut.TestAsync(Trigger("x", action: "local a = 1", actionType: TriggerActionType.Script), "x");
        result.Describe().Should().Contain(Strings.TrigTest_ScriptNothing);
    }

    [Fact]
    public async Task Lua_RuntimeError_IsShownWithItsLine_AndWhatHappenedBefore()
    {
        const string script = "om.send('antes')\nlocal t = nil\nom.send(t.campo)";
        var result = await _sut.TestAsync(Trigger("x", action: script, actionType: TriggerActionType.Script), "x");

        result.Matched.Should().BeTrue();
        result.Error.Should().NotBeNull().And.Contain("3");
        result.ScriptEffects.Should().Equal(string.Format(Strings.TrigTest_FxSend, "antes"));
        result.Describe().Should().Contain(result.Error!);
    }

    [Fact]
    public async Task Lua_SyntaxError_IsShown()
    {
        var result = await _sut.TestAsync(Trigger("x", action: "om.send(", actionType: TriggerActionType.Script), "x");
        result.Error.Should().NotBeNullOrEmpty();
        result.ScriptEffects.Should().BeEmpty();
    }

    [Fact]
    public async Task Lua_InfiniteLoop_Ends_WithAnError()
    {
        var watch = Stopwatch.StartNew();
        var result = await _sut.TestAsync(Trigger("x", action: "while true do end", actionType: TriggerActionType.Script), "x");

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20));
        result.Matched.Should().BeTrue();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Lua_InfiniteLoopThatSends_EndsToo_AndKeepsWhatItCollected()
    {
        var result = await _sut.TestAsync(Trigger("x", action: "while true do om.send('otra vez') end", actionType: TriggerActionType.Script), "x");
        result.Error.Should().NotBeNullOrEmpty();
        result.ScriptEffects.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Lua_WithoutEngine_SaysScriptsCannotRun()
    {
        var result = await new TriggerTester(null).TestAsync(Trigger("x", action: "om.send('a')", actionType: TriggerActionType.Script), "x");
        result.Matched.Should().BeTrue();
        result.Error.Should().Be(Strings.TrigTest_ErrNoEngine);
    }

    [Fact]
    public async Task Lua_Get_DoesNotWaitForAMudThatIsNotThere()
    {
        var watch = Stopwatch.StartNew();
        var result = await _sut.TestAsync(Trigger("x", action: "local r = om.get('vida')\nom.send(tostring(r))", actionType: TriggerActionType.Script), "x");
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        result.ScriptEffects.Should().Equal(string.Format(Strings.TrigTest_FxGet, "vida"), string.Format(Strings.TrigTest_FxSend, "nil"));
    }
}
