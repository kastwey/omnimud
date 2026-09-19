using FluentAssertions;
using NSubstitute;
using Omnimud.Core.Connection;
using Omnimud.Core.Options;
using Omnimud.Core.Session;
using Omnimud.Core.Sound;

namespace Omnimud.Core.Tests.Session;

/// <summary>How the session uses <see cref="IProxySettingsResolver"/>: on every connection attempt and every time the options change.</summary>
public sealed class MudSessionProxyTests : IAsyncDisposable
{
    private readonly SessionHarness _h = new();
    private readonly IProxySettingsResolver _resolver = Substitute.For<IProxySettingsResolver>();

    public MudSessionProxyTests()
    {
        _resolver.ForDownloads(Arg.Any<OmnimudOptions>()).Returns(DownloadProxySettings.Direct);
        _h.ProxySettings = _resolver;
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    [Fact]
    public async Task Connect_AsksTheResolver_WithTheOptionsAndTheDestination_AndUsesItsAnswer()
    {
        var proxy = new ProxyConfig("proxy", 3128, ProxyProtocol.HttpConnect, "juan", "s3cret");
        _resolver.ForMud(Arg.Any<OmnimudOptions>(), "mud.example", 4000).Returns(proxy);
        _h.SetOptions(o => o with { ProxyType = ProxyMode.Automatic, UseProxyForMud = true });

        await _h.StartAsync();

        _h.Connection.LastConfig!.Proxy.Should().BeSameAs(proxy);
        _resolver.Received(1).ForMud(Arg.Is<OmnimudOptions>(o => o.ProxyType == ProxyMode.Automatic && o.UseProxyForMud), "mud.example", 4000);
    }

    [Fact]
    public async Task Connect_ResolverSaysDirect_ConnectsWithoutProxy()
    {
        _resolver.ForMud(Arg.Any<OmnimudOptions>(), Arg.Any<string>(), Arg.Any<int>()).Returns((ProxyConfig?)null);

        await _h.StartAsync();

        _h.Connection.LastConfig!.Proxy.Should().BeNull();
    }

    [Fact]
    public async Task Reconnect_AfterTheOptionsChanged_ResolvesAgainWithTheNewOptions()
    {
        _resolver.ForMud(Arg.Any<OmnimudOptions>(), Arg.Any<string>(), Arg.Any<int>())
            .Returns(call => call.Arg<OmnimudOptions>().UseProxyForMud ? new ProxyConfig("proxy", 1080) : null);
        await _h.StartAsync();
        _h.Connection.LastConfig!.Proxy.Should().BeNull();

        await _h.OptionsService.SaveAsync(OptionScope.Global, null,
            _h.OptionsService.Current with { ProxyType = ProxyMode.Manual, ProxyHost = "proxy", ProxyPort = 1080, UseProxyForMud = true });
        await _h.Session.WhenIdleAsync();
        await _h.Connection.RaiseServerClosed();
        await _h.Session.WhenIdleAsync();
        await _h.Session.ConnectAsync();

        _h.Connection.ConnectCount.Should().Be(2);
        _h.Connection.LastConfig!.Proxy.Should().Be(new ProxyConfig("proxy", 1080));
    }

    [Fact]
    public async Task Reconnect_ResolvesEveryTime_BecauseTheSystemProxyMayHaveChanged()
    {
        var answers = new Queue<ProxyConfig?>([new ProxyConfig("first", 1080), new ProxyConfig("second", 1080)]);
        _resolver.ForMud(Arg.Any<OmnimudOptions>(), Arg.Any<string>(), Arg.Any<int>()).Returns(_ => answers.Dequeue());
        await _h.StartAsync();

        await _h.Connection.RaiseServerClosed();
        await _h.Session.WhenIdleAsync();
        await _h.Session.ConnectAsync();

        _h.Connection.LastConfig!.Proxy!.Host.Should().Be("second");
    }

    [Fact]
    public async Task AResolverThatFails_FailsTheAttempt_AndLeavesTheSessionDisconnected()
    {
        _resolver.ForMud(Arg.Any<OmnimudOptions>(), Arg.Any<string>(), Arg.Any<int>()).Returns(_ => throw new InvalidOperationException("boom"));
        await _h.StartAsync(connect: false);

        var act = () => _h.Session.ConnectAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
        _h.Session.State.Should().Be(SessionState.Disconnected);
        _h.Connection.ConnectCount.Should().Be(0);
    }

    [Fact]
    public async Task Sound_GetsTheDownloadProxy_AtStart_AndAgainWhenTheOptionsChange()
    {
        var manual = new DownloadProxySettings(DownloadProxyKind.Manual, new Uri("http://proxy:3128"), "juan", "s3cret");
        _resolver.ForDownloads(Arg.Any<OmnimudOptions>())
            .Returns(call => call.Arg<OmnimudOptions>().ProxyType == ProxyMode.Manual ? manual : DownloadProxySettings.Direct);
        await _h.StartAsync();
        _h.Sound.Received().Configure(Arg.Is<SoundSettings>(s => s.DownloadProxySettings == DownloadProxySettings.Direct && s.DownloadProxy == null));

        await _h.OptionsService.SaveAsync(OptionScope.Global, null, _h.OptionsService.Current with { ProxyType = ProxyMode.Manual });
        await _h.Session.WhenIdleAsync();

        _h.Sound.Received().Configure(Arg.Is<SoundSettings>(s => s.DownloadProxySettings == manual && s.DownloadProxy == manual.Address));
    }

    [Fact]
    public async Task WithoutAResolver_AManualProxyStillWorks_WithoutPassword_AndAutomaticMeansDirect()
    {
        await using var manual = new SessionHarness();
        manual.SetOptions(o => o with
        {
            ProxyType = ProxyMode.Manual, ProxyHost = "proxy", ProxyPort = 1080, UseProxyForMud = true,
            ProxyUsername = "juan", ProxyPasswordProtected = "bm8gc2UgcHVlZGUgZGVzY2lmcmFy"
        });
        await manual.StartAsync();
        manual.Connection.LastConfig!.Proxy.Should().Be(new ProxyConfig("proxy", 1080, ProxyProtocol.Socks5, "juan"));

        await using var automatic = new SessionHarness();
        automatic.SetOptions(o => o with { ProxyType = ProxyMode.Automatic, UseProxyForMud = true });
        await automatic.StartAsync();
        automatic.Connection.LastConfig!.Proxy.Should().BeNull();
    }
}
