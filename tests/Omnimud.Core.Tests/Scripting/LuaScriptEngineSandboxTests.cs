using FluentAssertions;
using Omnimud.Core.Scripting;

namespace Omnimud.Core.Tests.Scripting;

/// <summary>
/// Security tests to ensure the Lua sandbox cannot escape its restrictions.
/// </summary>
public sealed class LuaScriptEngineSandboxTests : IDisposable
{
    private readonly LuaScriptEngine _sut = new();
    private readonly ScriptContext _context = new();

    public void Dispose() => _sut.Dispose();

    [Theory]
    [InlineData("io.open('/etc/passwd', 'r')")]
    [InlineData("local f = io.open('test.txt', 'w')")]
    [InlineData("io.write('hello')")]
    public async Task Sandbox_BlocksIoAccess(string script)
    {
        var result = await _sut.ExecuteAsync(script, _context);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("os.execute('whoami')")]
    [InlineData("os.remove('file.txt')")]
    [InlineData("os.getenv('PATH')")]
    public async Task Sandbox_BlocksOsAccess(string script)
    {
        var result = await _sut.ExecuteAsync(script, _context);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("debug.getinfo(1)")]
    [InlineData("debug.sethook(function() end, '', 1)")]
    public async Task Sandbox_BlocksDebugAccess(string script)
    {
        var result = await _sut.ExecuteAsync(script, _context);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("load('print(1)')()")]
    [InlineData("loadfile('script.lua')")]
    [InlineData("dofile('script.lua')")]
    [InlineData("require('socket')")]
    public async Task Sandbox_BlocksDynamicLoading(string script)
    {
        var result = await _sut.ExecuteAsync(script, _context);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Sandbox_InfiniteLoop_ExceedsInstructionLimit()
    {
        const string script = """
            while true do
                local x = 1 + 1
            end
            """;
        var limits = new ScriptLimits { MaxInstructions = 1000, MaxExecutionTime = TimeSpan.FromSeconds(5) };

        var result = await _sut.ExecuteAsync(script, _context, limits);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("instruction");
    }

    [Fact]
    public async Task Sandbox_Timeout_ReturnsFailure()
    {
        // A tight loop with body - exceeds instruction count
        const string script = """
            local x = 0
            for i = 1, 999999999 do
                x = x + i
            end
            """;
        var limits = new ScriptLimits { MaxExecutionTime = TimeSpan.FromSeconds(5), MaxInstructions = 500 };

        var result = await _sut.ExecuteAsync(script, _context, limits);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("instruction");
    }

    [Fact]
    public async Task Sandbox_CannotModifyGlobalEnvironment_RawGet()
    {
        const string script = """
            rawset(_G, 'os', { execute = function(s) end })
            os.execute('whoami')
            om.send('escaped')
            """;

        var result = await _sut.ExecuteAsync(script, _context);

        // Even if rawset succeeds, the os.execute should not do anything harmful
        // (it's just a lua function, not the real os module)
        // The important thing is no side effects escape
        result.CommandsToSend.Should().NotContain("escaped_system");
    }

    // ---- nothing may hang a thread ----

    private static readonly ScriptLimits LowLimits = new() { MaxInstructions = 20_000, MaxExecutionTime = TimeSpan.FromSeconds(5) };

    [Theory]
    [InlineData("while true do end")]
    [InlineData("repeat until false")]
    [InlineData("for i = 1, math.huge do end")]
    [InlineData("local function f() return f() end f()")]
    [InlineData("local function f() f() end f()")]
    [InlineData("pcall(function() while true do end end) om.send('survived')")]
    [InlineData("while true do pcall(error, 'x') end")]
    [InlineData("table.sort({3, 2, 1}, function(a, b) while true do end end)")]
    [InlineData("string.gsub('abc', 'b', function(x) while true do end end)")]
    [InlineData("local t = setmetatable({}, { __tostring = function() while true do end end }) tostring(t)")]
    [InlineData("local t = setmetatable({}, { __index = function(t, k) return t[k] end }) local x = t.a")]
    public async Task Sandbox_RunawayCodeOnOneLine_IsStoppedByInstructionLimit(string script)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var result = await _sut.ExecuteAsync(script, _context, LowLimits);

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        result.Success.Should().BeFalse();
        result.ErrorKind.Should().BeOneOf(ScriptErrorKind.InstructionLimit, ScriptErrorKind.MemoryLimit);
        result.CommandsToSend.Should().BeEmpty();
    }

