using MoonSharp.Interpreter;
using Omnimud.Core.Resources;

namespace Omnimud.Core.Scripting;

/// <summary>
/// Lua scripting engine (MoonSharp) with a real sandbox. One instance per session.
///
/// <para><b>Isolation.</b> Every execution gets its own MoonSharp <c>Script</c>, so scripts of
/// different triggers can run concurrently without sharing Lua state; what they share are the
/// host's session variables. The environment has string/table/math/bit32, pcall, metatables and
/// os.time/date/clock; no io, os.execute, debug, load*, dofile, require, coroutine, nor any CLR type.</para>
///
/// <para><b>Limits.</b> Enforced from inside the VM before every instruction by <see cref="ScriptGuard"/>
/// (instructions, script time, total time, allocated bytes, CLR stack depth, cancellation), never by
/// rewriting the script text. om.sleep / om.get / om.countdown are cooperative: they hold no thread
/// while waiting, and that time counts against MaxTotalTime only.</para>
///
/// <para><b>Errors.</b> Neither overload throws because of a script: syntax and runtime errors,
/// broken limits, cancellation through the caller's token and disposal of the engine all come back
/// as a failed <see cref="ScriptResult"/> (see <see cref="ScriptResult.ErrorKind"/>). Cancellation
/// is deliberately not rethrown as OperationCanceledException because callers fire scripts and
/// forget them. With a host, real errors are also sent to <see cref="IScriptHost.ReportScriptError"/>;
/// cancellation and disposal are not, as they are not the script's fault.</para>
/// </summary>
public sealed class LuaScriptEngine : IScriptEngine
{
    /// <summary>Timers alive at once, per engine.</summary>
    public const int MaxTimers = 10;

    internal const string DisposedMessage = "The script engine has been disposed.";

    private sealed class TimerEntry(string name, CancellationTokenSource cts)
    {
        public string Name { get; } = name;
        public CancellationTokenSource Cts { get; } = cts;
    }

    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly Dictionary<string, TimerEntry> _timers = new(StringComparer.Ordinal);
    private volatile bool _disposed;

    public LuaScriptEngine() : this(TimeProvider.System)
    {
    }

    public LuaScriptEngine(TimeProvider timeProvider)
    {
        _time = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    internal bool IsDisposed => _disposed;

    /// <summary>Number of timers waiting to fire.</summary>
    public int ActiveTimerCount
    {
        get { lock (_timers) return _timers.Count; }
    }

    public async Task<ScriptResult> ExecuteAsync(string script, ScriptContext context, ScriptLimits? limits = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var collector = new CollectingScriptHost(context);
        var result = await ExecuteAsync(script, context, collector, limits, ct).ConfigureAwait(false);
        return result.Success ? collector.ToResult() : result;
    }

    public async Task<ScriptResult> ExecuteAsync(string script, ScriptContext context, IScriptHost host, ScriptLimits? limits = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(host);

        if (_disposed)
            return ScriptResult.Fail(ScriptErrorKind.EngineDisposed, DisposedMessage);
        if (string.IsNullOrWhiteSpace(script))
            return ScriptResult.Ok();

        CancellationTokenSource linked;
        try
        {
            linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
        }
        catch (ObjectDisposedException)
        {
            return ScriptResult.Fail(ScriptErrorKind.EngineDisposed, DisposedMessage);
        }

        using (linked)
        {
            try
            {
                // Building the VM is not free (first use loads MoonSharp); keep it off the caller's thread.
                var run = await Task.Run(() => new ScriptRun(this, context, host, limits ?? ScriptLimits.Default, _time), CancellationToken.None).ConfigureAwait(false);
                return await run.RunMainAsync(script, linked.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Nothing a script does may take the client down (the original closed the application).
                return ScriptResult.Fail(ScriptErrorKind.Runtime, string.Format(Strings.Error_ScriptRuntime, ex.Message));
            }
        }
    }

    public string? Validate(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return null;
        try
        {
            new Script(CoreModules.None).LoadString(script, null, LuaSandbox.ChunkName);
            return null;
        }
        catch (SyntaxErrorException ex)
        {
            return string.Format(Strings.Error_ScriptSyntax, ScriptRun.Describe(ex).Message);
        }
        catch (Exception ex)
        {
            return string.Format(Strings.Error_ScriptSyntax, ex.Message);
        }
    }

    /// <summary>
    /// Arms (or replaces) a named timer. The delay is created here, synchronously, so that once
    /// om.timer returns the timer is really pending.
    /// </summary>
    internal void SetTimer(ScriptRun run, string name, TimeSpan interval, DynValue function, bool repeating)
    {
        TimerEntry entry;
        Task firstDelay;
        lock (_timers)
        {
            if (_disposed)
                throw new InvalidOperationException(DisposedMessage);

            if (_timers.Remove(name, out var previous))
                previous.Cts.Cancel();
            else if (_timers.Count >= MaxTimers)
                throw new InvalidOperationException($"too many timers (the limit is {MaxTimers})");

            entry = new TimerEntry(name, CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token));
            firstDelay = Task.Delay(interval, _time, entry.Cts.Token);
            _timers[name] = entry;
        }

        _ = RunTimerAsync(entry, run, function, interval, repeating, firstDelay);
    }

    internal bool CancelTimer(string name)
    {
        lock (_timers)
        {
            if (!_timers.Remove(name, out var entry)) return false;
            entry.Cts.Cancel();
            return true;
        }
    }

    private async Task RunTimerAsync(TimerEntry entry, ScriptRun run, DynValue function, TimeSpan interval, bool repeating, Task delay)
    {
        try
        {
            while (true)
            {
                await delay.ConfigureAwait(false);
                if (run.IsPoisoned) break;

                // A one-shot timer frees its slot before the callback so that it can re-arm itself.
                if (!repeating) Forget(entry);

                // The callback answers to the engine only: om.canceltimer stops future firings,
                // it does not kill a callback that is already running.
                var result = await run.RunCallbackAsync(function, entry.Name, _disposeCts.Token).ConfigureAwait(false);
                if (!repeating || !result.Success) break;

                delay = Task.Delay(interval, _time, entry.Cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // Never let a timer fault an unobserved task.
        }
        finally
        {
            Forget(entry);
            entry.Cts.Dispose();
        }
    }

    private void Forget(TimerEntry entry)
    {
        lock (_timers)
        {
            if (_timers.TryGetValue(entry.Name, out var current) && ReferenceEquals(current, entry))
                _timers.Remove(entry.Name);
        }
    }

    /// <summary>Cancels every timer and every running script; later executions fail with EngineDisposed.</summary>
    public void Dispose()
    {
        lock (_timers)
        {
            if (_disposed) return;
            _disposed = true;
            _timers.Clear();
        }
        // Not disposed on purpose: running scripts still hold tokens linked to it.
        _disposeCts.Cancel();
    }
}
