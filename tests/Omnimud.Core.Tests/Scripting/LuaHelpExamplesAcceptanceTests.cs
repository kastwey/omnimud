using FluentAssertions;
using Omnimud.Core.Scripting;

namespace Omnimud.Core.Tests.Scripting;

/// <summary>
/// The examples of the original client's help (chm 3.10.x, see docs/auditoria/A2 §1.10), which were
/// C# compiled on the fly, rewritten in Lua. The same scripts are documented in docs/API_LUA.md.
/// </summary>
public sealed class LuaHelpExamplesAcceptanceTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly SignalingTimeProvider _time = new();
    private readonly LuaScriptEngine _sut;
    private readonly RecordingHost _host = new();

    public LuaHelpExamplesAcceptanceTests() => _sut = new LuaScriptEngine(_time);

    public void Dispose() => _sut.Dispose();

    private async Task RunOkAsync(string script, ScriptContext context)
    {
        var result = await _sut.ExecuteAsync(script, context, _host).WaitAsync(Patience);
        result.Success.Should().BeTrue(result.Error);
        _host.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ReplyToATell_UsingCaptures()
    {
        // Original: Happen "%s te dice: '%s'" -> Action "decir Anda, %1 me ha dicho %2."
        const string script = "om.send('decir Anda, ' .. om.captures[1] .. ' me ha dicho ' .. om.captures[2] .. '.')";
        var context = new ScriptContext { MatchedLine = "Gandalf te dice: 'corre'", Captures = ["Gandalf", "corre"] };

        await RunOkAsync(script, context);

        _host.Sent.Should().Equal("decir Anda, Gandalf me ha dicho corre.");
    }

    [Fact]
    public async Task LootWhenSomeoneDies()
    {
        // Original: Happen "^%s ha muerto." -> OmSend("coger todo de " + args[0]);
        const string script = "om.send('coger todo de ' .. om.args[1])";
        var context = new ScriptContext { MatchedLine = "El orco ha muerto.", Captures = ["El orco"] };

        await RunOkAsync(script, context);

        _host.Sent.Should().Equal("coger todo de El orco");
    }

    [Fact]
    public async Task AsleepState_KeptInSessionVariablesAcrossThreeTriggers()
    {
        const string fallAsleep = "om.setvar('dormido', 'si')";   // ^Te duermes.$
        const string wakeUp = "om.removevar('dormido')";          // ^Te despiertas.$
        const string someoneArrives = """
            if om.isset('dormido') and om.getvar('dormido') == 'si' then
                om.send('despertar')
                om.send('¡Hola ' .. om.args[1] .. '!')
            end
            """;                                                   // ^%w llega
        var arrival = new ScriptContext { MatchedLine = "Bilbo llega", Captures = ["Bilbo"] };

        await RunOkAsync(someoneArrives, arrival);
        _host.Sent.Should().BeEmpty("nobody is asleep yet");

        await RunOkAsync(fallAsleep, new ScriptContext { MatchedLine = "Te duermes." });
        await RunOkAsync(someoneArrives, arrival);
        _host.Sent.Should().Equal("despertar", "¡Hola Bilbo!");

        await RunOkAsync(wakeUp, new ScriptContext { MatchedLine = "Te despiertas." });
        await RunOkAsync(someoneArrives, arrival);
        _host.Sent.Should().HaveCount(2, "awake again, so the third arrival does nothing");
    }

    [Fact]
    public async Task CopySongsToTheMessagesBox()
    {
        // Original: Happen "^%w canta: '%s'$" -> AddMessage(args[0] + " canta: '" + args[1] + "'");
        const string script = """om.message(om.args[1] .. " canta: '" .. om.args[2] .. "'")""";
        var context = new ScriptContext { MatchedLine = "Elrond canta: 'A Elbereth'", Captures = ["Elrond", "A Elbereth"] };

        await RunOkAsync(script, context);

        _host.Messages.Should().Equal("Elrond canta: 'A Elbereth'");
    }

    [Fact]
    public async Task AvisaCuraCommand_PollsHitPointsUntilFullThenPlaysASound()
    {
        // Original: Happen "@AvisaCura" -> loop with Thread.Sleep(2000), OmGet("pv"), parse "actual/max", PlaySound("curado.wav")
        const string script = """
            while true do
                om.sleep(2)
                local respuesta = om.get('pv')
                if respuesta then
                    local actual, maximo = string.match(respuesta, '(%d+)/(%d+)')
                    if actual and tonumber(actual) >= tonumber(maximo) then
                        om.playsound('curado.wav')
                        break
                    end
                end
            end
            """;
        var context = new ScriptContext { ScriptName = "AvisaCura", FullCommand = "@AvisaCura" };
        _host.GetReplies.Enqueue("Pv: 50/100");
        _host.GetReplies.Enqueue(null); // a timed-out om.get must not break the loop
        _host.GetReplies.Enqueue("Pv: 100/100");

        var run = _sut.ExecuteAsync(script, context, _host);
        for (int i = 0; i < 3; i++)
            await _time.WaitAndAdvanceAsync(TimeSpan.FromSeconds(2));
        var result = await run.WaitAsync(Patience);

        result.Success.Should().BeTrue(result.Error);
        _host.GetCommands.Should().Equal("pv", "pv", "pv");
        _host.Sounds.Should().Equal("curado.wav:1");
    }

    [Fact]
    public async Task Counters()
    {
        // Original: StartCount(150, "m", "durante.mp3", "fin.mp3"); StartCount(3, "2s", "durante.mp3", "fin.mp3"); StartCount(8, "fin.mp3");
        const string script = """
            om.countdown(150, { unit = 'm', during = 'durante.mp3', finish = 'fin.mp3' })
            om.countdown(3, { unit = '2s', during = 'durante.mp3', finish = 'fin.mp3' })
            om.countdown(8, { finish = 'fin.mp3' })
            """;

        var run = _sut.ExecuteAsync(script, new ScriptContext(), _host);
        await _time.WaitAndAdvanceAsync(TimeSpan.FromMilliseconds(150));
        await _time.WaitAndAdvanceAsync(TimeSpan.FromSeconds(6));
        await _time.WaitAndAdvanceAsync(TimeSpan.FromSeconds(8));
        var result = await run.WaitAsync(Patience);

        result.Success.Should().BeTrue(result.Error);
        _host.Sounds.Should().Equal("durante.mp3:-1", "fin.mp3:1", "durante.mp3:-1", "fin.mp3:1", "fin.mp3:1");
        _host.StoppedSounds.Should().Equal("durante.mp3", "durante.mp3");
    }
}
