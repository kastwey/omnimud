using FluentAssertions;
using Omnimud.Core.Session;

namespace Omnimud.Core.Tests.Session;

public sealed class MudSessionGetTests : IAsyncDisposable
{
    private static readonly TimeSpan ThreeSeconds = TimeSpan.FromSeconds(3);
    private readonly SessionHarness _h = new();

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private async Task<Task<string?>> GetAsync(string command, string? pattern = null, bool keepColors = false, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var task = _h.Session.GetAsync(command, pattern, timeout ?? ThreeSeconds, keepColors, ct);
        await _h.Session.WhenIdleAsync();
        return task;
    }

    [Fact]
    public async Task Get_SendsTheCommandRaw_AndCapturesUntilThePrompt()
    {
        _h.Store.Aliases.Add(new Omnimud.Core.Aliases.AliasDefinition("pv", "NO"));
        _h.Store.Triggers.Add(SessionHarness.Trigger("Vida", "NO"));
        _h.Store.Rules.Add(new MessageRule("Vida", "NO"));
        await _h.StartAsync();

        var task = await GetAsync("pv");
        _h.SentLines.Should().Equal("pv");
        await _h.ReceiveAsync("Vida: 40/120\nManá: 3/10\n> ");
        task.IsCompleted.Should().BeFalse();
        await _h.AdvanceAsync(150);

        (await task).Should().Be("Vida: 40/120\nManá: 3/10");
        _h.Lines.Should().BeEmpty();
        _h.Spoken.Should().BeEmpty();
        _h.AddedMessages.Should().BeEmpty();
        _h.SentLines.Should().Equal("pv"); // no trigger fired
    }

    [Fact]
    public async Task Get_EndsAfterAQuietPeriod_WhenThereIsNoPrompt()
    {
        await _h.StartAsync();

        var task = await GetAsync("pv");
        await _h.ReceiveAsync("Vida: 40/120\n");
        await _h.AdvanceAsync(100);
        task.IsCompleted.Should().BeFalse();
        await _h.ReceiveAsync("Maná: 3/10\n");
        await _h.AdvanceAsync(100);
        task.IsCompleted.Should().BeFalse();
        await _h.AdvanceAsync(50);

        (await task).Should().Be("Vida: 40/120\nManá: 3/10");
    }

