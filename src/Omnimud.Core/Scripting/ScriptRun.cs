using System.Runtime;
using System.Text.RegularExpressions;
using MoonSharp.Interpreter;
using Omnimud.Core.Resources;

namespace Omnimud.Core.Scripting;

/// <summary>
/// One execution of a script: its own MoonSharp <see cref="Script"/>, guard and om.* table.
/// The main chunk and any timer callback it registered are "fibers" (MoonSharp coroutines) that
/// share the VM; they never run at the same time (<see cref="_vmLock"/>) and each has its own budget.
///
/// A fiber runs in synchronous slices. When Lua calls om.sleep / om.get / om.countdown the native
/// function parks a request in the fiber and yields; the driver awaits it WITHOUT holding a thread
/// and resumes the coroutine with the answer. There is no coroutine library inside the sandbox, so
/// every yield the driver sees is one of ours.
/// </summary>
internal sealed partial class ScriptRun
{
    private abstract record WaitRequest;
    private sealed record SleepRequest(TimeSpan Duration) : WaitRequest;
    private sealed record GetRequest(string Command, string? Pattern, TimeSpan Timeout, bool KeepColors) : WaitRequest;
    private sealed record CountdownRequest(TimeSpan Duration, string? During, string? Finish) : WaitRequest;

    private sealed class Fiber(ScriptBudget budget)
    {
        public ScriptBudget Budget { get; } = budget;
        public WaitRequest? Pending { get; set; }
    }

    private sealed class SliceWatch
    {
        public bool Finished;
    }

