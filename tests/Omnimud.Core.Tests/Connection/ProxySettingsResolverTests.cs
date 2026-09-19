using System.Globalization;
using System.Security.Cryptography;
using FluentAssertions;
using NSubstitute;
using Omnimud.Core.Connection;
using Omnimud.Core.Options;
using Omnimud.Core.Security;
using Omnimud.Core.Sound;

namespace Omnimud.Core.Tests.Connection;

public sealed class ProxySettingsResolverTests
{
    private const string Password = "s3cret";

    private readonly ProxyCredentialStore _credentials = new(new AesPasswordProtector(RandomNumberGenerator.GetBytes(32)));
    private readonly ISystemProxyResolver _system = Substitute.For<ISystemProxyResolver>();
    private readonly ProxySettingsResolver _sut;

    public ProxySettingsResolverTests() => _sut = new ProxySettingsResolver(_credentials, _system);

    private OmnimudOptions Manual(ProxyProtocol protocol = ProxyProtocol.Socks5, bool forMud = true) =>
        _credentials.WithPassword(OmnimudOptions.Default with
        {
            ProxyType = ProxyMode.Manual, ProxyHost = " proxy.corp ", ProxyPort = 1080, ProxyProtocol = protocol,
            ProxyUsername = " juan ", UseProxyForMud = forMud
        }, Password);

    // ── the MUD ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ProxyProtocol.Socks5)]
    [InlineData(ProxyProtocol.HttpConnect)]
    public void Mud_Manual_UsesHostPortProtocolAndCredentials(ProxyProtocol protocol)
    {
        _sut.ForMud(Manual(protocol), "mud.example.org", 4000)
            .Should().Be(new ProxyConfig("proxy.corp", 1080, protocol, "juan", Password));
        _system.DidNotReceiveWithAnyArgs().Resolve(default!, default);
    }

    [Theory]
    [InlineData(ProxyMode.Disabled)]
    [InlineData(ProxyMode.Automatic)]
    [InlineData(ProxyMode.Manual)]
    public void Mud_WithUseProxyForMudOff_IsAlwaysDirect(ProxyMode mode)
    {
        _system.Resolve(Arg.Any<string>(), Arg.Any<int>()).Returns(new SystemProxy("sys.corp", 8080, ProxyProtocol.HttpConnect));

        _sut.ForMud(Manual(forMud: false) with { ProxyType = mode }, "mud.example.org", 4000).Should().BeNull();
        _system.DidNotReceiveWithAnyArgs().Resolve(default!, default);
    }

    [Fact]
    public void Mud_Disabled_IsDirect_EvenWithUseProxyForMudOn()
    {
        _sut.ForMud(Manual() with { ProxyType = ProxyMode.Disabled }, "mud.example.org", 4000).Should().BeNull();
    }

    [Theory]
    [InlineData(null, 1080)]
    [InlineData("  ", 1080)]
    [InlineData("proxy.corp", 0)]
    [InlineData("proxy.corp", 70000)]
    public void Mud_Manual_WithoutAUsableProxy_IsDirect(string? host, int port)
    {
        _sut.ForMud(Manual() with { ProxyHost = host, ProxyPort = port }, "mud.example.org", 4000).Should().BeNull();
    }

    [Fact]
    public void Mud_Manual_AlsoProxiesAMudOnThisMachine_BecauseTheUserSaidSo()
    {
        _sut.ForMud(Manual(), "127.0.0.1", 4000).Should().NotBeNull();
    }

    [Fact]
    public void Mud_Automatic_UsesWhatTheSystemResolvesForThatDestination_WithTheCredentials()
    {
        _system.Resolve("mud.example.org", 4000).Returns(new SystemProxy("sys.corp", 8080, ProxyProtocol.HttpConnect));
        var options = Manual() with { ProxyType = ProxyMode.Automatic };

        _sut.ForMud(options, "mud.example.org", 4000)
            .Should().Be(new ProxyConfig("sys.corp", 8080, ProxyProtocol.HttpConnect, "juan", Password),
                "the manual host, port and protocol are ignored in automatic mode");
    }

    [Fact]
    public void Mud_Automatic_SystemSaysDirect_IsDirect()
    {
        _system.Resolve(Arg.Any<string>(), Arg.Any<int>()).Returns((SystemProxy?)null);

        _sut.ForMud(Manual() with { ProxyType = ProxyMode.Automatic }, "mud.example.org", 4000).Should().BeNull();
    }

    // ── credentials ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithoutUserName_NoCredentialsAreSent_EvenIfAPasswordIsStored(string? user)
    {
        var options = Manual() with { ProxyUsername = user };

        _sut.ForMud(options, "mud.example.org", 4000).Should().Be(new ProxyConfig("proxy.corp", 1080));
        _sut.ForDownloads(options).Should().Be(new DownloadProxySettings(DownloadProxyKind.Manual, new Uri("socks5://proxy.corp:1080")));
    }

    [Fact]
    public void UserNameWithoutStoredPassword_SendsTheUserAlone()
    {
        var options = Manual() with { ProxyPasswordProtected = null };

        _sut.ForMud(options, "mud.example.org", 4000)!.Username.Should().Be("juan");
        _sut.ForMud(options, "mud.example.org", 4000)!.Password.Should().BeNull();
    }

