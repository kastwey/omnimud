using FluentAssertions;
using System.Globalization;
using Omnimud.Core.Session;

namespace Omnimud.Core.Tests.Session;

public sealed class MovementKeysTests
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es");
    private static readonly CultureInfo EsEs = CultureInfo.GetCultureInfo("es-ES");
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Codes_AreStable_TheyAreStoredInTheDatabase()
    {
        ((int)MovementKey.NumPad0).Should().Be(0);
        ((int)MovementKey.NumPad9).Should().Be(9);
        ((int)MovementKey.ArrowUp).Should().Be(10);
        ((int)MovementKey.ArrowDown).Should().Be(11);
        ((int)MovementKey.ArrowLeft).Should().Be(12);
        ((int)MovementKey.ArrowRight).Should().Be(13);
        ((int)MovementKey.PageUp).Should().Be(14);
        ((int)MovementKey.PageDown).Should().Be(15);
        ((int)MovementKey.Home).Should().Be(16);
        ((int)MovementKey.End).Should().Be(17);
        Enum.GetValues<MovementKey>().Select(k => (int)k).Should().BeEquivalentTo(Enumerable.Range(0, 18));
    }

    [Fact]
    public void CodeLists_CoverEveryKeyOnce()
    {
        MovementKeys.NavigationCodes.Should().Equal(10, 11, 12, 13, 14, 15, 16, 17);
        MovementKeys.NumPadCodes.Should().Equal(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        MovementKeys.AllCodes.Should().BeEquivalentTo(Enumerable.Range(0, 18)).And.OnlyHaveUniqueItems();
        MovementKeys.Count.Should().Be(18);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(9, true)]
    [InlineData(10, true)]
    [InlineData(17, true)]
    [InlineData(18, false)]
    [InlineData(38, false)]
    public void IsValid_Is0To17(int code, bool expected) => MovementKeys.IsValid(code).Should().Be(expected);

    [Fact]
    public void Defaults_InSpanish()
    {
        MovementKeys.DefaultCommands(Es).Should().BeEquivalentTo(new Dictionary<int, string>
        {
            [10] = "norte", [11] = "sur", [12] = "oeste", [13] = "este", [14] = "arriba", [15] = "abajo",
            [8] = "norte", [2] = "sur", [4] = "oeste", [6] = "este",
            [7] = "noroeste", [9] = "noreste", [1] = "sudoeste", [3] = "sudeste",
        });
        MovementKeys.DefaultCommands(EsEs).Should().BeSameAs(MovementKeys.DefaultCommands(Es));
    }

    [Fact]
    public void Defaults_InEnglish_AndInAnyOtherLanguage()
    {
        var expected = new Dictionary<int, string>
        {
            [10] = "north", [11] = "south", [12] = "west", [13] = "east", [14] = "up", [15] = "down",
            [8] = "north", [2] = "south", [4] = "west", [6] = "east",
            [7] = "northwest", [9] = "northeast", [1] = "southwest", [3] = "southeast",
        };
        MovementKeys.DefaultCommands(En).Should().BeEquivalentTo(expected);
        MovementKeys.DefaultCommands(CultureInfo.GetCultureInfo("fr")).Should().BeEquivalentTo(expected);
        MovementKeys.DefaultCommands(CultureInfo.InvariantCulture).Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Defaults_WithoutCulture_FollowTheCurrentUiCulture()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = Es;
            MovementKeys.DefaultCommands()[10].Should().Be("norte");
            CultureInfo.CurrentUICulture = En;
            MovementKeys.DefaultCommands()[10].Should().Be("north");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Theory]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(5)]
    [InlineData(0)]
    public void HomeEndFiveAndZero_HaveNoDefault(int code)
    {
        MovementKeys.HasDefault(code).Should().BeFalse();
        MovementKeys.DefaultCommands(Es).Should().NotContainKey(code);
        MovementKeys.DefaultCommands(En).Should().NotContainKey(code);
    }

    [Fact]
    public void HasDefault_MatchesTheTables_InBothLanguages()
    {
        foreach (var code in MovementKeys.AllCodes)
        {
            MovementKeys.DefaultCommands(Es).ContainsKey(code).Should().Be(MovementKeys.HasDefault(code));
            MovementKeys.DefaultCommands(En).ContainsKey(code).Should().Be(MovementKeys.HasDefault(code));
        }
    }

    [Fact]
    public void Effective_IsConfiguredElseDefault()
    {
        var configured = new Dictionary<int, string> { [8] = "n", [16] = "entrar" };

        var effective = MovementKeys.Effective(configured, MovementKeys.DefaultCommands(Es));

        effective[8].Should().Be("n", "configured wins over the default");
        effective[16].Should().Be("entrar", "a key without default can be configured");
        effective[10].Should().Be("norte", "not configured: default");
        effective[2].Should().Be("sur");
        effective.Should().NotContainKey(17).And.NotContainKey(5).And.NotContainKey(0);
        effective.Should().HaveCount(15);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Effective_AnEmptyConfiguredCommand_SwitchesTheKeyOff_EvenWithDefault(string stored)
    {
        var effective = MovementKeys.Effective(new Dictionary<int, string> { [10] = stored }, MovementKeys.DefaultCommands(Es));

        effective.Should().NotContainKey(10);
        effective[11].Should().Be("sur");
    }

    [Fact]
    public void Effective_IgnoresCodesOutOfRange()
    {
        var effective = MovementKeys.Effective(new Dictionary<int, string> { [18] = "x", [-1] = "y" }, new Dictionary<int, string>());

        effective.Should().BeEmpty();
    }

    [Fact]
    public void DisplayNames_AreLocalized_AndUnique()
    {
        MovementKeys.DisplayName(10, Es).Should().Be("Flecha arriba");
        MovementKeys.DisplayName(14, Es).Should().Be("Re Pág");
        MovementKeys.DisplayName(15, Es).Should().Be("Av Pág");
        MovementKeys.DisplayName(16, Es).Should().Be("Inicio");
        MovementKeys.DisplayName(8, Es).Should().Be("Teclado numérico 8");
        MovementKeys.DisplayName(10, En).Should().Be("Up arrow");
        MovementKeys.DisplayName(14, En).Should().Be("Page Up");
        MovementKeys.DisplayName(8, En).Should().Be("Numpad 8");

        MovementKeys.AllCodes.Select(c => MovementKeys.DisplayName(c, Es)).Should().OnlyHaveUniqueItems();
        MovementKeys.AllCodes.Select(c => MovementKeys.DisplayName(c, En)).Should().OnlyHaveUniqueItems();
        FluentActions.Invoking(() => MovementKeys.DisplayName(18, Es)).Should().Throw<ArgumentOutOfRangeException>();
    }
}