    private static readonly Regex PositionRegex = new(@"^[^:]*:\((\d+),", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly LuaScriptEngine _engine;
    private readonly ScriptContext _context;
    private readonly IScriptHost _host;
    private readonly ScriptLimits _limits;
    private readonly TimeProvider _time;
    private readonly ScriptGuard _guard;
    private readonly Script _lua;

    private readonly object _vmLock = new();
    private readonly object _hostGate = new();
    private Fiber? _current;      // only touched under _vmLock
    private bool _inHost;         // under _hostGate
    private bool _abortRequested; // under _hostGate
    private volatile bool _poisoned;

    public ScriptRun(LuaScriptEngine engine, ScriptContext context, IScriptHost host, ScriptLimits limits, TimeProvider time)
    {
        _engine = engine;
        _context = context;
        _host = host;
        _limits = limits;
        _time = time;
        _guard = new ScriptGuard(time);
        _lua = LuaSandbox.Create(_guard, limits, text => CallHost("print", () => _host.Echo(text)));
        RegisterApi();
    }

    private string ScriptName => string.IsNullOrEmpty(_context.ScriptName) ? "script" : _context.ScriptName;

    /// <summary>True once the VM may be corrupt (its thread had to be aborted); timers must not reuse it.</summary>
    public bool IsPoisoned => _poisoned;

    public Task<ScriptResult> RunMainAsync(string script, CancellationToken ct)
        => RunFiberAsync(() => _lua.CreateCoroutine(_lua.LoadString(script, null, LuaSandbox.ChunkName)).Coroutine, ScriptName, ct);

    public Task<ScriptResult> RunCallbackAsync(DynValue function, string timerName, CancellationToken ct)
        => RunFiberAsync(() => _lua.CreateCoroutine(function).Coroutine, $"{ScriptName} [timer {timerName}]", ct);

    private async Task<ScriptResult> RunFiberAsync(Func<Coroutine> start, string reportName, CancellationToken ct)
    {
        ScriptResult result;
        try
        {
            var fiber = new Fiber(new ScriptBudget(_limits, _time, ct));
            await DriveAsync(fiber, start).ConfigureAwait(false);
            return ScriptResult.Ok();
        }
        catch (Exception ex)
        {
            result = ToFailure(ex);
        }

        if (result.ErrorKind is not (ScriptErrorKind.Cancelled or ScriptErrorKind.EngineDisposed))
        {
            try { _host.ReportScriptError(reportName, result.Error ?? string.Empty); }
            catch (Exception) { /* a broken host must not turn a script error into a crash */ }
        }
        return result;
    }

    private async Task DriveAsync(Fiber fiber, Func<Coroutine> start)
    {
        Coroutine? coroutine = null;
        DynValue? answer = null;

        while (true)
        {
            var toSend = answer;
            // Always a fresh pool thread: a slice must never be inlined into whoever completed the
            // awaited task (it could be another script's thread, or the UI).
            await Task.Run(() => RunSlice(fiber, () =>
            {
                coroutine ??= start();
                if (toSend is null) coroutine.Resume();
                else coroutine.Resume(toSend);
            }), CancellationToken.None).ConfigureAwait(false);

            if (coroutine is null || coroutine.State == CoroutineState.Dead)
                return;

            var request = fiber.Pending ?? throw new ScriptRuntimeException("unexpected yield");
            fiber.Pending = null;
            answer = await ServeAsync(request, fiber.Budget).ConfigureAwait(false);
        }
    }

    private void RunSlice(Fiber fiber, Action body)
    {
        lock (_vmLock)
        {
            if (_poisoned) throw new ScriptAbortException(ScriptErrorKind.Timeout);
            _current = fiber;
            try
            {
                _guard.BeginSegment(fiber.Budget);
                ExecuteWithWatchdog(body, fiber.Budget);
            }
            finally
            {
                _guard.EndSegment();
                _current = null;
            }
        }
    }

    /// <summary>
    /// The guard only sees Lua instructions. A native call that runs none (a huge table.sort, a
    /// pattern match over a very long string) is invisible to it, so a real-time watchdog aborts
    /// the thread if the slice is still busy after the time limit plus a grace period. The abort is
    /// never delivered while the thread is inside a host call.
    /// </summary>
    private void ExecuteWithWatchdog(Action body, ScriptBudget budget)
    {
        var grace = _limits.HardAbortGrace;
        if (grace <= TimeSpan.Zero)
        {
            body();
            return;
        }

        var due = _limits.MaxExecutionTime - budget.CpuTime;
        if (due < TimeSpan.Zero) due = TimeSpan.Zero;
        due += grace;
        if (due > TimeSpan.FromDays(1)) due = TimeSpan.FromDays(1);

        var watch = new SliceWatch();
        using var cts = new CancellationTokenSource();
        Timer? timer = null;
        timer = new Timer(_ =>
        {
            lock (_hostGate)
            {
                if (watch.Finished) return;
                if (_inHost)
                {
                    try { timer?.Change(TimeSpan.FromMilliseconds(50), Timeout.InfiniteTimeSpan); }
                    catch (ObjectDisposedException) { }
                    return;
                }
                _abortRequested = true;
            }
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { }
        }, null, due, Timeout.InfiniteTimeSpan);

        try
        {
#pragma warning disable SYSLIB0046 // ControlledExecution is exactly for this: stopping runaway code we do not control.
            ControlledExecution.Run(body, cts.Token);
#pragma warning restore SYSLIB0046
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            _poisoned = true;
            throw new ScriptAbortException(ScriptErrorKind.Timeout);
        }
        finally
        {
            lock (_hostGate) watch.Finished = true;
            timer.Dispose();
        }

        bool lateAbort;
        lock (_hostGate) lateAbort = _abortRequested;
        if (lateAbort)
        {
            _poisoned = true;
            throw new ScriptAbortException(ScriptErrorKind.Timeout);
        }
    }

    /// <summary>Every call into the host goes through here (see <see cref="ExecuteWithWatchdog"/>).</summary>
    private T CallHost<T>(string function, Func<T> call)
    {
        lock (_hostGate)
        {
            if (_abortRequested) throw new ScriptAbortException(ScriptErrorKind.Timeout);
            _inHost = true;
        }
        T result;
        try
        {
            result = call();
        }
        catch (Exception ex) when (ex is not (ScriptAbortException or InterpreterException or OperationCanceledException))
        {
            throw new ScriptRuntimeException($"{function}: {ex.Message}");
        }
        finally
        {
            lock (_hostGate) _inHost = false;
        }
        _guard.CheckNow(); // time spent in the host counts too
        return result;
    }

    private void CallHost(string function, Action call)
        => CallHost<object?>(function, () => { call(); return null; });

    private async Task<DynValue> ServeAsync(WaitRequest request, ScriptBudget budget)
    {
        switch (request)
        {
            case SleepRequest sleep:
                await DelayAsync(sleep.Duration, budget).ConfigureAwait(false);
                return DynValue.Nil;

            case GetRequest get:
            {
                var remaining = budget.TotalRemaining(_time);
                var timeout = get.Timeout < remaining ? get.Timeout : remaining;
                string? reply;
                try
                {
                    reply = await _host.GetAsync(get.Command, get.Pattern, timeout, get.KeepColors, budget.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw new ScriptRuntimeException($"om.get: {ex.Message}");
                }
                budget.Token.ThrowIfCancellationRequested();
                return reply is null ? DynValue.Nil : DynValue.NewString(reply);
            }

            case CountdownRequest countdown:
            {
                bool started = false;
                try
                {
                    if (countdown.During is not null)
                    {
                        _host.PlaySound(countdown.During, -1, 100, 50);
                        started = true;
                    }
                    await DelayAsync(countdown.Duration, budget).ConfigureAwait(false);
                }
                finally
                {
                    if (started) _host.StopSound(countdown.During!);
                }
                if (countdown.Finish is not null)
                    _host.PlaySound(countdown.Finish, 1, 100, 50);
                return DynValue.Nil;
            }

            default:
                throw new InvalidOperationException("Unknown wait request.");
        }
    }

    private async Task DelayAsync(TimeSpan duration, ScriptBudget budget)
    {
        var remaining = budget.TotalRemaining(_time);
        bool cut = duration > remaining;
        var delay = cut ? remaining : duration;
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, _time, budget.Token).ConfigureAwait(false);
        else
            budget.Token.ThrowIfCancellationRequested();
        if (cut)
            throw new ScriptAbortException(ScriptErrorKind.Timeout);
    }

    private ScriptResult ToFailure(Exception ex)
    {
        switch (ex)
        {
            case ScriptAbortException abort:
                return abort.Kind switch
                {
                    ScriptErrorKind.InstructionLimit => ScriptResult.Fail(abort.Kind, Strings.Error_ScriptInstructionLimit),
                    ScriptErrorKind.Timeout => ScriptResult.Fail(abort.Kind, Strings.Error_ScriptTimedOut),
                    ScriptErrorKind.MemoryLimit => ScriptResult.Fail(abort.Kind, "Script exceeded the memory limit or nested calls too deeply."),
                    _ => Cancelled()
                };

            case OperationCanceledException:
                return Cancelled();

            case SyntaxErrorException syntax:
            {
                var (line, message) = Describe(syntax);
                return ScriptResult.Fail(ScriptErrorKind.Syntax, string.Format(Strings.Error_ScriptSyntax, message), line);
            }

            case InterpreterException interpreter:
            {
                var (line, message) = Describe(interpreter);
                return ScriptResult.Fail(ScriptErrorKind.Runtime, string.Format(Strings.Error_ScriptRuntime, message), line);
            }

            case OutOfMemoryException or InsufficientExecutionStackException:
                return ScriptResult.Fail(ScriptErrorKind.MemoryLimit, "Script exceeded the memory limit or nested calls too deeply.");

            default:
                // MoonSharp surfaces some failures (VM stack exhaustion, bad casts) as plain CLR exceptions.
                return ScriptResult.Fail(ScriptErrorKind.Runtime, string.Format(Strings.Error_ScriptRuntime, ex.Message));
        }

        ScriptResult Cancelled() => _engine.IsDisposed
            ? ScriptResult.Fail(ScriptErrorKind.EngineDisposed, LuaScriptEngine.DisposedMessage)
            : ScriptResult.Fail(ScriptErrorKind.Cancelled, "Script was cancelled.");
    }

    /// <summary>Turns MoonSharp's "chunk:(line,col-col): message" into (line, "line N: message").</summary>
    internal static (int? Line, string Message) Describe(InterpreterException ex)
    {
        var decorated = ex.DecoratedMessage;
        if (!string.IsNullOrEmpty(decorated))
        {
            var match = PositionRegex.Match(decorated);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int line))
                return (line, $"line {line}: {ex.Message}");
        }
        return (null, ex.Message);
    }
}
