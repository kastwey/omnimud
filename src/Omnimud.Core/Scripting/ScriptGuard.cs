using System.Runtime.CompilerServices;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Debugging;

namespace Omnimud.Core.Scripting;

/// <summary>
/// Thrown from inside the VM to stop a script that broke a limit. Deliberately NOT a MoonSharp
/// InterpreterException, so Lua's pcall/xpcall cannot swallow it.
/// </summary>
internal sealed class ScriptAbortException(ScriptErrorKind kind) : Exception(kind.ToString())
{
    public ScriptErrorKind Kind { get; } = kind;
}

/// <summary>Budget of one execution (the main chunk, or one timer callback).</summary>
internal sealed class ScriptBudget(ScriptLimits limits, TimeProvider time, CancellationToken ct)
{
    public ScriptLimits Limits { get; } = limits;
    public CancellationToken Token { get; } = ct;
    public long StartTimestamp { get; } = time.GetTimestamp();
    public long Instructions;
    public long AllocatedBytes;
    public TimeSpan CpuTime;

    public TimeSpan TotalElapsed(TimeProvider provider) => provider.GetElapsedTime(StartTimestamp);

    public TimeSpan TotalRemaining(TimeProvider provider)
    {
        var left = Limits.MaxTotalTime - TotalElapsed(provider);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }
}

/// <summary>
/// Enforces the limits from inside the MoonSharp VM. It is attached as a debugger because that is
/// the only hook MoonSharp calls before EVERY instruction, including the ones executed by Lua
/// functions that native code calls back (table.sort comparators, string.gsub replacers,
/// __tostring...). Coroutine.AutoYieldCounter was tried first and does not yield in those nested
/// calls, so a loop inside a comparator would hang the thread.
/// </summary>
internal sealed class ScriptGuard(TimeProvider time) : IDebugger
{
    private const int TimeCheckMask = 0x3F; // look at the clock every 64 instructions

    private ScriptBudget? _budget;
    private long _segmentStart;
    private long _segmentAllocBase;

    /// <summary>Starts a synchronous slice of execution on the current thread.</summary>
    public void BeginSegment(ScriptBudget budget)
    {
        _budget = budget;
        _segmentStart = time.GetTimestamp();
        _segmentAllocBase = GC.GetAllocatedBytesForCurrentThread();
        Check(budget, checkClock: true);
    }

    public void EndSegment()
    {
        var budget = _budget;
        if (budget is null) return;
        budget.CpuTime += time.GetElapsedTime(_segmentStart);
        budget.AllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - _segmentAllocBase;
        _budget = null;
    }

    /// <summary>Called by native om.* functions so that they are subject to the same limits.</summary>
    public void CheckNow()
    {
        if (_budget is { } budget) Check(budget, checkClock: true);
    }

    public bool IsPauseRequested()
    {
        var budget = _budget;
        if (budget is null) return false;

        long count = ++budget.Instructions;
        if (budget.Limits.MaxInstructions > 0 && count > budget.Limits.MaxInstructions)
            throw new ScriptAbortException(ScriptErrorKind.InstructionLimit);

        Check(budget, (count & TimeCheckMask) == 0);
        return false;
    }

    private void Check(ScriptBudget budget, bool checkClock)
    {
        var limits = budget.Limits;

        if (limits.MaxAllocatedBytes > 0 &&
            budget.AllocatedBytes + (GC.GetAllocatedBytesForCurrentThread() - _segmentAllocBase) > limits.MaxAllocatedBytes)
            throw new ScriptAbortException(ScriptErrorKind.MemoryLimit);

        // Native code calling back into Lua (gsub/sort/tostring) recurses on the CLR stack.
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
            throw new ScriptAbortException(ScriptErrorKind.MemoryLimit);

        if (!checkClock) return;

        if (budget.Token.IsCancellationRequested)
            throw new ScriptAbortException(ScriptErrorKind.Cancelled);
        if (budget.CpuTime + time.GetElapsedTime(_segmentStart) > limits.MaxExecutionTime)
            throw new ScriptAbortException(ScriptErrorKind.Timeout);
        if (budget.TotalElapsed(time) > limits.MaxTotalTime)
            throw new ScriptAbortException(ScriptErrorKind.Timeout);
    }

    public DebuggerCaps GetDebuggerCaps() => DebuggerCaps.CanDebugSourceCode;
    public DebuggerAction GetAction(int ip, SourceRef sourceref) => new() { Action = DebuggerAction.ActionType.Run };
    public bool SignalRuntimeException(ScriptRuntimeException ex) => false;
    public void SetDebugService(DebugService debugService) { }
    public void SetSourceCode(SourceCode sourceCode) { }
    public void SetByteCode(string[] byteCode) { }
    public void SignalExecutionEnded() { }
    public void Update(WatchType watchType, IEnumerable<WatchItem> items) { }
    public List<DynamicExpression> GetWatchItems() => [];
    public void RefreshBreakpoints(IEnumerable<SourceRef> refs) { }
}
