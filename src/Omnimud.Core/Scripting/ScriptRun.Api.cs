using System.Text.RegularExpressions;
using MoonSharp.Interpreter;

namespace Omnimud.Core.Scripting;

/// <summary>The om.* table. User reference: docs/API_LUA.md.</summary>
internal sealed partial class ScriptRun
{
    internal static readonly TimeSpan MaxSleep = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MinTimerInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MaxTimerInterval = TimeSpan.FromDays(1);

    private const int MatchCacheCapacity = 256;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Regex> MatchCache = new(StringComparer.Ordinal);

    private static readonly Regex AnsiRegex = new(@"\x1b\[[0-9;?]*[ -/]*[@-~]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CountdownUnitRegex = new(@"^(\d{1,6})?([sdcm])$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly (string Name, int Code)[] AnsiConstants =
    [
        ("ANSI_RESET", 0), ("ANSI_BOLD", 1), ("ANSI_DIM", 2), ("ANSI_ITALIC", 3), ("ANSI_UNDERLINE", 4),
        ("ANSI_BLINK", 5), ("ANSI_INVERSE", 7), ("ANSI_HIDDEN", 8), ("ANSI_STRIKE", 9),
        ("ANSI_BOLD_OFF", 22), ("ANSI_DIM_OFF", 22), ("ANSI_ITALIC_OFF", 23), ("ANSI_UNDERLINE_OFF", 24),
        ("ANSI_BLINK_OFF", 25), ("ANSI_INVERSE_OFF", 27), ("ANSI_HIDDEN_OFF", 28), ("ANSI_STRIKE_OFF", 29),
        ("ANSI_BLACK", 30), ("ANSI_RED", 31), ("ANSI_GREEN", 32), ("ANSI_YELLOW", 33), ("ANSI_BLUE", 34),
        ("ANSI_MAGENTA", 35), ("ANSI_PURPLE", 35), ("ANSI_CYAN", 36), ("ANSI_WHITE", 37), ("ANSI_DEFAULT", 39),
        ("ANSI_BG_BLACK", 40), ("ANSI_BG_RED", 41), ("ANSI_BG_GREEN", 42), ("ANSI_BG_YELLOW", 43), ("ANSI_BG_BLUE", 44),
        ("ANSI_BG_MAGENTA", 45), ("ANSI_BG_PURPLE", 45), ("ANSI_BG_CYAN", 46), ("ANSI_BG_WHITE", 47), ("ANSI_BG_DEFAULT", 49)
    ];

    private void RegisterApi()
    {
        var om = new Table(_lua);
        _lua.Globals["om"] = om;

        // ---- sending ----
        Define(om, "send", args =>
        {
            var command = Text(args, 0, "om.send");
            if (command.Length > 0) CallHost("om.send", () => _host.Send(command));
            return DynValue.Nil;
        });

        Define(om, "sendraw", args =>
        {
            var command = Text(args, 0, "om.sendraw");
            if (command.Length > 0) CallHost("om.sendraw", () => _host.SendRaw(command));
            return DynValue.Nil;
        });

        Define(om, "get", args =>
        {
            EnsureWaitingAllowed("om.get");
            var command = Text(args, 0, "om.get");
            string? pattern = null;
            double timeout = 3;
            bool colors = false;

            var second = Arg(args, 1);
            if (second.Type == DataType.String)
            {
                pattern = second.String;
            }
            else if (Options(args, 1, "om.get") is { } options)
            {
                pattern = OptionalText(options, "pattern", "om.get");
                timeout = OptionalNumber(options, "timeout", "om.get") ?? 3;
                colors = options.Get("colors").CastToBool();
            }

            if (double.IsNaN(timeout) || timeout <= 0)
                throw new ScriptRuntimeException("om.get: timeout must be a positive number of seconds");
            if (string.IsNullOrEmpty(pattern)) pattern = null;
            if (pattern is not null) EnsureValidRegex(pattern, "om.get");

            return Yield(new GetRequest(command, pattern, TimeSpan.FromSeconds(Math.Clamp(timeout, 0.1, 60)), colors), "om.get");
        });

        // ---- output ----
        Define(om, "display", args =>
        {
            var text = Text(args, 0, "om.display");
            CallHost("om.display", () => _host.Display(text));
            return DynValue.Nil;
        });

        Define(om, "echo", args =>
        {
            var text = Text(args, 0, "om.echo");
            CallHost("om.echo", () => _host.Echo(text));
            return DynValue.Nil;
        });

        Define(om, "say", args =>
        {
            var text = Text(args, 0, "om.say");
            bool interrupt = Arg(args, 1).CastToBool();
            if (text.Length > 0) CallHost("om.say", () => _host.Say(text, interrupt));
            return DynValue.Nil;
        });

        Define(om, "notify", args =>
        {
            var text = Text(args, 0, "om.notify");
            if (text.Length > 0) CallHost("om.notify", () => _host.Say(text, false));
            return DynValue.Nil;
        });

        Define(om, "message", args =>
        {
            var text = Text(args, 0, "om.message");
            if (text.Length > 0) CallHost("om.message", () => _host.AddMessage(text));
            return DynValue.Nil;
        });

        Define(om, "status", args =>
        {
            var text = Text(args, 0, "om.status");
            CallHost("om.status", () => _host.SetStatus(text));
            return DynValue.Nil;
        });

        Define(om, "log", args =>
        {
            var text = Text(args, 0, "om.log");
            CallHost("om.log", () => _host.Log(text));
            return DynValue.Nil;
        });

        Define(om, "gag", _ =>
        {
            CallHost("om.gag", () => _host.Gag());
            return DynValue.Nil;
        });

        // ---- sound ----
        Define(om, "playsound", args =>
        {
            var name = NonEmptyText(args, 0, "om.playsound");
            int loop = 1, volume = 100, priority = 50;

            var second = Arg(args, 1);
            if (second.Type == DataType.Number)
            {
                loop = ToInt(second.Number, "om.playsound", "loop");
            }
            else if (Options(args, 1, "om.playsound") is { } options)
            {
                loop = OptionalInt(options, "loop", "om.playsound") ?? 1;
                volume = OptionalInt(options, "volume", "om.playsound") ?? 100;
                priority = OptionalInt(options, "priority", "om.playsound") ?? 50;
            }

            if (loop == 0) loop = 1;        // the original treated 0 and 1 alike
            if (loop < -1) loop = -1;
            volume = Math.Clamp(volume, 0, 100);
            priority = Math.Clamp(priority, 0, 100);
            CallHost("om.playsound", () => _host.PlaySound(name, loop, volume, priority));
            return DynValue.Nil;
        });

        Define(om, "stopsound", args =>
        {
            var name = NonEmptyText(args, 0, "om.stopsound");
            return DynValue.NewBoolean(CallHost("om.stopsound", () => _host.StopSound(name)));
        });

        // ---- session variables ----
        Define(om, "setvar", args =>
        {
            var name = Text(args, 0, "om.setvar");
            var raw = Arg(args, 1);
            string value = raw.Type switch
            {
                DataType.Nil or DataType.Void => string.Empty,
                DataType.Boolean => raw.Boolean ? "true" : "false",
                DataType.String or DataType.Number => raw.CastToString(),
                _ => throw new ScriptRuntimeException($"om.setvar: argument #2 must be a string, number or boolean (got {raw.Type.ToLuaTypeString()})")
            };
            if (name.Length > 0) CallHost("om.setvar", () => _host.SetVariable(name, value));
            return DynValue.Nil;
        });

        Define(om, "getvar", args =>
        {
            var name = Text(args, 0, "om.getvar");
            if (name.Length == 0) return DynValue.Nil;
            var value = CallHost("om.getvar", () => _host.GetVariable(name));
            return value is null ? DynValue.Nil : DynValue.NewString(value);
        });

        Define(om, "removevar", args =>
        {
            var name = Text(args, 0, "om.removevar");
            return DynValue.NewBoolean(name.Length > 0 && CallHost("om.removevar", () => _host.RemoveVariable(name)));
        });

        Define(om, "isset", args =>
        {
            var name = Text(args, 0, "om.isset");
            return DynValue.NewBoolean(name.Length > 0 && CallHost("om.isset", () => _host.IsVariableSet(name)));
        });

        // ---- utilities ----
        Define(om, "lastactivity", _ => DynValue.NewNumber(CallHost("om.lastactivity", () => _host.SecondsSinceLastActivity)));

        Define(om, "removecolors", args => DynValue.NewString(AnsiRegex.Replace(Text(args, 0, "om.removecolors"), string.Empty)));

        Define(om, "match", args =>
        {
            var text = Text(args, 0, "om.match");
            var pattern = Text(args, 1, "om.match");
            Match match;
            try
            {
                match = CachedRegex(pattern).Match(text);
            }
            catch (RegexMatchTimeoutException)
            {
                throw new ScriptRuntimeException("om.match: the pattern took too long to evaluate");
            }
            catch (ArgumentException ex)
            {
                throw new ScriptRuntimeException($"om.match: invalid pattern: {ex.Message}");
            }

            if (!match.Success) return DynValue.Nil;

            var result = new Table(_lua);
            result.Set(DynValue.NewNumber(0), DynValue.NewString(match.Value));
            for (int i = 1; i < match.Groups.Count; i++)
            {
                if (match.Groups[i].Success) result.Set(i, DynValue.NewString(match.Groups[i].Value));
            }
            foreach (Group group in match.Groups)
            {
                if (group.Success && !int.TryParse(group.Name, out _)) result.Set(group.Name, DynValue.NewString(group.Value));
            }
            return DynValue.NewTable(result);
        });

        // ---- waiting ----
        Define(om, "sleep", args =>
        {
            EnsureWaitingAllowed("om.sleep");
            double seconds = Number(args, 0, "om.sleep");
            if (double.IsNaN(seconds) || seconds < 0)
                throw new ScriptRuntimeException("om.sleep: seconds must be zero or positive");
            var duration = TimeSpan.FromSeconds(Math.Min(seconds, MaxSleep.TotalSeconds));
            return Yield(new SleepRequest(duration), "om.sleep");
        });

        Define(om, "countdown", args =>
        {
            EnsureWaitingAllowed("om.countdown");
            double ticks = Number(args, 0, "om.countdown");
            if (double.IsNaN(ticks) || ticks < 0 || ticks > int.MaxValue)
                throw new ScriptRuntimeException("om.countdown: ticks must be zero or positive");

            string unit = "s";
            string? during = null, finish = null;
            if (Options(args, 1, "om.countdown") is { } options)
            {
                unit = OptionalText(options, "unit", "om.countdown") ?? "s";
                during = OptionalText(options, "during", "om.countdown");
                finish = OptionalText(options, "finish", "om.countdown");
            }

            var unitMatch = CountdownUnitRegex.Match(unit);
            if (!unitMatch.Success)
                throw new ScriptRuntimeException($"om.countdown: invalid unit '{unit}' (use s, d, c or m, optionally with a multiplier such as \"2s\")");
            double multiplier = unitMatch.Groups[1].Success ? int.Parse(unitMatch.Groups[1].Value) : 1;
            double unitMilliseconds = unitMatch.Groups[2].Value switch { "s" => 1000, "d" => 100, "c" => 10, _ => 1 };

            double total = Math.Min(Math.Floor(ticks) * multiplier * unitMilliseconds, TimeSpan.FromDays(1).TotalMilliseconds);
            if (string.IsNullOrEmpty(during)) during = null;
            if (string.IsNullOrEmpty(finish)) finish = null;
            return Yield(new CountdownRequest(TimeSpan.FromMilliseconds(total), during, finish), "om.countdown");
        });

        // ---- timers ----
        Define(om, "timer", args =>
        {
            EnsureWaitingAllowed("om.timer");
            var name = NonEmptyText(args, 0, "om.timer");
            double seconds = Number(args, 1, "om.timer");
            var function = Arg(args, 2);
            if (function.Type != DataType.Function)
                throw new ScriptRuntimeException($"om.timer: argument #3 must be a function (got {function.Type.ToLuaTypeString()})");
            if (double.IsNaN(seconds) || seconds < 0)
                throw new ScriptRuntimeException("om.timer: seconds must be zero or positive");
            bool repeating = Arg(args, 3).CastToBool();

            var interval = TimeSpan.FromSeconds(Math.Min(seconds, MaxTimerInterval.TotalSeconds));
            if (interval < MinTimerInterval) interval = MinTimerInterval;
            try
            {
                _engine.SetTimer(this, name, interval, function, repeating);
            }
            catch (InvalidOperationException ex)
            {
                throw new ScriptRuntimeException($"om.timer: {ex.Message}");
            }
            return DynValue.Nil;
        });

        Define(om, "canceltimer", args => DynValue.NewBoolean(_engine.CancelTimer(NonEmptyText(args, 0, "om.canceltimer"))));

        // ---- data ----
        om["line"] = _context.MatchedLine;
        var block = string.IsNullOrEmpty(_context.Block) ? _context.MatchedLine : _context.Block;
        om["block"] = block;

        var lines = new Table(_lua);
        IReadOnlyList<string> sourceLines = _context.Lines ?? block.Replace("\r", string.Empty).Split('\n');
        for (int i = 0; i < sourceLines.Count; i++)
            lines[i + 1] = sourceLines[i];
        om["lines"] = lines;

        var captures = new Table(_lua);
        for (int i = 0; i < _context.Captures.Count; i++)
            captures[i + 1] = _context.Captures[i];
        om["captures"] = captures;
        om["args"] = captures;
        om["command"] = _context.FullCommand is null ? DynValue.Nil : DynValue.NewString(_context.FullCommand);

        var mud = new Table(_lua);
        mud["name"] = _context.MudName ?? string.Empty;
        om["mud"] = mud;
        var character = new Table(_lua);
        character["name"] = _context.CharacterName ?? string.Empty;
        om["character"] = character;

        foreach (var (name, code) in AnsiConstants)
            om[name] = $"[{code}m";
    }

    /// <summary>
    /// om.match patterns, parsed once. The static Regex cache holds 15 expressions, fewer than one
    /// message-rules script uses for every block it sees.
    /// </summary>
    private static Regex CachedRegex(string pattern)
    {
        if (MatchCache.TryGetValue(pattern, out var regex)) return regex;
        regex = new Regex(pattern, RegexOptions.CultureInvariant, MatchTimeout);
        if (MatchCache.Count >= MatchCacheCapacity) MatchCache.Clear();
        MatchCache[pattern] = regex;
        return regex;
    }

    private static void Define(Table table, string name, Func<CallbackArguments, DynValue> body)
        => table[name] = DynValue.NewCallback((_, args) => body(args), "om." + name);

    private void EnsureWaitingAllowed(string function)
    {
        if (!_context.AllowWaiting)
            throw new ScriptRuntimeException($"{function}: not available here (this script cannot wait nor set timers)");
    }

    private DynValue Yield(WaitRequest request, string function)
    {
        var fiber = _current ?? throw new ScriptRuntimeException($"{function}: cannot wait here");
        fiber.Pending = request;
        // Inside a function called back by native code (table.sort, string.gsub) MoonSharp refuses
        // the yield with "attempt to yield across a CLR-call boundary", which is the right error.
        return DynValue.NewYieldReq([]);
    }

    private static DynValue Arg(CallbackArguments args, int index)
        => index < args.Count ? args[index] : DynValue.Nil;

    private static string Text(CallbackArguments args, int index, string function)
    {
        var value = Arg(args, index);
        return value.Type is DataType.String or DataType.Number
            ? value.CastToString()
            : throw new ScriptRuntimeException($"{function}: argument #{index + 1} must be a string (got {value.Type.ToLuaTypeString()})");
    }

    private static string NonEmptyText(CallbackArguments args, int index, string function)
    {
        var text = Text(args, index, function);
        return text.Length > 0 ? text : throw new ScriptRuntimeException($"{function}: argument #{index + 1} must not be empty");
    }

    private static double Number(CallbackArguments args, int index, string function)
    {
        var value = Arg(args, index);
        return value.CastToNumber()
            ?? throw new ScriptRuntimeException($"{function}: argument #{index + 1} must be a number (got {value.Type.ToLuaTypeString()})");
    }

    private static Table? Options(CallbackArguments args, int index, string function)
    {
        var value = Arg(args, index);
        return value.Type switch
        {
            DataType.Nil or DataType.Void => null,
            DataType.Table => value.Table,
            _ => throw new ScriptRuntimeException($"{function}: argument #{index + 1} must be a table of options (got {value.Type.ToLuaTypeString()})")
        };
    }

    private static string? OptionalText(Table options, string key, string function)
    {
        var value = options.Get(key);
        return value.Type switch
        {
            DataType.Nil or DataType.Void => null,
            DataType.String or DataType.Number => value.CastToString(),
            _ => throw new ScriptRuntimeException($"{function}: option '{key}' must be a string (got {value.Type.ToLuaTypeString()})")
        };
    }

    private static double? OptionalNumber(Table options, string key, string function)
    {
        var value = options.Get(key);
        if (value.IsNil()) return null;
        return value.CastToNumber()
            ?? throw new ScriptRuntimeException($"{function}: option '{key}' must be a number (got {value.Type.ToLuaTypeString()})");
    }

    private static int? OptionalInt(Table options, string key, string function)
        => OptionalNumber(options, key, function) is { } number ? ToInt(number, function, key) : null;

    private static int ToInt(double number, string function, string what)
        => double.IsNaN(number) || number < int.MinValue || number > int.MaxValue
            ? throw new ScriptRuntimeException($"{function}: '{what}' is out of range")
            : (int)number;

    private static void EnsureValidRegex(string pattern, string function)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.None, MatchTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new ScriptRuntimeException($"{function}: invalid pattern: {ex.Message}");
        }
    }
}
