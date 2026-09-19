using FluentAssertions;
using Omnimud.Core.Scripting;

namespace Omnimud.Core.Tests.Scripting;

public sealed class LuaScriptEngineTests : IDisposable
{
    private readonly LuaScriptEngine _sut = new();
    private readonly ScriptContext _context = new()
    {
        MatchedLine = "You receive 100 gold coins.",
        Captures = ["100", "gold"]
    };

    public void Dispose() => _sut.Dispose();

    [Fact]
    public async Task ExecuteAsync_SimpleSend_ReturnsCommand()
    {
        var result = await _sut.ExecuteAsync("om.send('look')", _context);

        result.Success.Should().BeTrue();
        result.CommandsToSend.Should().Contain("look");
    }

    [Fact]
    public async Task ExecuteAsync_MultipleSends_ReturnsAllCommands()
    {
        const string script = """
            om.send('north')
            om.send('look')
            om.send('south')
            """;

        var result = await _sut.ExecuteAsync(script, _context);

        result.Success.Should().BeTrue();
        result.CommandsToSend.Should().HaveCount(3);
        result.CommandsToSend.Should().ContainInOrder("north", "look", "south");
    }

    [Fact]
    public async Task ExecuteAsync_Display_ReturnsDisplayMessage()
    {
        var result = await _sut.ExecuteAsync("om.display('Hello world')", _context);

        result.Success.Should().BeTrue();
        result.DisplayMessages.Should().Contain("Hello world");
    }

    [Fact]
    public async Task ExecuteAsync_Notify_ReturnsNotification()
    {
        var result = await _sut.ExecuteAsync("om.notify('Combat started!')", _context);

        result.Success.Should().BeTrue();
        result.Notifications.Should().Contain("Combat started!");
    }

    [Fact]
    public async Task ExecuteAsync_Variables_SetAndGet()
    {
        const string script = """
            om.setvar('hp', '100')
            local hp = om.getvar('hp')
            om.send('HP is ' .. hp)
            """;

        var result = await _sut.ExecuteAsync(script, _context);

        result.Success.Should().BeTrue();
        result.CommandsToSend.Should().Contain("HP is 100");
    }

    [Fact]
    public async Task ExecuteAsync_Variables_GetNonexistentReturnsNil()
    {
        const string script = """
            local val = om.getvar('nonexistent')
            if val == nil then
                om.send('nil')
            end
            """;

        var result = await _sut.ExecuteAsync(script, _context);

        result.Success.Should().BeTrue();
        result.CommandsToSend.Should().Contain("nil");
    }

    [Fact]
    public async Task ExecuteAsync_Variables_RemoveVar()
    {
        var ctx = new ScriptContext
        {
            Variables = new Dictionary<string, string> { ["target"] = "orc" }
        };

        const string script = """
            om.removevar('target')
            local val = om.getvar('target')
            if val == nil then
                om.send('removed')
            end
            """;

        var result = await _sut.ExecuteAsync(script, ctx);

        result.Success.Should().BeTrue();
        result.CommandsToSend.Should().Contain("removed");
    }

    [Fact]
    public async Task ExecuteAsync_Captures_AccessibleFromScript()
    {
        const string script = """
            local amount = om.captures[1]
            local currency = om.captures[2]
            om.send('got ' .. amount .. ' ' .. currency)
            """;

        var result = await _sut.ExecuteAsync(script, _context);

        result.Success.Should().BeTrue();
        result.CommandsToSend.Should().Contain("got 100 gold");
    }

    [Fact]
    public async Task ExecuteAsync_Line_AccessibleFromScript()
    {
        const string script = "om.send(om.line)";

        var result = await _sut.ExecuteAsync(script, _context);

        result.Success.Should().BeTrue();
        result.CommandsToSend.Should().Contain("You receive 100 gold coins.");
    }

    [Fact]
    public async Task ExecuteAsync_Log_AddsToDisplayWithPrefix()
    {
        var result = await _sut.ExecuteAsync("om.log('debug info')", _context);

        result.Success.Should().BeTrue();
        result.DisplayMessages.Should().Contain("[LOG] debug info");
    }

    [Fact]
    public async Task ExecuteAsync_SyntaxError_ReturnsFail()
    {
        var result = await _sut.ExecuteAsync("this is not lua code!!!!", _context);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("syntax error");
    }

    [Fact]
    public async Task ExecuteAsync_RuntimeError_ReturnsFail()
    {
        var result = await _sut.ExecuteAsync("error('boom')", _context);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("boom");
    }

    [Fact]
    public async Task ExecuteAsync_WithoutHost_CollectsEveryKindOfEffect()
    {
        const string script = """
            om.send('uno')
            om.sendraw('dos')
            om.display('pantalla')
            om.echo('eco')
            om.say('voz', true)
            om.message('mensaje')
            om.playsound('ding.wav')
            om.status('ignorado')
            om.gag()
            om.send(tostring(om.isset('x')) .. ' ' .. tostring(om.stopsound('ding.wav')) .. ' ' .. om.lastactivity())
            """;

        var result = await _sut.ExecuteAsync(script, _context);

        result.Success.Should().BeTrue(result.Error);
        result.ErrorKind.Should().Be(ScriptErrorKind.None);
        result.CommandsToSend.Should().Equal("uno", "dos", "false false 0");
        result.DisplayMessages.Should().Equal("pantalla", "eco");
        result.Notifications.Should().Equal("voz");
        result.Messages.Should().Equal("mensaje");
        result.SoundsToPlay.Should().Equal("ding.wav");
        result.Gagged.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_Failure_CarriesKindAndLine()
    {
        var result = await _sut.ExecuteAsync("om.send('a')\nerror('boom')", _context);

        result.Success.Should().BeFalse();
        result.ErrorKind.Should().Be(ScriptErrorKind.Runtime);
        result.ErrorLine.Should().Be(2);
        result.CommandsToSend.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_VariablesPersistInContext()
    {
        var ctx = new ScriptContext { Variables = new Dictionary<string, string>() };

        await _sut.ExecuteAsync("om.setvar('counter', '42')", ctx);

        ctx.Variables.Should().ContainKey("counter");
        ctx.Variables["counter"].Should().Be("42");
    }
}