    [Fact]
    public void PasswordThatCannotBeDecrypted_CountsAsNoPassword()
    {
        var elsewhere = new ProxySettingsResolver(new ProxyCredentialStore(new AesPasswordProtector(RandomNumberGenerator.GetBytes(32))), _system);

        var proxy = elsewhere.ForMud(Manual(), "mud.example.org", 4000);

        proxy.Should().Be(new ProxyConfig("proxy.corp", 1080, ProxyProtocol.Socks5, "juan"));
    }

    // ── downloads ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ProxyProtocol.HttpConnect, "http://proxy.corp:1080/")]
    [InlineData(ProxyProtocol.Socks5, "socks5://proxy.corp:1080")]
    public void Downloads_Manual_FollowTheProtocol_WithCredentials_WhateverUseProxyForMudSays(ProxyProtocol protocol, string address)
    {
        foreach (var forMud in new[] { true, false })
        {
            _sut.ForDownloads(Manual(protocol, forMud))
                .Should().Be(new DownloadProxySettings(DownloadProxyKind.Manual, new Uri(address), "juan", Password));
        }
    }

    [Fact]
    public void Downloads_Manual_Ipv6Proxy_GetsBrackets()
    {
        _sut.ForDownloads(Manual(ProxyProtocol.HttpConnect) with { ProxyHost = "2001:db8::1" }).Address
            .Should().Be(new Uri("http://[2001:db8::1]:1080/"));
    }

    [Fact]
    public void Downloads_Automatic_UseTheSystemProxy_WithTheCredentials()
    {
        _sut.ForDownloads(Manual() with { ProxyType = ProxyMode.Automatic })
            .Should().Be(new DownloadProxySettings(DownloadProxyKind.System, null, "juan", Password));
    }

    [Fact]
    public void Downloads_Disabled_OrManualWithoutHost_AreDirect()
    {
        _sut.ForDownloads(Manual() with { ProxyType = ProxyMode.Disabled }).Should().Be(DownloadProxySettings.Direct);
        _sut.ForDownloads(Manual() with { ProxyHost = null }).Should().Be(DownloadProxySettings.Direct);
        _sut.ForDownloads(OmnimudOptions.Default).Should().Be(DownloadProxySettings.Direct);
    }

    [Fact]
    public void Basic_HasNoSystemProxyAndNoPasswords()
    {
        var basic = ProxySettingsResolver.Basic;

        basic.ForMud(Manual() with { ProxyType = ProxyMode.Automatic }, "mud.example.org", 4000).Should().BeNull();
        basic.ForMud(Manual(), "mud.example.org", 4000).Should().Be(new ProxyConfig("proxy.corp", 1080, ProxyProtocol.Socks5, "juan"));
    }

    // ── what the user is told ────────────────────────────────────────────────

    [Theory]
    [InlineData("es", ProxyErrorKind.ConnectionFailed, "No se pudo conectar al servidor proxy (SOCKS5)")]
    [InlineData("es", ProxyErrorKind.AuthenticationFailed, "El proxy (SOCKS5) pide usuario y contraseña, o ha rechazado los indicados")]
    [InlineData("es", ProxyErrorKind.Rejected, "El proxy (SOCKS5) se ha negado a conectar con el MUD")]
    [InlineData("es", ProxyErrorKind.ProtocolError, "El proxy ha dado una respuesta inesperada")]
    [InlineData("en", ProxyErrorKind.ConnectionFailed, "Could not connect to the proxy server (SOCKS5)")]
    [InlineData("en", ProxyErrorKind.AuthenticationFailed, "The proxy (SOCKS5) asks for a user name and password, or rejected the ones given")]
    [InlineData("en", ProxyErrorKind.Rejected, "The proxy (SOCKS5) refused to connect to the MUD")]
    [InlineData("en", ProxyErrorKind.ProtocolError, "The proxy gave an unexpected answer")]
    public void ProxyErrors_AreExplainedInTheLanguageOfTheUser_ByKind(string culture, ProxyErrorKind kind, string expectedStart)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        var error = new ProxyException(ProxyProtocol.Socks5, kind, "internal English detail");

        var text = ConnectionErrorDescriber.Describe(error);

        text.Should().StartWith(expectedStart).And.NotContain("internal English detail");
    }

    [Fact]
    public void ProxyErrors_Rejected_CarryTheCode_AndNameTheProtocol()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("es");

        ConnectionErrorDescriber.Describe(new ProxyException(ProxyProtocol.HttpConnect, ProxyErrorKind.Rejected, "x", 403))
            .Should().Be("El proxy (HTTP CONNECT) se ha negado a conectar con el MUD o no ha podido llegar a él (código 403).");
    }

    [Fact]
    public void ProxyErrors_AreFoundInsideOtherExceptions_AndOtherErrorsKeepTheirMessage()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("es");
        var wrapped = new InvalidOperationException("outer", new ProxyException(ProxyProtocol.Socks5, ProxyErrorKind.ProtocolError, "x"));

        ConnectionErrorDescriber.Describe(wrapped).Should().StartWith("El proxy ha dado una respuesta inesperada");
        ConnectionErrorDescriber.Describe(new TimeoutException("se acabó el tiempo")).Should().Be("se acabó el tiempo");
    }
}