    [Fact]
    public async Task Get_EndsImmediatelyOnGoAhead()
    {
        await _h.StartAsync();

        var task = await GetAsync("pv");
        await _h.ReceiveBytesAsync([.. "Vida: 1\n> "u8, 255, 249]);

        (await task).Should().Be("Vida: 1");
        _h.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_AfterCompletion_TextFlowsNormallyAgain()
    {
        await _h.StartAsync();
        var task = await GetAsync("pv");
        await _h.ReceiveAsync("Vida: 1\n");
        await _h.AdvanceAsync(150);
        await task;

        await _h.ReceiveAsync("Un orco llega.\n");

        _h.PlainLines.Should().Equal("Un orco llega.");
    }

    [Fact]
    public async Task Get_WithPattern_CapturesOnlyMatchingLines_TheRestIsShown()
    {
        await _h.StartAsync();

        var task = await GetAsync("pv", @"^Vida: \d+");
        await _h.ReceiveAsync("Ana dice: hola\nVida: 40\nLlueve.\n");
        await _h.AdvanceAsync(150);

        (await task).Should().Be("Vida: 40");
        _h.PlainLines.Should().Equal("Ana dice: hola", "Llueve.");
    }

    [Fact]
    public async Task Get_WithPattern_APromptBeforeAnyMatchDoesNotEndIt()
    {
        await _h.StartAsync();

        var task = await GetAsync("pv", "^Vida");
        await _h.ReceiveAsync("> ");
        await _h.AdvanceAsync(150);
        task.IsCompleted.Should().BeFalse();
        await _h.ReceiveAsync("Vida: 9\n");
        await _h.AdvanceAsync(150);

        (await task).Should().Be("Vida: 9");
        _h.PlainLines.Should().Equal("> ");
    }

    [Fact]
    public async Task Get_KeepColors_ReturnsRawText_OtherwisePlain()
    {
        await _h.StartAsync();
        var colored = $"{SessionHarness.Esc}[32mVida: 9{SessionHarness.Esc}[0m";

        var withColors = await GetAsync("pv", keepColors: true);
        await _h.ReceiveAsync(colored + "\n");
        await _h.AdvanceAsync(150);
        var plain = await GetAsync("pv");
        await _h.ReceiveAsync(colored + "\n");
        await _h.AdvanceAsync(150);

        (await withColors).Should().Be(colored);
        (await plain).Should().Be("Vida: 9");
    }

    [Fact]
    public async Task Get_TimesOut_WithNull()
    {
        await _h.StartAsync();

        var task = await GetAsync("pv", timeout: TimeSpan.FromSeconds(3));
        await _h.AdvanceAsync(2999);
        task.IsCompleted.Should().BeFalse();
        await _h.AdvanceAsync(1);

        (await task).Should().BeNull();
    }

    [Fact]
    public async Task Get_TwoConcurrentRequests_AreQueued_AndNeverMixReplies()
    {
        await _h.StartAsync();

        var first = _h.Session.GetAsync("pv", null, ThreeSeconds, false, default);
        var second = _h.Session.GetAsync("oro", null, ThreeSeconds, false, default);
        await _h.Session.WhenIdleAsync();
        _h.SentLines.Should().Equal("pv"); // the second command waits for its turn

        await _h.ReceiveAsync("Vida: 40\n");
        await _h.AdvanceAsync(150);
        (await first).Should().Be("Vida: 40");
        _h.SentLines.Should().Equal("pv", "oro");

        await _h.ReceiveAsync("Oro: 7\n");
        await _h.AdvanceAsync(150);
        (await second).Should().Be("Oro: 7");
        _h.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_QueuedRequest_StartsAfterTheFirstTimesOut_WithItsOwnTimeout()
    {
        await _h.StartAsync();

        var first = _h.Session.GetAsync("a", null, TimeSpan.FromSeconds(1), false, default);
        var second = _h.Session.GetAsync("b", null, TimeSpan.FromSeconds(1), false, default);
        await _h.Session.WhenIdleAsync();
        await _h.AdvanceAsync(1000);

        (await first).Should().BeNull();
        second.IsCompleted.Should().BeFalse();
        _h.SentLines.Should().Equal("a", "b");

        await _h.ReceiveAsync("respuesta b\n");
        await _h.AdvanceAsync(150);
        (await second).Should().Be("respuesta b");
    }

    [Fact]
    public async Task Get_PendingPromptFromBefore_IsShown_NotCaptured()
    {
        await _h.StartAsync();
        await _h.ReceiveAsync("Vida: 5> ");

        var task = await GetAsync("pv");
        await _h.ReceiveAsync("Vida: 5/10\n");
        await _h.AdvanceAsync(150);

        (await task).Should().Be("Vida: 5/10");
        _h.PlainLines.Should().Equal("Vida: 5> ");
    }

    [Fact]
    public async Task Get_IsNotLogged()
    {
        _h.SetOptions(o => o with { LogType = Omnimud.Core.Options.LogMode.PerDay });
        await _h.StartAsync();

        var task = await GetAsync("pv");
        await _h.ReceiveAsync("Vida secreta\n");
        await _h.AdvanceAsync(150);
        await task;
        await _h.Session.CloseAsync(false);

        var log = File.ReadAllText(Directory.GetFiles(_h.LogDirectory, "*.log", SearchOption.AllDirectories).Single());
        log.Should().NotContain("pv").And.NotContain("Vida secreta");
    }

    [Fact]
    public async Task Get_WhenNotConnected_ReturnsNull()
    {
        await _h.StartAsync(connect: false);

        var result = await _h.Session.GetAsync("pv", null, ThreeSeconds, false, default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Get_Disconnection_CompletesEveryPendingRequestWithNull()
    {
        await _h.StartAsync();
        var first = _h.Session.GetAsync("a", null, ThreeSeconds, false, default);
        var second = _h.Session.GetAsync("b", null, ThreeSeconds, false, default);
        await _h.Session.WhenIdleAsync();

        await _h.Connection.RaiseServerClosed();
        await _h.Session.WhenIdleAsync();

        (await first).Should().BeNull();
        (await second).Should().BeNull();
    }

    [Fact]
    public async Task Get_Cancellation_CancelsTheTask_AndFreesTheQueue()
    {
        await _h.StartAsync();
        using var cts = new CancellationTokenSource();

        var first = await GetAsync("a", ct: cts.Token);
        var second = await GetAsync("b");
        cts.Cancel();
        await _h.Session.WhenIdleAsync();

        await FluentActions.Awaiting(() => first).Should().ThrowAsync<OperationCanceledException>();
        _h.SentLines.Should().Equal("a", "b");
        await _h.ReceiveAsync("para b\n");
        await _h.AdvanceAsync(150);
        (await second).Should().Be("para b");
    }

    [Fact]
    public async Task Get_InvalidPattern_ReturnsNull_AndTellsTheUser()
    {
        await _h.StartAsync();

        var result = await _h.Session.GetAsync("pv", "(sin cerrar", ThreeSeconds, false, default);
        await _h.Session.WhenIdleAsync();

        result.Should().BeNull();
        _h.SystemLines.Single().Should().Contain("om.get");
        _h.SentLines.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_MspLinesInTheReply_StillGoToSound()
    {
        await _h.StartAsync();

        var task = await GetAsync("pv");
        await _h.ReceiveAsync("!!SOUND(latido.wav)\nVida: 1\n");
        await _h.AdvanceAsync(150);

        (await task).Should().Be("Vida: 1");
    }

    [Fact]
    public async Task Get_FromAScript_BlockingItsThread_DoesNotDeadlockTheSession()
    {
        await _h.StartAsync();

        // This is what the Lua engine does: block the script thread on the result.
        var script = Task.Run(() => _h.Session.GetAsync("pv", null, ThreeSeconds, false, default).GetAwaiter().GetResult());
        await _h.WaitUntilAsync(() => _h.SentLines.Count == 1);
        await _h.ReceiveAsync("Vida: 3\n");
        await _h.AdvanceAsync(150);

        (await script).Should().Be("Vida: 3");
    }
}
