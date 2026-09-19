using System.Globalization;
using NSubstitute;
using Omnimud.Core.Options;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;

namespace Omnimud.UI.Tests.Services;

public sealed class LanguageServiceTests : IDisposable
{
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;
    private readonly CultureInfo? _defaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
    private readonly CultureInfo? _stringsCulture = Strings.Culture;
    private readonly CultureInfo? _coreCulture = Omnimud.Core.Localization.Culture;

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _uiCulture;
        CultureInfo.DefaultThreadCurrentUICulture = _defaultUiCulture;
        Strings.Culture = _stringsCulture;
        Omnimud.Core.Localization.SetCulture(_coreCulture);
    }

    private sealed record FakeSystem(CultureInfo UiCulture, IReadOnlyList<CultureInfo> InstalledLanguages) : ISystemLanguages;

    private static LanguageService Service(string windows, params string[] installed) =>
        new(new FakeSystem(CultureInfo.GetCultureInfo(windows), installed.Select(CultureInfo.GetCultureInfo).ToArray()));

    [Theory]
    [InlineData("es", "es")]
    [InlineData("ES", "es")]
    [InlineData(" es ", "es")]
    [InlineData("es-MX", "es")]
    [InlineData("en", "en")]
    [InlineData("en-GB", "en")]
    [InlineData("en_US", "en")]
    public void ExplicitLanguage_Wins_WhateverWindowsSays(string option, string expected)
    {
        Service("fr-FR", "fr-FR").Resolve(option).Name.Should().Be(expected);
        Service("es-ES", "es-ES").Resolve(option).Name.Should().Be(expected);
    }

    [Theory]
    [InlineData("es-ES")]
    [InlineData("es-AR")]
    [InlineData("es")]
    public void Automatic_SpanishWindows_IsSpanish(string windows) =>
        Service(windows, "en-US").Resolve("").Name.Should().Be("es");

    [Fact]
    public void Automatic_EnglishWindows_WithSpanishInstalled_IsSpanish() =>
        Service("en-US", "en-US", "es-ES").Resolve("").Name.Should().Be("es");

    [Fact]
    public void Automatic_EnglishWindows_WithLatinAmericanSpanishInstalled_IsSpanish() =>
        Service("en-GB", "es-MX").Resolve(null).Name.Should().Be("es");

    [Fact]
    public void Automatic_WithoutAnySpanish_IsEnglish()
    {
        Service("en-US", "en-US").Resolve("").Name.Should().Be("en");
        Service("de-DE", "de-DE", "fr-FR").Resolve("").Name.Should().Be("en");
        Service("en-US").Resolve("   ").Name.Should().Be("en");
    }

    [Fact]
    public void UnknownLanguage_IsTreatedAsAutomatic()
    {
        Service("en-US", "es-ES").Resolve("fr").Name.Should().Be("es");
        Service("en-US").Resolve("klingon").Name.Should().Be("en");
        Service("en-US").Resolve("eso").Name.Should().Be("en", "the word eso is not the language es");
    }

    [Fact]
    public void Apply_SetsTheThreadCultures_AndBothStringResources()
    {
        var culture = Service("en-US").Apply("es");

        culture.Name.Should().Be("es");
        CultureInfo.CurrentUICulture.Name.Should().Be("es");
        CultureInfo.DefaultThreadCurrentUICulture!.Name.Should().Be("es");
        Strings.Culture!.Name.Should().Be("es");
        Omnimud.Core.Localization.Culture!.Name.Should().Be("es");
        Strings.Common_CancelButton.Should().Be("Cancelar");

        Service("es-ES").Apply("en");

        CultureInfo.CurrentUICulture.Name.Should().Be("en");
        Strings.Common_CancelButton.Should().Be("Cancel");
        Omnimud.Core.Localization.Culture!.Name.Should().Be("en");
    }

    [Fact]
    public void Apply_ReachesThreadsCreatedAfterwards()
    {
        Service("en-US").Apply("es");

        string? seen = null;
        var thread = new Thread(() => seen = CultureInfo.CurrentUICulture.Name);
        thread.Start();
        thread.Join();

        seen.Should().Be("es");
    }

    [Fact]
    public void ApplyFromOptions_ReadsTheGlobalLanguage()
    {
        var options = Substitute.For<IOptionsService>();
        options.ResolveAsync(null, null, Arg.Any<CancellationToken>()).Returns(OmnimudOptions.Default with { Language = "es" });

        Service("en-US").ApplyFromOptions(options).Name.Should().Be("es");

        Strings.Culture!.Name.Should().Be("es");
        CultureInfo.CurrentUICulture.Name.Should().Be("es", "the culture of the calling thread is set, not the one of a pool thread");
    }

    [Fact]
    public void ApplyFromOptions_WhenTheOptionsCannotBeRead_UsesTheAutomaticLanguage()
    {
        var options = Substitute.For<IOptionsService>();
        options.ResolveAsync(null, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<OmnimudOptions>(new InvalidOperationException("sin base de datos")));

        Service("en-US", "es-ES").ApplyFromOptions(options).Name.Should().Be("es");
        Service("en-US").ApplyFromOptions(options).Name.Should().Be("en");
    }

    [Fact]
    public void WindowsSystemLanguages_ReadsTheRealSystemWithoutFailing()
    {
        var system = new WindowsSystemLanguages();

        system.UiCulture.Should().NotBeNull();
        system.InstalledLanguages.Should().NotBeNull();
        new LanguageService(system).Resolve("").Name.Should().BeOneOf("es", "en");
    }
}
