using MoonSharp.Interpreter;

namespace Omnimud.Core.Scripting;

/// <summary>
/// Builds the restricted Lua environment. Nothing here depends on the text of the user's script.
/// </summary>
internal static class LuaSandbox
{
    /// <summary>Name given to the user's chunk; error positions are reported as "script:(line,col)".</summary>
    public const string ChunkName = "script";

    // HardSandbox = constants, iterators, string, table, basic, math, bit32.
    // Added: pcall/xpcall (they cannot catch limit aborts), metatables, and os.time/date/clock/difftime.
    // Left out on purpose: io, os (system), debug, load/loadstring/loadfile/dofile/require, dynamic,
    // json, and coroutine (a user coroutine would hijack the yields om.sleep/om.get rely on).
    private const CoreModules Modules =
        CoreModules.Preset_HardSandbox | CoreModules.ErrorHandling | CoreModules.Metatables | CoreModules.OS_Time;

    private static readonly string[] RemovedGlobals =
    [
        "io", "debug", "load", "loadstring", "loadfile", "dofile", "require", "package",
        "coroutine", "dynamic", "json", "collectgarbage", "_MOONSHARP"
    ];

    private static readonly string[] AllowedOsFunctions = ["time", "date", "clock", "difftime"];

    public static Script Create(ScriptGuard guard, ScriptLimits limits, Action<string> print)
    {
        var lua = new Script(Modules);
        // Exclusive access is guaranteed by ScriptRun's lock; slices legitimately hop between pool threads.
        lua.Options.CheckThreadAccess = false;
        lua.Options.DebugPrint = print;
        lua.Options.DebugInput = _ => string.Empty;

        foreach (var name in RemovedGlobals)
            lua.Globals.Remove(name);

        if (lua.Globals.Get("os").Type == DataType.Table)
        {
            var os = lua.Globals.Get("os").Table;
            foreach (var key in os.Keys.ToList())
            {
                if (key.Type != DataType.String || !AllowedOsFunctions.Contains(key.String))
                    os.Remove(key);
            }
        }

        // GC.Collect on demand is a cheap way to stall the whole client.
        lua.Globals["collectgarbage"] = DynValue.NewCallback((_, _) => DynValue.NewNumber(0));

        HardenStringLibrary(lua, limits);
        HardenTableLibrary(lua, limits);

        lua.AttachDebugger(guard);
        lua.DebuggerEnabled = true;
        return lua;
    }

    private static void HardenStringLibrary(Script lua, ScriptLimits limits)
    {
        var str = lua.Globals.Get("string").Table;
        str.Remove("dump");
        long max = limits.MaxStringLength > 0 ? limits.MaxStringLength : long.MaxValue;

        var rep = str.Get("rep").Callback.ClrCallback;
        str["rep"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count >= 2 && args[0].Type is DataType.String or DataType.Number && args[1].Type == DataType.Number)
            {
                double n = args[1].Number;
                long unit = args[0].CastToString().Length;
                if (args.Count >= 3 && args[2].Type == DataType.String) unit += args[2].String.Length;
                if (n > 0 && n * unit > max)
                    throw TooLong("string.rep", max);
            }
            return rep(ctx, args);
        }, "rep");

        var format = str.Get("format").Callback.ClrCallback;
        str["format"] = DynValue.NewCallback((ctx, args) =>
        {
            long total = 0;
            for (int i = 0; i < args.Count; i++)
                total += args[i].Type == DataType.String ? args[i].String.Length : 32;
            if (total > max)
                throw TooLong("string.format", max);
            return format(ctx, args);
        }, "format");

        var gsub = str.Get("gsub").Callback.ClrCallback;
        str["gsub"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count >= 3 && args[0].Type == DataType.String)
            {
                long subject = args[0].String.Length + 1L;
                long replacement = args[2].Type switch
                {
                    DataType.String => args[2].String.Length,
                    DataType.Table => LongestString(args[2].Table),
                    _ => 0 // a function: every call runs instructions, so the guard sees the growth
                };
                // Worst case is one replacement per character.
                if (subject > max || subject * Math.Max(1L, replacement) > max * 16)
                    throw TooLong("string.gsub", max);
            }
            return gsub(ctx, args);
        }, "gsub");
    }

    private static void HardenTableLibrary(Script lua, ScriptLimits limits)
    {
        var table = lua.Globals.Get("table").Table;
        long max = limits.MaxStringLength > 0 ? limits.MaxStringLength : long.MaxValue;

        var concat = table.Get("concat").Callback.ClrCallback;
        table["concat"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count >= 1 && args[0].Type == DataType.Table)
            {
                var list = args[0].Table;
                long separator = args.Count >= 2 && args[1].Type == DataType.String ? args[1].String.Length : 0;
                int first = args.Count >= 3 && args[2].Type == DataType.Number ? (int)args[2].Number : 1;
                int last = args.Count >= 4 && args[3].Type == DataType.Number ? (int)args[3].Number : list.Length;
                long total = 0;
                for (int i = first; i <= last; i++)
                {
                    var item = list.Get(i);
                    total += separator + (item.Type == DataType.String ? item.String.Length : 32);
                    if (total > max)
                        throw TooLong("table.concat", max);
                }
            }
            return concat(ctx, args);
        }, "concat");
    }

    private static long LongestString(Table table)
    {
        long longest = 0;
        foreach (var pair in table.Pairs)
        {
            if (pair.Value.Type == DataType.String && pair.Value.String.Length > longest)
                longest = pair.Value.String.Length;
        }
        return longest;
    }

    private static ScriptRuntimeException TooLong(string function, long max)
        => new($"{function}: resulting string too long (limit is {max} characters)");
}
