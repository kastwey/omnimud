using FluentAssertions;
using NSubstitute;
using Omnimud.Core.Scripting;

namespace Omnimud.Core.Tests.Scripting;

/// <summary>om.get, om.sleep, om.countdown, time limits and cancellation. No real waiting: the clock is fake.</summary>
public sealed class LuaScriptEngineWaitTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly SignalingTimeProvider _time = new();
    private readonly LuaScriptEngine _sut;
    private readonly IScriptHost _host = Substitute.For<IScriptHost>();
    private readonly ScriptContext _context = new() { ScriptName = "esperas" };

    public LuaScriptEngineWaitTests() => _sut = new LuaScriptEngine(_time);

    public void Dispose() => _sut.Dispose();

    // ---- om.get ----

    [Fact]
    public async Task Get_ReturnsTheReplyFromTheHost()
    {
        _host.GetAsync("pv", null, TimeSpan.FromSeconds(3), false, Arg.Any<CancellationToken>()).Returns("Pv: 50/100");

        var result = await _sut.ExecuteAsync("local r = om.get('pv') om.send('tengo ' .. r)", _context, _host);

        result.Success.Should().BeTrue(result.Error);
        _host.Received(1).Send("tengo Pv: 50/100");
    }

    [Fact]
    public async Task Get_ReturnsNilWhenTheHostTimesOut()
    {
        _host.GetAsync(default!, default, default, default, default).ReturnsForAnyArgs((string?)null);

        var result = await _sut.ExecuteAsync("local r = om.get('pv') if r == nil then om.send('sin respuesta') end", _context, _host);

        result.Success.Should().BeTrue(result.Error);
        _host.Received(1).Send("sin respuesta");
    }

    [Fact]
    public async Task Get_PassesPatternTimeoutAndColors()
    {
        _host.GetAsync(default!, default, default, default, default).ReturnsForAnyArgs("x");

        var result = await _sut.ExecuteAsync("""
            om.get('pv', { pattern = [[^Pv: \d+]], timeout = 7.5, colors = true })
            om.get('estado', '^Estado')
            om.get('lento', { timeout = 9999 })
            """, _context, _host);

        result.Success.Should().BeTrue(result.Error);
        Received.InOrder(() =>
        {
            _host.GetAsync("pv", @"^Pv: \d+", TimeSpan.FromSeconds(7.5), true, Arg.Any<CancellationToken>());
            _host.GetAsync("estado", "^Estado", TimeSpan.FromSeconds(3), false, Arg.Any<CancellationToken>());
            _host.GetAsync("lento", null, TimeSpan.FromSeconds(60), false, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Get_WaitsWithoutBlockingAndResumesWithTheReply()
    {
        var reply = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _host.GetAsync(default!, default, default, default, default).ReturnsForAnyArgs(_ => { asked.SetResult(); return reply.Task; });

        var run = _sut.ExecuteAsync("om.send('antes') local r = om.get('inventario') om.send(r)", _context, _host);
        await asked.Task.WaitAsync(Patience);

        run.IsCompleted.Should().BeFalse();
        _host.Received(1).Send("antes");
        reply.SetResult("una espada");
        var result = await run.WaitAsync(Patience);

        result.Success.Should().BeTrue(result.Error);
        _host.Received(1).Send("una espada");
    }

    [Fact]
    public async Task Get_HostFailure_FailsTheScriptCleanly()
    {
        _host.GetAsync(default!, default, default, default, default)
            .ReturnsForAnyArgs(Task.FromException<string?>(new InvalidOperationException("sin conexion")));

        var result = await _sut.ExecuteAsync("om.get('pv')", _context, _host);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("om.get: sin conexion");
    }

    [Fact]
    public async Task Get_WithoutHost_RecordsTheCommandAndReturnsNil()
    {
        var result = await _sut.ExecuteAsync("om.send(tostring(om.get('pv')))", _context);

        result.Success.Should().BeTrue();
        result.CommandsToSend.Should().Equal("pv", "nil");
    }

    // ---- om.sleep ----

    [Fact]
    public async Task Sleep_ResumesOnlyWhenTheClockAdvances()
    {
        var run = _sut.ExecuteAsync("om.send('antes') om.sleep(2) om.send('despues')", _context, _host);

        await _time.WaitForTimerAsync();
        _host.Received(1).Send("antes");
        _host.DidNotReceive().Send("despues");
        _time.Advance(TimeSpan.FromSeconds(1.9));
        run.IsCompleted.Should().BeFalse();
        _time.Advance(TimeSpan.FromSeconds(0.1));
        var result = await run.WaitAsync(Patience);

        result.Success.Should().BeTrue(result.Error);
        _host.Received(1).Send("despues");
    }

    [Fact]
    public async Task Sleep_IsCappedAtTenSecondsPerCall()
    {
        var run = _sut.ExecuteAsync("om.sleep(3600) om.send('ya')", _context, _host);

        await _time.WaitAndAdvanceAsync(TimeSpan.FromSeconds(10));
        var result = await run.WaitAsync(Patience);

        result.Success.Should().BeTrue(result.Error);
    }

    [Fact]
    public async Task Sleep_DoesNotCountAgainstMaxExecutionTime()
    {
        var limits = new ScriptLimits { MaxExecutionTime = TimeSpan.FromSeconds(1) };

        var run = _sut.ExecuteAsync("for i = 1, 3 do om.sleep(10) end om.send('fin')", _context, _host, limits);
        for (int i = 0; i < 3; i++)
            await _time.WaitAndAdvanceAsync(TimeSpan.FromSeconds(10));
        var result = await run.WaitAsync(Patience);

        result.Success.Should().BeTrue(result.Error);
        _host.Received(1).Send("fin");
    }

    [Fact]
    public async Task Sleep_InsideANativeCallback_IsARegularLuaError()
    {
        var result = await _sut.ExecuteAsync("table.sort({2, 1}, function(a, b) om.sleep(1) return a < b end)", _context, _host);

        result.Success.Should().BeFalse();
        result.ErrorKind.Should().Be(ScriptErrorKind.Runtime);
    }

    // ---- limits on time ----

    [Fact]
    public async Task MaxTotalTime_StopsAScriptThatKeepsSleeping()
    {
        var limits = new ScriptLimits { MaxTotalTime = TimeSpan.FromSeconds(25) };

        var run = _sut.ExecuteAsync("while true do om.sleep(10) om.send('tic') end", _context, _host, limits);
        await _time.WaitAndAdvanceAsync(TimeSpan.FromSeconds(10));
        await _time.WaitAndAdvanceAsync(TimeSpan.FromSeconds(10));
        await _time.WaitAndAdvanceAsync(TimeSpan.FromSeconds(5));
        var result = await run.WaitAsync(Patience);

        result.Success.Should().BeFalse();
        result.ErrorKind.Should().Be(ScriptErrorKind.Timeout);
        _host.Received(2).Send("tic");
        _host.Received(1).ReportScriptError("esperas", Arg.Any<string>());
    }

    [Fact]
    public async Task MaxExecutionTime_CountsTimeSpentRunning_IncludingHostCalls()
    {
        _host.When(h => h.Send("lento")).Do(_ => _time.Advance(TimeSpan.FromSeconds(6)));

        var result = await _sut.ExecuteAsync("om.send('lento') om.send('nunca')", _context, _host);

        result.Success.Should().BeFalse();
        result.ErrorKind.Should().Be(ScriptErrorKind.Timeout);
        _host.DidNotReceive().Send("nunca");
    }

    // ---- cancellation ----

    [Fact]
    public async Task CancellingTheToken_InTheMiddleOfASleep_EndsTheScriptWithoutReportingAnError()
    {
        using var cts = new CancellationTokenSource();

        var run = _sut.ExecuteAsync("om.send('antes') om.sleep(5) om.send('despues')", _context, _host, null, cts.Token);
        await _time.WaitForTimerAsync();
        cts.Cancel();
        var result = await run.WaitAsync(Patience);

        result.Success.Should().BeFalse();
        result.ErrorKind.Should().Be(ScriptErrorKind.Cancelled);
        _host.DidNotReceive().Send("despues");
        _host.DidNotReceiveWithAnyArgs().ReportScriptError(default!, default!);
    }

    [Fact]
    public async Task CancelledToken_BeforeStarting_NeverRunsTheScript()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await _sut.ExecuteAsync("om.send('x')", _context, _host, null, cts.Token);

        result.ErrorKind.Should().Be(ScriptErrorKind.Cancelled);
        _host.DidNotReceiveWithAnyArgs().Send(default!);
    }

    [Fact]
    public async Task CancellingTheToken_DuringAGet_IsPassedToTheHost()
    {
        using var cts = new CancellationTokenSource();
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _host.GetAsync(default!, default, default, default, default).ReturnsForAnyArgs(call =>
        {
            var token = call.Arg<CancellationToken>();
            var pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => pending.TrySetCanceled(token));
            asked.SetResult();
            return pending.Task;
        });

        var run = _sut.ExecuteAsync("om.get('pv') om.send('despues')", _context, _host, null, cts.Token);
        await asked.Task.WaitAsync(Patience);
        cts.Cancel();
        var result = await run.WaitAsync(Patience);

        result.ErrorKind.Should().Be(ScriptErrorKind.Cancelled);
        _host.DidNotReceive().Send("despues");
    }

    [Fact]
    public async Task Dispose_CancelsRunningScripts_AndLaterExecutionsFailCleanly()
    {
        var run = _sut.ExecuteAsync("om.sleep(5) om.send('despues')", _context, _host);
        await _time.WaitForTimerAsync();

        _sut.Dispose();
        var interrupted = await run.WaitAsync(Patience);
        var afterDispose = await _sut.ExecuteAsync("om.send('x')", _context, _host);
        var legacyAfterDispose = await _sut.ExecuteAsync("om.send('x')", _context);

        interrupted.ErrorKind.Should().Be(ScriptErrorKind.EngineDisposed);
        afterDispose.Success.Should().BeFalse();
        afterDispose.ErrorKind.Should().Be(ScriptErrorKind.EngineDisposed);
        legacyAfterDispose.ErrorKind.Should().Be(ScriptErrorKind.EngineDisposed);
        _host.DidNotReceiveWithAnyArgs().Send(default!);
        _host.DidNotReceiveWithAnyArgs().ReportScriptError(default!, default!);
    }

    // ---- om.countdown (port of StartCount) ----

    [Fact]
    public async Task Countdown_PlaysDuringInALoop_WaitsTicksTimesUnit_ThenStopsItAndPlaysFinish()
    {
        var run = _sut.ExecuteAsync("om.countdown(3, { unit = '2s', during = 'durante.mp3', finish = 'fin.mp3' }) om.send('listo')", _context, _host);

        await _time.WaitForTimerAsync();
        _host.Received(1).PlaySound("durante.mp3", -1, 100, 50);
        _time.Advance(TimeSpan.FromSeconds(5.9));
        run.IsCompleted.Should().BeFalse();
        _host.DidNotReceiveWithAnyArgs().StopSound(default!);
        _time.Advance(TimeSpan.FromSeconds(0.1));
        var result = await run.WaitAsync(Patience);

        result.Success.Should().BeTrue(result.Error);
        Received.InOrder(() =>
        {
            _host.PlaySound("durante.mp3", -1, 100, 50);
            _host.StopSound("durante.mp3");
            _host.PlaySound("fin.mp3", 1, 100, 50);
            _host.Send("listo");
        });
    }

    [Theory]
    [InlineData("{ unit = 's' }", 8000)]
    [InlineData("nil", 8000)]
    [InlineData("{ unit = 'd' }", 800)]
    [InlineData("{ unit = 'c' }", 80)]
    [InlineData("{ unit = 'm' }", 8)]
    [InlineData("{ unit = '5d' }", 4000)]
    public async Task Countdown_Units(string options, int expectedMilliseconds)
    {
        var run = _sut.ExecuteAsync($"om.countdown(8, {options})", _context, _host);

        await _time.WaitForTimerAsync();
        _time.Advance(TimeSpan.FromMilliseconds(expectedMilliseconds - 1));
        run.IsCompleted.Should().BeFalse();
        _time.Advance(TimeSpan.FromMilliseconds(1));
        var result = await run.WaitAsync(Patience);

        result.Success.Should().BeTrue(result.Error);
        _host.DidNotReceiveWithAnyArgs().PlaySound(default!, default, default, default);
    }

    [Fact]
    public async Task Countdown_Cancelled_StopsTheLoopingSoundAndSkipsFinish()
    {
        using var cts = new CancellationTokenSource();

        var run = _sut.ExecuteAsync("om.countdown(60, { during = 'durante.mp3', finish = 'fin.mp3' })", _context, _host, null, cts.Token);
        await _time.WaitForTimerAsync();
        cts.Cancel();
        var result = await run.WaitAsync(Patience);

        result.ErrorKind.Should().Be(ScriptErrorKind.Cancelled);
        _host.Received(1).StopSound("durante.mp3");
        _host.DidNotReceive().PlaySound("fin.mp3", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
    }
}
