namespace Omnimud.Core.Scripting;

/// <summary>
/// Execution limits for sandbox scripts.
/// </summary>
public sealed record ScriptLimits
{
    /// <summary>
    /// Time the script may spend actually running Lua code. Time spent waiting inside
    /// om.sleep / om.get / om.countdown does not count.
    /// </summary>
    public TimeSpan MaxExecutionTime { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Lua VM instructions per execution (0 or less = unlimited). Timer callbacks get a fresh budget each.</summary>
    public int MaxInstructions { get; init; } = 100_000;

    /// <summary>Wall-clock ceiling for one execution, waits included.</summary>
    public TimeSpan MaxTotalTime { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Longest string string.rep / table.concat / string.format / string.gsub may build, in characters.</summary>
    public int MaxStringLength { get; init; } = 1_000_000;

    /// <summary>
    /// Approximate memory ceiling: bytes the script may ALLOCATE (not retain) while running.
    /// MoonSharp has no native memory limit, so this is measured on the executing thread
    /// after every VM instruction. 0 or less = unlimited.
    /// </summary>
    public long MaxAllocatedBytes { get; init; } = 100L * 1024 * 1024;

    /// <summary>
    /// Last line of defence: if a single native call (one that runs no Lua instructions) keeps the
    /// thread busy this long after <see cref="MaxExecutionTime"/> has elapsed, the executing thread
    /// is aborted. Zero or negative disables it.
    /// </summary>
    public TimeSpan HardAbortGrace { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Default limits: 5s of script time + 100k instructions + 10 min in total.</summary>
    public static ScriptLimits Default => new();

    /// <summary>Relaxed limits for testing only.</summary>
    public static ScriptLimits Relaxed => new()
    {
        MaxInstructions = 1_000_000,
        MaxExecutionTime = TimeSpan.FromSeconds(30),
        MaxAllocatedBytes = 1024L * 1024 * 1024
    };
}
