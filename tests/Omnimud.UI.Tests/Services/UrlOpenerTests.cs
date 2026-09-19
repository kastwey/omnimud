using Omnimud.UI.Services;

namespace Omnimud.UI.Tests.Services;

/// <summary>Links come from MUD text, which is untrusted: only a short whitelist may reach the shell.</summary>
public sealed class UrlOpenerTests
{
    [Theory]
    [InlineData("http://www.omnimud.org", "http://www.omnimud.org/")]
    [InlineData("https://ejemplo.org/pagina?x=1", "https://ejemplo.org/pagina?x=1")]
    [InlineData("www.ejemplo.org", "http://www.ejemplo.org/")]
    [InlineData("https://ejemplo.org/pagina.", "https://ejemplo.org/pagina")]
    [InlineData("info@omnimud.org", "mailto:info@omnimud.org")]
    [InlineData("mailto:info@omnimud.org", "mailto:info@omnimud.org")]
    [InlineData("ftp://ftp.ejemplo.org/f.zip", "ftp://ftp.ejemplo.org/f.zip")]
    public void Parse_AcceptsWebMailAndFtpLinks(string word, string expected)
    {
        UrlOpener.Parse(word)!.AbsoluteUri.Should().Be(expected);
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("C:\\Windows\\System32\\calc.exe")]
    [InlineData("\\\\servidor\\recurso\\virus.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-settings:privacy")]
    [InlineData("calc.exe")]
    [InlineData("cmd /c del")]
    [InlineData("espada")]
    [InlineData("")]
    [InlineData(null)]
    public void Parse_RefusesAnythingThatCouldRunOrOpenLocalContent(string? word)
    {
        UrlOpener.Parse(word).Should().BeNull();
    }

    [Fact]
    public void Open_RefusesADisallowedScheme_EvenIfGivenAUriDirectly()
    {
        UrlOpener.Open(new Uri("file:///C:/Windows/System32/calc.exe")).Should().BeFalse();
    }

    [Fact]
    public void Parse_RefusesAbsurdlyLongInput()
    {
        UrlOpener.Parse("http://ejemplo.org/" + new string('a', 3000)).Should().BeNull();
    }
}
