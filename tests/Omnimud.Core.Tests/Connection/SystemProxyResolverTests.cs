using FluentAssertions;
using Omnimud.Core.Connection;
using Omnimud.Core.Options;

namespace Omnimud.Core.Tests.Connection;

/// <summary>The decision logic only, over a fake configuration source: the real Windows settings are never read or changed here.</summary>
public sealed class SystemProxyResolverTests
{
    private sealed class FakeSource : ISystemProxySource
    {
        public SystemProxySettings Settings { get; set; } = SystemProxySettings.None;
        public Func<Uri, Uri?> WebProxy { get; set; } = _ => null;
        public List<Uri> Asked { get; } = [];
        public bool ThrowOnRead { get; set; }

        public SystemProxySettings ReadSettings() =>
            ThrowOnRead ? throw new InvalidOperationException("registry unavailable") : Settings;

        public Uri? GetWebProxy(Uri destination)
        {
            Asked.Add(destination);
            return WebProxy(destination);
        }
    }

    private readonly FakeSource _source = new();

    private SystemProxy? Resolve(string host = "mud.example.org", int port = 4000) =>
        new SystemProxyResolver(_source).Resolve(host, port);

    private void Static(string server, string? bypass = null, bool enabled = true) =>
        _source.Settings = new SystemProxySettings(enabled, server, bypass);

    // ── no proxy ─────────────────────────────────────────────────────────────

    [Fact]
    public void NoProxyConfigured_IsDirect()
    {
        Resolve().Should().BeNull();
        _source.Asked.Should().Equal(new Uri("http://mud.example.org:4000/"));
    }

    [Fact]
    public void ProxyEnableZero_IgnoresTheStoredServer()
    {
        Static("proxy.corp:8080", enabled: false);

        Resolve().Should().BeNull();
    }

    [Fact]
    public void ProxyEnabledButEmptyServer_IsDirect()
    {
        Static("   ");

        Resolve().Should().BeNull();
    }

    [Fact]
    public void ASourceThatFails_MeansDirect_NotAnException()
    {
        _source.ThrowOnRead = true;
        _source.WebProxy = _ => throw new InvalidOperationException("PAC engine down");

        Resolve().Should().BeNull();
    }

    [Theory]
    [InlineData("", 4000)]
    [InlineData("mud.example.org", 0)]
    [InlineData("mud.example.org", 70000)]
    public void NonsenseDestination_IsDirect(string host, int port)
    {
        Static("proxy.corp:8080");

        Resolve(host, port).Should().BeNull();
    }

    // ── http ─────────────────────────────────────────────────────────────────

    [Fact]
    public void BareHostAndPort_IsAnHttpConnectProxy()
    {
        Static("proxy.corp:8080");

        Resolve().Should().Be(new SystemProxy("proxy.corp", 8080, ProxyProtocol.HttpConnect));
        _source.Asked.Should().BeEmpty("a static proxy that is on decides by itself");
    }

    [Theory]
    [InlineData("proxy.corp", "proxy.corp", 80)]
    [InlineData("http://proxy.corp:3128", "proxy.corp", 3128)]
    [InlineData("http://proxy.corp:3128/", "proxy.corp", 3128)]
    [InlineData("  proxy.corp:3128  ", "proxy.corp", 3128)]
    [InlineData("[2001:db8::1]:3128", "2001:db8::1", 3128)]
    [InlineData("10.0.0.1:8080", "10.0.0.1", 8080)]
    public void BareEntry_Forms(string server, string host, int port)
    {
        Static(server);

        Resolve().Should().Be(new SystemProxy(host, port, ProxyProtocol.HttpConnect));
    }

    [Theory]
    [InlineData("proxy.corp:0")]
    [InlineData("proxy.corp:99999")]
    [InlineData("proxy.corp:abc")]
    [InlineData(":8080")]
    public void UnusableEntry_IsDirect(string server)
    {
        Static(server);

        Resolve().Should().BeNull();
    }

    // ── per protocol ─────────────────────────────────────────────────────────

    [Fact]
    public void PerProtocolList_WithSocks_PrefersSocks5()
    {
        Static("http=web.corp:8080;https=secure.corp:8443;ftp=ftp.corp:21;socks=socks.corp:1081");

        Resolve().Should().Be(new SystemProxy("socks.corp", 1081, ProxyProtocol.Socks5));
    }

    [Fact]
    public void PerProtocolList_WithoutSocks_PrefersTheHttpsProxy_WhichIsTheOneThatTunnels()
    {
        Static("http=web.corp:8080;https=secure.corp:8443;ftp=ftp.corp:21");

        Resolve().Should().Be(new SystemProxy("secure.corp", 8443, ProxyProtocol.HttpConnect));
    }

    [Fact]
    public void PerProtocolList_OnlyHttp_UsesIt()
    {
        Static("http=web.corp:8080");

        Resolve().Should().Be(new SystemProxy("web.corp", 8080, ProxyProtocol.HttpConnect));
    }

    [Fact]
    public void PerProtocolList_OnlyOtherProtocols_IsDirect()
    {
        Static("ftp=ftp.corp:21");

        Resolve().Should().BeNull();
    }

    [Theory]
    [InlineData("socks=socks.corp", 1080)]
    [InlineData("socks=socks.corp:9050", 9050)]
    [InlineData("SOCKS=socks.corp:9050", 9050)]
    [InlineData("socks=socks5://socks.corp:9050", 9050)]
    [InlineData("http=web.corp:8080 socks=socks.corp:9050", 9050)]
    public void SocksEntry_Forms(string server, int port)
    {
        Static(server);

        Resolve().Should().Be(new SystemProxy("socks.corp", port, ProxyProtocol.Socks5));
    }

