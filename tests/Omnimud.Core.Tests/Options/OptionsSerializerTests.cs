using FluentAssertions;
using System.Globalization;
using System.Reflection;
using Omnimud.Core.Options;

namespace Omnimud.Core.Tests.Options;

public sealed class OptionsSerializerTests
{
    /// <summary>Every property with a value different from its default, so a property that is not round-tripped is noticed.</summary>
    private static readonly OmnimudOptions NonDefault = new()
    {
        ScreenReader = ScreenReaderMode.Nvda,
        CursorOnReceived = CursorBehavior.Keep,
        CursorOnMessages = CursorBehavior.GoToEnd,
        HistorySize = 123,
        ConfirmBeforeExit = false,
        TrySaveBeforeExit = false,
        TelnetNegotiation = false,
        Language = "es",
        AnnounceMudText = false,
        AnnounceMessages = false,
        FlashWindow = false,
        MaxLines = 2_500,
        PromptFlushMilliseconds = 275,
        LogType = LogMode.PerSession,
        LogDirectory = @"D:\Mis registros\año 2026",
        EnableSounds = false,
        EnableMusic = false,
        PlaySoundsInBackground = false,
        PlayMusicInBackground = false,
        DownloadSounds = false,
        AllowHttpDownloads = true,
        Volume = 37,
        ProxyType = ProxyMode.Manual,
        ProxyHost = "proxy.example.org",
        ProxyPort = 1080,
        UseProxyForMud = true,
        ProxyProtocol = ProxyProtocol.HttpConnect,
        ProxyUsername = "juan",
        ProxyPasswordProtected = "c2FsdCtub25jZSt0YWcrY2lmcmFkbw==",
        FontFamily = "Courier New",
        FontSize = 8.25f,
        UseConcatChar = true,
        ConcatChar = '|',
        UseRepeatChar = true,
        RepeatChar = '*'
    };

    [Fact]
    public void NonDefaultFixture_ReallyChangesEveryProperty()
    {
        var defaults = new OmnimudOptions();
        foreach (var property in InstanceProperties())
            property.GetValue(NonDefault).Should().NotBe(property.GetValue(defaults), $"{property.Name} must differ from its default for the round-trip tests to mean anything");
    }

    [Fact]
    public void Keys_AreThePropertyNames()
    {
        OptionsSerializer.Keys.Should().BeEquivalentTo(InstanceProperties().Select(p => p.Name));
        OptionsSerializer.Keys.Should().HaveCountGreaterThanOrEqualTo(33);
    }

    [Fact]
    public void SerializeForExport_LeavesTheSecretsOut_AndNothingElse()
    {
        var exported = OptionsSerializer.SerializeForExport(NonDefault);

        exported.Keys.Should().BeEquivalentTo(OptionsSerializer.Keys.Except([nameof(OmnimudOptions.ProxyPasswordProtected)]));
        exported.Values.Should().NotContain(NonDefault.ProxyPasswordProtected!);
        exported[nameof(OmnimudOptions.ProxyUsername)].Should().Be("juan", "the user name is not a secret");
        OptionsSerializer.Deserialize(exported).Should().Be(NonDefault with { ProxyPasswordProtected = null });
    }

    [Fact]
    public void SecretKeys_AreRealOptions_AndMatchWhateverTheCase()
    {
        OptionsSerializer.Keys.Should().Contain(OptionsSerializer.SecretKeys);
        OptionsSerializer.WithoutSecrets(new Dictionary<string, string> { ["proxypasswordprotected"] = "x", ["Volume"] = "5" })
            .Should().BeEquivalentTo(new Dictionary<string, string> { ["Volume"] = "5" });
    }

    [Fact]
    public void RoundTrip_AllProperties()
    {
        var values = OptionsSerializer.Serialize(NonDefault);

        values.Keys.Should().BeEquivalentTo(OptionsSerializer.Keys);
        OptionsSerializer.Deserialize(values).Should().Be(NonDefault);
    }

    [Fact]
    public void RoundTrip_Defaults_IncludingNullStrings()
    {
        var values = OptionsSerializer.Serialize(new OmnimudOptions());

        values[nameof(OmnimudOptions.LogDirectory)].Should().BeEmpty();
        values[nameof(OmnimudOptions.ProxyHost)].Should().BeEmpty();
        var options = OptionsSerializer.Deserialize(values);
        options.Should().Be(new OmnimudOptions());
        options.LogDirectory.Should().BeNull();
        options.Language.Should().BeEmpty("Language is a non-null string whose default is empty");
    }

    [Fact]
    public void Serialize_UsesStableInvariantForms()
    {
        var values = OptionsSerializer.Serialize(NonDefault);

        values[nameof(OmnimudOptions.FontSize)].Should().Be("8.25");
        values[nameof(OmnimudOptions.ConfirmBeforeExit)].Should().Be("false");
        values[nameof(OmnimudOptions.AllowHttpDownloads)].Should().Be("true");
        values[nameof(OmnimudOptions.ScreenReader)].Should().Be("Nvda");
        values[nameof(OmnimudOptions.ProxyProtocol)].Should().Be("HttpConnect");
        values[nameof(OmnimudOptions.ConcatChar)].Should().Be("|");
        values[nameof(OmnimudOptions.MaxLines)].Should().Be("2500");
    }

