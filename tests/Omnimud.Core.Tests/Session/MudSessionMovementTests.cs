using FluentAssertions;
using Omnimud.Core.Aliases;
using Omnimud.Core.Session;

namespace Omnimud.Core.Tests.Session;

/// <summary>Movement keys of a session: effective command = configured ?? default for the UI language.</summary>
public sealed class MudSessionMovementTests : IAsyncDisposable
{
    private SessionHarness _h = new();

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    [Fact]
    public async Task NothingConfigured_Spanish_ArrowsAndPagesWorkOutOfTheBox()
    {
        await _h.StartAsync();

        foreach (var key in new[] { 10, 11, 12, 13, 14, 15 })
            (await _h.Session.ExecuteMovementAsync(key)).Should().BeTrue();

        _h.SentLines.Should().Equal("norte", "sur", "oeste", "este", "arriba", "abajo");
        _h.Session.History.Should().BeEmpty("movement never goes to the history");
    }

    [Fact]
    public async Task NothingConfigured_Spanish_NumpadCompass()
    {
        await _h.StartAsync();

        foreach (var key in new[] { 8, 2, 4, 6, 7, 9, 1, 3 })
            (await _h.Session.ExecuteMovementAsync(key)).Should().BeTrue();

        _h.SentLines.Should().Equal("norte", "sur", "oeste", "este", "noroeste", "noreste", "sudoeste", "sudeste");
    }

    [Fact]
    public async Task NothingConfigured_English_UsesEnglishDirections()
    {
        await _h.DisposeAsync();
        _h = new SessionHarness("en");
        await _h.StartAsync();

        foreach (var key in new[] { 10, 11, 12, 13, 14, 15, 7, 3 })
            (await _h.Session.ExecuteMovementAsync(key)).Should().BeTrue();

        _h.SentLines.Should().Equal("north", "south", "west", "east", "up", "down", "northwest", "southeast");
    }

    [Theory]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(5)]
    [InlineData(0)]
    [InlineData(18)]
    [InlineData(-1)]
    public async Task KeysWithoutDefault_AndUnknownCodes_DoNothing(int key)
    {
        await _h.StartAsync();

        _h.Session.HasMovement(key).Should().BeFalse();
        (await _h.Session.ExecuteMovementAsync(key)).Should().BeFalse();

        _h.SentLines.Should().BeEmpty();
    }

    [Fact]
    public async Task Configured_WinsOverTheDefault_AndTheRestKeepTheirDefaults()
    {
        _h.Store.Movements[10] = "n";
        _h.Store.Movements[16] = "entrar";
        await _h.StartAsync();

        await _h.Session.ExecuteMovementAsync(10);
        await _h.Session.ExecuteMovementAsync(16);
        await _h.Session.ExecuteMovementAsync(11);

        _h.SentLines.Should().Equal("n", "entrar", "sur");
    }

    [Fact]
    public async Task ConfiguredEmpty_SwitchesTheKeyOff_DespiteItsDefault()
    {
        _h.Store.Movements[10] = "";
        await _h.StartAsync();

        _h.Session.HasMovement(10).Should().BeFalse();
        (await _h.Session.ExecuteMovementAsync(10)).Should().BeFalse();
        _h.SentLines.Should().BeEmpty();
    }

    [Fact]
    public async Task Movements_ExposeTheEffectiveCommands_Synchronously()
    {
        _h.Store.Movements[8] = "n";
        _h.Store.Movements[5] = "mirar";
        await _h.StartAsync();

        var movements = _h.Session.Movements;

        movements[8].Should().Be("n");
        movements[5].Should().Be("mirar");
        movements[10].Should().Be("norte");
        movements.Should().NotContainKey(16).And.NotContainKey(17).And.NotContainKey(0);
        _h.Session.HasMovement(8).Should().BeTrue();
        _h.Session.HasMovement(10).Should().BeTrue();
        _h.Session.HasMovement(16).Should().BeFalse();
    }

    [Fact]
    public async Task BeforeInitialize_TheDefaultsAreAlreadyThere()
    {
        await _h.StartAsync(connect: false);
        var session = _h.Session;

        session.HasMovement(10).Should().BeTrue();
        session.Movements[14].Should().Be("arriba");
    }

    [Fact]
    public async Task Reload_PicksUpEditedKeys()
    {
        await _h.StartAsync();
        _h.Session.Movements[10].Should().Be("norte");

        _h.Store.Movements[10] = "n";
        _h.Store.Movements[17] = "salir";
        await _h.Session.ReloadAsync();

        _h.Session.Movements[10].Should().Be("n");
        _h.Session.HasMovement(17).Should().BeTrue();

        _h.Store.Movements.Clear();
        await _h.Session.ReloadAsync();

        _h.Session.Movements[10].Should().Be("norte", "restoring defaults means having nothing configured");
        _h.Session.HasMovement(17).Should().BeFalse();
    }

    [Fact]
    public async Task TheCommand_GoesThroughTheFullInputPipeline()
    {
        _h.Store.Aliases.Add(new AliasDefinition("norte", "escalar muro"));
        await _h.StartAsync();

        (await _h.Session.ExecuteMovementAsync(10)).Should().BeTrue();

        _h.SentLines.Should().Equal("escalar muro");
        _h.Session.History.Should().BeEmpty();
    }

    [Fact]
    public async Task MovementMode_DoesNotGateExecution_TheViewDecidesWhenToCall()
    {
        _h.Store.MovementMode = false;
        await _h.StartAsync();

        (await _h.Session.ExecuteMovementAsync(8)).Should().BeTrue();

        _h.SentLines.Should().Equal("norte");
    }
}