    [Fact]
    public void BrokenSocksEntry_FallsBackToTheHttpOne()
    {
        Static("socks=:0;http=web.corp:8080");

        Resolve().Should().Be(new SystemProxy("web.corp", 8080, ProxyProtocol.HttpConnect));
    }

    // ── exclusions ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("mud.example.org")]
    [InlineData("MUD.EXAMPLE.ORG")]
    [InlineData("*.example.org")]
    [InlineData("mud.*")]
    [InlineData("other.net;*.example.org;<local>")]
    [InlineData("other.net *.example.org")]
    [InlineData("http://mud.example.org")]
    [InlineData("mud.example.org:4000")]
    [InlineData("*")]
    public void ExclusionList_MatchingTheMud_IsDirect(string bypass)
    {
        Static("proxy.corp:8080", bypass);

        Resolve().Should().BeNull();
    }

    [Theory]
    [InlineData("example.org")]
    [InlineData("*.example.com")]
    [InlineData("<local>")]
    [InlineData("<-loopback>")]
    [InlineData("")]
    public void ExclusionList_NotMatchingTheMud_KeepsTheProxy(string bypass)
    {
        Static("proxy.corp:8080", bypass);

        Resolve().Should().Be(new SystemProxy("proxy.corp", 8080, ProxyProtocol.HttpConnect));
    }

    [Theory]
    [InlineData("192.168.*", "192.168.1.20", true)]
    [InlineData("192.168.*", "192.169.1.20", false)]
    [InlineData("10.*;172.16.*", "172.16.0.9", true)]
    public void ExclusionList_WorksOnIpLiterals(string bypass, string host, bool direct)
    {
        Static("proxy.corp:8080", bypass);

        (Resolve(host) is null).Should().Be(direct);
    }

    [Theory]
    [InlineData("servidor", true)]
    [InlineData("servidor.", true)]
    [InlineData("servidor.casa", false)]
    [InlineData("192.168.1.20", false)]
    public void Local_MeansNamesWithoutADot(string host, bool direct)
    {
        Static("socks=socks.corp:1080", "<local>");

        (Resolve(host) is null).Should().Be(direct);
    }

    // ── this machine ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData("localhost.")]
    [InlineData("mud.localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("127.8.9.10")]
    [InlineData("::1")]
    [InlineData("[::1]")]
    [InlineData("::ffff:127.0.0.1")]
    public void ThisMachine_NeverGoesThroughAProxy(string host)
    {
        Static("http=web.corp:8080;socks=socks.corp:1080");
        _source.WebProxy = _ => new Uri("http://pac.corp:8080");

        Resolve(host).Should().BeNull();
        _source.Asked.Should().BeEmpty();
    }

    [Theory]
    [InlineData("localhost.example.org")]
    [InlineData("128.0.0.1")]
    [InlineData("notlocalhost")]
    public void ThingsThatOnlyLookLocal_AreNot(string host)
    {
        SystemProxyResolver.IsLoopback(host).Should().BeFalse();
    }

    // ── PAC / auto-detection / environment, through the platform ─────────────

    [Fact]
    public void NoStaticProxy_WhatThePlatformResolves_IsUsedAsHttpConnect()
    {
        _source.WebProxy = _ => new Uri("http://pac.corp:3128/");

        Resolve().Should().Be(new SystemProxy("pac.corp", 3128, ProxyProtocol.HttpConnect));
    }

    [Theory]
    [InlineData("socks5://pac.corp:1080", 1080)]
    [InlineData("socks4://pac.corp:1085", 1085)]
    public void NoStaticProxy_ASocksAnswerFromThePlatform_IsSocks5(string answer, int port)
    {
        _source.WebProxy = _ => new Uri(answer);

        Resolve().Should().Be(new SystemProxy("pac.corp", port, ProxyProtocol.Socks5));
    }

    [Fact]
    public void ThePlatformAnsweringWithTheDestinationItself_MeansDirect()
    {
        _source.WebProxy = destination => destination;

        Resolve().Should().BeNull();
    }

    [Fact]
    public void Ipv6Mud_IsAskedWithBrackets()
    {
        Resolve("2001:db8::7", 23).Should().BeNull();

        _source.Asked.Should().Equal(new Uri("http://[2001:db8::7]:23/"));
    }

    [Fact]
    public void StaticProxyOn_AndExcluded_DoesNotFallBackToThePlatform()
    {
        Static("proxy.corp:8080", "*.example.org");
        _source.WebProxy = _ => new Uri("http://pac.corp:3128/");

        Resolve().Should().BeNull();
        _source.Asked.Should().BeEmpty();
    }

    // ── the real source, read-only ───────────────────────────────────────────

    [Fact]
    public void WindowsSource_OnlyReads_AndNeverThrows()
    {
        var source = new WindowsSystemProxySource();

        // Only the registry read: asking the platform could start a real WPAD discovery on some networks.
        var read = () => source.ReadSettings();

        read.Should().NotThrow().Which.Should().NotBeNull();
        new SystemProxyResolver(source).Resolve("localhost", 4000).Should().BeNull();
    }

    [Fact]
    public void NoSystemProxyResolver_IsAlwaysDirect()
    {
        NoSystemProxyResolver.Instance.Resolve("mud.example.org", 4000).Should().BeNull();
    }
}