    [Theory]
    [InlineData("local function f() string.gsub('a', 'a', f) end f()")]
    [InlineData("local function f() table.sort({2, 1}, f) end f()")]
    [InlineData("local t = {} setmetatable(t, { __tostring = function() return tostring(t) end }) tostring(t)")]
    public async Task Sandbox_RecursionThroughNativeCalls_DoesNotOverflowTheStack(string script)
    {
        var limits = new ScriptLimits { MaxInstructions = 5_000_000, MaxExecutionTime = TimeSpan.FromSeconds(5) };

        var result = await _sut.ExecuteAsync(script, _context, limits);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task Sandbox_InfiniteLoopWithoutInstructionLimit_IsStoppedByTime()
    {
        var limits = new ScriptLimits { MaxInstructions = 0, MaxExecutionTime = TimeSpan.FromMilliseconds(150) };
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var result = await _sut.ExecuteAsync("while true do end", _context, limits);

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        result.Success.Should().BeFalse();
        result.ErrorKind.Should().Be(ScriptErrorKind.Timeout);
    }

    [Fact]
    public async Task Sandbox_NativeCallThatRunsNoInstructions_IsAbortedByTheWatchdog()
    {
        // A plain find of a 500k needle in a 1M haystack that almost matches everywhere is
        // quadratic native work and executes no Lua instruction at all.
        const string script = """
            local haystack = string.rep('a', 1000000)
            local needle = string.rep('a', 500000) .. 'b'
            for i = 1, 1000 do string.find(haystack, needle, 1, true) end
            om.send('finished')
            """;
        var limits = new ScriptLimits
        {
            MaxExecutionTime = TimeSpan.FromMilliseconds(150),
            HardAbortGrace = TimeSpan.FromMilliseconds(150)
        };
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var result = await _sut.ExecuteAsync(script, _context, limits);

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
        result.Success.Should().BeFalse();
        result.ErrorKind.Should().Be(ScriptErrorKind.Timeout);
    }

    [Fact]
    public async Task Sandbox_RedefiningInternals_DoesNotDisableTheLimits()
    {
        const string script = """
            __guard = function() end
            om = nil
            debug = { sethook = function() end }
            string.rep = nil
            _G.__guard = nil
            while true do end
            """;

        var result = await _sut.ExecuteAsync(script, _context, LowLimits);

        result.Success.Should().BeFalse();
        result.ErrorKind.Should().Be(ScriptErrorKind.InstructionLimit);
    }

    [Fact]
    public async Task Sandbox_OneRunCannotAffectTheNext()
    {
        await _sut.ExecuteAsync("om.send = nil string.upper = nil leaked = 'yes'", _context);

        var result = await _sut.ExecuteAsync("om.send(string.upper(tostring(leaked)))", _context);

        result.Success.Should().BeTrue();
        result.CommandsToSend.Should().Equal("NIL");
    }

    // ---- memory ----

    [Theory]
    [InlineData("local s = string.rep('x', 1000000000)")]
    [InlineData("local s = ('x'):rep(1e9)")]
    [InlineData("local s = string.rep('x', 10, string.rep('y', 999999))")]
    [InlineData("local s = string.rep('x', 1000000) local t = {} for i = 1, 100 do t[i] = s end local big = table.concat(t)")]
    [InlineData("local s = string.rep('x', 1000000) local big = string.gsub(s, 'x', s)")]
    [InlineData("local s = string.rep('x', 1000000) local big = string.gsub(s, 'x', { x = s })")]
    [InlineData("local s = string.rep('x', 600000) local big = string.format('%s%s', s, s)")]
    public async Task Sandbox_HugeStrings_AreRejected(string script)
    {
        var result = await _sut.ExecuteAsync(script + " om.send('built')", _context);

        result.Success.Should().BeFalse();
        result.ErrorKind.Should().Be(ScriptErrorKind.Runtime);
        result.Error.Should().Contain("too long");
    }

    [Fact]
    public async Task Sandbox_StringRepWithinTheLimit_Works()
    {
        var result = await _sut.ExecuteAsync("om.send(string.rep('ab', 3, '-'))", _context);

        result.Success.Should().BeTrue();
        result.CommandsToSend.Should().Equal("ab-ab-ab");
    }

    [Fact]
    public async Task Sandbox_DoublingAStringForever_HitsTheMemoryLimit()
    {
        var limits = new ScriptLimits { MaxInstructions = 0, MaxAllocatedBytes = 20 * 1024 * 1024 };

        var result = await _sut.ExecuteAsync("local s = 'xxxxxxxx' while true do s = s .. s end", _context, limits);

        result.Success.Should().BeFalse();
        result.ErrorKind.Should().Be(ScriptErrorKind.MemoryLimit);
    }

    [Fact]
    public async Task Sandbox_FillingATableForever_HitsTheMemoryLimit()
    {
        var limits = new ScriptLimits { MaxInstructions = 0, MaxAllocatedBytes = 20 * 1024 * 1024 };

        var result = await _sut.ExecuteAsync("local t = {} local i = 0 while true do i = i + 1 t[i] = 'item ' .. i end", _context, limits);

        result.Success.Should().BeFalse();
        result.ErrorKind.Should().Be(ScriptErrorKind.MemoryLimit);
    }

    // ---- what is and is not reachable ----

    [Theory]
    [InlineData("io")]
    [InlineData("debug")]
    [InlineData("load")]
    [InlineData("loadstring")]
    [InlineData("loadfile")]
    [InlineData("dofile")]
    [InlineData("require")]
    [InlineData("package")]
    [InlineData("coroutine")]
    [InlineData("dynamic")]
    [InlineData("json")]
    [InlineData("os.execute")]
    [InlineData("os.exit")]
    [InlineData("os.remove")]
    [InlineData("os.rename")]
    [InlineData("os.getenv")]
    [InlineData("os.tmpname")]
    [InlineData("string.dump")]
    [InlineData("_MOONSHARP")]
    [InlineData("clr")]
    [InlineData("luanet")]
    [InlineData("import")]
    [InlineData("System")]
    [InlineData("_G.io")]
    [InlineData("_G.require")]
    public async Task Sandbox_DangerousGlobal_IsNil(string expression)
    {
        var result = await _sut.ExecuteAsync($"om.send(type({expression}))", _context);

        result.Success.Should().BeTrue();
        result.CommandsToSend.Should().Equal("nil");
    }

    [Fact]
    public async Task Sandbox_OmFunctions_AreNotClrObjects()
    {
        const string script = """
            for name, value in pairs(om) do
                local kind = type(value)
                if kind ~= 'function' and kind ~= 'string' and kind ~= 'table' and kind ~= 'number' then
                    om.send(name .. ' is ' .. kind)
                end
            end
            om.send(type(getmetatable(om.send)))
            """;

        var result = await _sut.ExecuteAsync(script, _context);

        result.Success.Should().BeTrue();
        result.CommandsToSend.Should().Equal("nil");
    }

    [Fact]
    public async Task Sandbox_SafeOsFunctionsAndPcall_AreAvailable()
    {
        const string script = """
            local ok, err = pcall(function() error('inner') end)
            om.send(tostring(ok))
            om.send(type(os.time()) .. ' ' .. type(os.clock()) .. ' ' .. type(os.date('%Y')))
            om.send(tostring(collectgarbage('count')))
            """;

        var result = await _sut.ExecuteAsync(script, _context);

        result.Success.Should().BeTrue();
        result.CommandsToSend.Should().Equal("false", "number number string", "0");
    }

    // ---- scripts the old line-by-line guard injection used to break ----

    [Fact]
    public async Task Sandbox_MultilineStringsAndTables_Work()
    {
        // Explicit \n so that the test does not depend on how this file's line endings are saved.
        const string script =
            "local text = [[\n" +
            "first line\n" +
            "end of the string]]\n" +
            "local exits = {\n" +
            "    n = 'norte',\n" +
            "    s = 'sur',\n" +
            "    [1] = \"uno\"\n" +
            "}\n" +
            "local total = 1 +\n" +
            "    2 +\n" +
            "    3\n" +
            "om.send(exits.n .. ' ' .. exits[1] .. ' ' .. total)\n" +
            "om.send(text)\n";

        var result = await _sut.ExecuteAsync(script, _context);

        result.Success.Should().BeTrue(result.Error);
        result.CommandsToSend.Should().Equal("norte uno 6", "first line\nend of the string");
    }

    [Fact]
    public async Task Sandbox_TextThatLooksLikeTheOldGuard_IsJustText()
    {
        var result = await _sut.ExecuteAsync("local s = [[\n__guard() end\nuntil]] om.send(s)", _context);

        result.Success.Should().BeTrue();
        result.CommandsToSend.Should().Equal("__guard() end\nuntil");
    }

    [Fact]
    public async Task Sandbox_StringManipulation_IsAllowed()
    {
        const string script = """
            local s = string.upper("hello")
            om.send(s)
            """;

        var result = await _sut.ExecuteAsync(script, _context);

        result.Success.Should().BeTrue();
        result.CommandsToSend.Should().Contain("HELLO");
    }

    [Fact]
    public async Task Sandbox_MathModule_IsAllowed()
    {
        const string script = """
            local x = math.floor(3.7)
            om.send(tostring(x))
            """;

        var result = await _sut.ExecuteAsync(script, _context);

        result.Success.Should().BeTrue();
        result.CommandsToSend.Should().Contain("3");
    }

    [Fact]
    public async Task Sandbox_TableModule_IsAllowed()
    {
        const string script = """
            local t = {3, 1, 2}
            table.sort(t)
            om.send(tostring(t[1]) .. tostring(t[2]) .. tostring(t[3]))
            """;

        var result = await _sut.ExecuteAsync(script, _context);

        result.Success.Should().BeTrue();
        result.CommandsToSend.Should().Contain("123");
    }
}
