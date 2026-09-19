using FluentAssertions;
using NSubstitute;
using Omnimud.Core.Scripting;

namespace Omnimud.Core.Tests.Scripting;

public sealed class LuaScriptEngineTimerTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly SignalingTimeProvider _time = new();
    private readonly LuaScriptEngine _sut;
    private readonly IScriptHost _host = Substitute.For<IScriptHost>();
    private readonly ScriptContext _context = new() { ScriptName = "relojes" };

    public LuaScriptEngineTimerTests() => _sut = new LuaScriptEngine(_time);

    public void Dispose() => _sut.Dispose();

    private Task WhenSent(string command)
    {
        var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _host.When(h => h.Send(command)).Do(_ => seen.TrySetResult());
        return seen.Task.WaitAsync(Patience);
    }

    private async Task RunOkAsync(string script)
    {
        var result = await _sut.ExecuteAsync(script, _context, _host);
        result.Success.Should().BeTrue(result.Error);
    }

    [Fact]
    public async Task Timer_FiresOnceAfterTheDelay_WithAccessToUpvaluesAndTheApi()
    {
        var fired = WhenSent("beber pocion de vida");

        await RunOkAsync("local que = 'pocion de vida' om.timer('pocion', 5, function() om.send('beber ' .. que) end)");

        _sut.ActiveTimerCount.Should().Be(1);
        _time.Advance(TimeSpan.FromSeconds(4.9));
        _host.DidNotReceiveWithAnyArgs().Send(default!);
        _time.Advance(TimeSpan.FromSeconds(0.1));
        await fired;
        _sut.ActiveTimerCount.Should().Be(0);

        _time.Advance(TimeSpan.FromMinutes(1));
        _host.Received(1).Send("beber pocion de vida");
    }

    [Fact]
    public async Task CancelTimer_FromAnotherScript_PreventsItFromFiring()
    {
        await RunOkAsync("om.timer('pocion', 5, function() om.send('disparado') end)");

        await RunOkAsync("om.send(tostring(om.canceltimer('pocion')) .. ' ' .. tostring(om.canceltimer('pocion')))");

        _sut.ActiveTimerCount.Should().Be(0);
        _time.Advance(TimeSpan.FromSeconds(10));
        _host.Received(1).Send("true false");
        _host.DidNotReceive().Send("disparado");
    }

    [Fact]
    public async Task Timer_WithTheSameName_ReplacesThePreviousOne()
    {
        var fired = WhenSent("nuevo");

        await RunOkAsync("om.timer('t', 5, function() om.send('viejo') end)");
        await RunOkAsync("om.timer('t', 8, function() om.send('nuevo') end)");

        _sut.ActiveTimerCount.Should().Be(1);
        _time.Advance(TimeSpan.FromSeconds(8));
        await fired;
        _host.DidNotReceive().Send("viejo");
    }

    [Fact]
    public async Task Timers_AreCappedAtTenPerEngine()
    {
        await RunOkAsync("for i = 1, 10 do om.timer('t' .. i, 60, function() end) end");

        var eleventh = await _sut.ExecuteAsync("om.timer('uno mas', 60, function() end)", _context, _host);
        var replacing = await _sut.ExecuteAsync("om.timer('t3', 60, function() end)", _context, _host);

        eleventh.Success.Should().BeFalse();
        eleventh.Error.Should().Contain("om.timer: too many timers");
        replacing.Success.Should().BeTrue("replacing an existing timer does not need a free slot");
        _sut.ActiveTimerCount.Should().Be(LuaScriptEngine.MaxTimers);
    }

    [Fact]
    public async Task Dispose_CancelsEveryTimer()
    {
        await RunOkAsync("om.timer('a', 5, function() om.send('a') end) om.timer('b', 5, function() om.send('b') end)");

        _sut.Dispose();

        _sut.ActiveTimerCount.Should().Be(0);
        _time.Advance(TimeSpan.FromSeconds(10));
        _host.DidNotReceiveWithAnyArgs().Send(default!);
    }

    [Fact]
    public async Task RepeatingTimer_KeepsFiringUntilCancelled()
    {
        var first = WhenSent("tic 1");
        var second = WhenSent("tic 2");

        await RunOkAsync("local n = 0 om.timer('tic', 2, function() n = n + 1 om.send('tic ' .. n) end, true)");
        await _time.WaitAndAdvanceAsync(TimeSpan.FromSeconds(2));
        await first;
        await _time.WaitAndAdvanceAsync(TimeSpan.FromSeconds(2));
        await second;
        await _time.WaitForTimerAsync();

        _sut.ActiveTimerCount.Should().Be(1);
        await RunOkAsync("om.canceltimer('tic')");
        _time.Advance(TimeSpan.FromSeconds(10));
        _host.DidNotReceive().Send("tic 3");
    }

    [Fact]
    public async Task RepeatingTimer_CanCancelItselfFromItsCallback_AndTheCallbackStillFinishes()
    {
        var finished = WhenSent("fin de la vuelta 2");

        await RunOkAsync("""
            local n = 0
            om.timer('vigia', 2, function()
                n = n + 1
                if n == 2 then om.canceltimer('vigia') end
                om.send('fin de la vuelta ' .. n)
            end, true)
            """);
        await _time.WaitAndAdvanceAsync(TimeSpan.FromSeconds(2));
        await _time.WaitAndAdvanceAsync(TimeSpan.FromSeconds(2));
        await finished;

        _sut.ActiveTimerCount.Should().Be(0);
        _time.Advance(TimeSpan.FromSeconds(10));
        _host.DidNotReceive().Send("fin de la vuelta 3");
    }

    [Fact]
    public async Task OneShotTimer_CanRearmItselfFromItsCallback()
    {
        var second = WhenSent("vuelta 2");

        await RunOkAsync("""
            local n = 0
            local function vuelta()
                n = n + 1
                om.send('vuelta ' .. n)
                if n < 2 then om.timer('bucle', 1, vuelta) end
            end
            om.timer('bucle', 1, vuelta)
            """);
        await _time.WaitAndAdvanceAsync(TimeSpan.FromSeconds(1));
        await _time.WaitAndAdvanceAsync(TimeSpan.FromSeconds(1));
        await second;

        _host.Received(1).Send("vuelta 1");
    }

    [Fact]
    public async Task TimerCallback_RunsUnderTheSameLimits_AndReportsItsErrors()
    {
        var reported = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _host.When(h => h.ReportScriptError(Arg.Any<string>(), Arg.Any<string>()))
            .Do(call => reported.TrySetResult(call.ArgAt<string>(0) + " | " + call.ArgAt<string>(1)));
        var limits = new ScriptLimits { MaxInstructions = 5_000 };

        var result = await _sut.ExecuteAsync("om.timer('malo', 1, function() while true do end end, true)", _context, _host, limits);
        _time.Advance(TimeSpan.FromSeconds(1));
        var report = await reported.Task.WaitAsync(Patience);

        result.Success.Should().BeTrue(result.Error);
        report.Should().StartWith("relojes [timer malo] | ");
        report.Should().Contain("instruc");
    }

    [Fact]
    public async Task TimerCallback_RuntimeError_IsReportedWithItsLine()
    {
        var reported = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _host.When(h => h.ReportScriptError(Arg.Any<string>(), Arg.Any<string>()))
            .Do(call => reported.TrySetResult(call.ArgAt<string>(1)));

        await RunOkAsync("om.timer('malo', 1, function()\n  local t = nil\n  om.send(t.x)\nend)");
        _time.Advance(TimeSpan.FromSeconds(1));
        var report = await reported.Task.WaitAsync(Patience);

        report.Should().Contain("line 3");
    }

    [Fact]
    public async Task TimerCallback_CanSleepAndGet()
    {
        _host.GetAsync(default!, default, default, default, default).ReturnsForAnyArgs("Pv: 10/10");
        var done = WhenSent("Pv: 10/10");

        await RunOkAsync("om.timer('t', 1, function() om.sleep(2) om.send(om.get('pv')) end)");
        await _time.WaitAndAdvanceAsync(TimeSpan.FromSeconds(1));
        await _time.WaitAndAdvanceAsync(TimeSpan.FromSeconds(2));

        await done;
    }

    [Fact]
    public async Task Timers_WorkWithoutAHostToo()
    {
        var result = await _sut.ExecuteAsync("om.timer('t', 5, function() om.send('x') end) om.send('armado')", _context);

        result.Success.Should().BeTrue();
        result.CommandsToSend.Should().Equal("armado");
        _sut.ActiveTimerCount.Should().Be(1);
    }
}