    [Theory]
    [InlineData("es-ES")]
    [InlineData("de-DE")]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    public void RoundTrip_DoesNotDependOnTheCurrentCulture(string cultureName)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            var options = NonDefault with { FontSize = 12.5f, MaxLines = 123_456 };

            var values = OptionsSerializer.Serialize(options);

            values[nameof(OmnimudOptions.FontSize)].Should().Be("12.5", "a decimal comma would not be read back by another culture");
            values[nameof(OmnimudOptions.MaxLines)].Should().Be("123456");
            OptionsSerializer.Deserialize(values).Should().Be(options);
            OptionsSerializer.Deserialize(new Dictionary<string, string> { ["ScreenReader"] = "NVDA", ["LogType"] = "perday" })
                .Should().Be(new OmnimudOptions { ScreenReader = ScreenReaderMode.Nvda }, "enum names are matched without culture rules (Turkish i)");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Fact]
    public void Deserialize_DecimalComma_WrittenByACultureSensitiveWriter_IsAccepted()
    {
        OptionsSerializer.Deserialize(new Dictionary<string, string> { ["FontSize"] = "8,25" }).FontSize.Should().Be(8.25f);
    }

    [Fact]
    public void Deserialize_NullOrEmpty_GivesDefaults()
    {
        OptionsSerializer.Deserialize(null).Should().Be(new OmnimudOptions());
        OptionsSerializer.Deserialize(new Dictionary<string, string>()).Should().Be(new OmnimudOptions());
    }

    [Fact]
    public void Deserialize_UnknownKeys_AreIgnored_AndKeysAreCaseInsensitive()
    {
        var options = OptionsSerializer.Deserialize(new Dictionary<string, string>
        {
            ["NoExiste"] = "x",
            ["Default"] = "x",
            ["EqualityContract"] = "x",
            ["volume"] = "12"
        });

        options.Should().Be(new OmnimudOptions { Volume = 12 });
    }

    [Theory]
    [InlineData("Volume", "alto")]
    [InlineData("Volume", "")]
    [InlineData("Volume", "101")]
    [InlineData("Volume", "-1")]
    [InlineData("Volume", "12.5")]
    [InlineData("Volume", "99999999999999999999")]
    [InlineData("HistorySize", "0")]
    [InlineData("MaxLines", "5")]
    [InlineData("ProxyPort", "70000")]
    [InlineData("PromptFlushMilliseconds", "-5")]
    [InlineData("FontSize", "grande")]
    [InlineData("FontSize", "NaN")]
    [InlineData("FontSize", "0")]
    [InlineData("FontSize", "1e30")]
    [InlineData("ConfirmBeforeExit", "quizá")]
    [InlineData("ConfirmBeforeExit", "")]
    [InlineData("ScreenReader", "WindowEyes")]
    [InlineData("ScreenReader", "17")]
    [InlineData("ScreenReader", "")]
    [InlineData("LogType", "Jaws")]
    [InlineData("ConcatChar", "")]
    [InlineData("ConcatChar", ";;")]
    [InlineData("RepeatChar", "\n")]
    public void Deserialize_InvalidValue_FallsBackToTheDefaultOfThatOption_Only(string key, string value)
    {
        var options = OptionsSerializer.Deserialize(new Dictionary<string, string> { [key] = value, ["Language"] = "es" });

        options.Should().Be(new OmnimudOptions { Language = "es" });
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("True", true)]
    [InlineData(" false ", false)]
    public void Deserialize_Booleans_AreTolerant(string text, bool expected)
    {
        OptionsSerializer.Deserialize(new Dictionary<string, string> { ["AllowHttpDownloads"] = text, ["EnableMusic"] = text })
            .Should().Be(new OmnimudOptions { AllowHttpDownloads = expected, EnableMusic = expected });
    }

    [Fact]
    public void Deserialize_EnumByNumber_IsAcceptedOnlyWhenDefined()
    {
        OptionsSerializer.Deserialize(new Dictionary<string, string> { ["LogType"] = "2" }).LogType.Should().Be(LogMode.PerSession);
    }

    [Fact]
    public void Deserialize_LegacyKeys_AreRead_ButTheCurrentKeyWins()
    {
        OptionsSerializer.LegacyKeys.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["SoundEnabled"] = "EnableSounds",
            ["FlashOnMessage"] = "FlashWindow"
        });

        OptionsSerializer.Deserialize(new Dictionary<string, string> { ["SoundEnabled"] = "false", ["FlashOnMessage"] = "false" })
            .Should().Be(new OmnimudOptions { EnableSounds = false, FlashWindow = false });

        OptionsSerializer.Deserialize(new Dictionary<string, string> { ["SoundEnabled"] = "false", ["EnableSounds"] = "true" })
            .EnableSounds.Should().BeTrue();
        OptionsSerializer.Deserialize(new Dictionary<string, string> { ["EnableSounds"] = "true", ["SoundEnabled"] = "false" })
            .EnableSounds.Should().BeTrue("whatever the order of the rows");
    }

    [Fact]
    public void Deserialize_DoesNotTouchTheSharedDefaultInstance()
    {
        OptionsSerializer.Deserialize(new Dictionary<string, string> { ["Volume"] = "1" });

        OmnimudOptions.Default.Volume.Should().Be(100);
    }

    private static IEnumerable<PropertyInfo> InstanceProperties() =>
        typeof(OmnimudOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance);
}
