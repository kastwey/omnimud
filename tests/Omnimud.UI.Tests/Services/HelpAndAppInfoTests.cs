using System.Globalization;
using System.Xml.Linq;
using Omnimud.Core.Updates;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;
using Omnimud.UI.Tests.Presenters;

namespace Omnimud.UI.Tests.Services;

public sealed class HelpAndAppInfoTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "omnimud-help-" + Guid.NewGuid().ToString("N"));
    private readonly ScriptedPrompts _prompts = new();
    private readonly List<string> _opened = [];

    public HelpAndAppInfoTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "docs"));
        Strings.Culture = CultureInfo.GetCultureInfo("es");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    private void Install(params string[] files)
    {
        foreach (var file in files) File.WriteAllText(Path.Combine(_dir, "docs", file), "x");
    }

    private HelpService Create(bool opens = true) => new(_dir, _prompts, path => { _opened.Add(path); return opens; });

    // ── Manual ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("es", "manual.es.html")]
    [InlineData("es-ES", "manual.es.html")]
    [InlineData("es-MX", "manual.es.html")]
    [InlineData("en", "manual.en.html")]
    [InlineData("en-GB", "manual.en.html")]
    [InlineData("fr-FR", "manual.en.html")]
    public void F1_OpensTheManualOfTheInterfaceLanguage(string culture, string expected)
    {
        Install("manual.es.html", "manual.en.html");
        Strings.Culture = CultureInfo.GetCultureInfo(culture);

        Create().OpenManual();

        _opened.Should().ContainSingle().Which.Should().Be(Path.Combine(_dir, "docs", expected));
        _prompts.Infos.Should().BeEmpty();
    }

    [Theory]
    [InlineData("es", "manual.en.html")]
    [InlineData("en", "manual.es.html")]
    public void WhenTheManualOfTheLanguageIsMissing_TheOtherOneIsOpened(string culture, string installed)
    {
        Install(installed);
        Strings.Culture = CultureInfo.GetCultureInfo(culture);

        Create().OpenManual();

        _opened.Should().ContainSingle().Which.Should().EndWith(installed);
    }

    [Fact]
    public void WithoutAnyManual_TheUserIsTold_AndNothingIsOpened()
    {
        Create().OpenManual();

        _opened.Should().BeEmpty();
        _prompts.Infos.Should().ContainSingle().Which.Should().Contain("manual.");
    }

    [Fact]
    public void WhenTheFileCannotBeOpened_TheUserIsToldWhereItIs()
    {
        Install("manual.es.html");

        Create(opens: false).OpenManual();

        _prompts.Warnings.Should().ContainSingle().Which.Should().Contain("manual.es.html");
    }

    [Fact]
    public void LuaReference_IsOpenedFromTheSameFolder()
    {
        Install("API_LUA.md");

        Create().OpenLuaReference();

        _opened.Should().ContainSingle().Which.Should().Be(Path.Combine(_dir, "docs", "API_LUA.md"));
    }

    [Fact]
    public void LuaReference_Missing_IsSaid()
    {
        Create().OpenLuaReference();

        _opened.Should().BeEmpty();
        _prompts.Infos.Should().ContainSingle().Which.Should().Contain("API_LUA.md");
    }

    /// <summary>The build copies the help next to the executable; this is the folder the tests run from.</summary>
    [Fact]
    public void TheBuild_ShipsBothManualsAndTheLuaReference_NextToTheExecutable()
    {
        foreach (var file in new[] { "manual.es.html", "manual.en.html", "API_LUA.md" })
            File.Exists(Path.Combine(AppContext.BaseDirectory, "docs", file)).Should().BeTrue($"docs\\{file} must be copied to the output");

        HelpService.ResolveManualPath(AppContext.BaseDirectory, CultureInfo.GetCultureInfo("es")).Should().EndWith("manual.es.html");
        HelpService.ResolveManualPath(AppContext.BaseDirectory, CultureInfo.GetCultureInfo("en")).Should().EndWith("manual.en.html");
    }

    // ── Version ────────────────────────────────────────────────────────────

    [Fact]
    public void TheVersion_ComesFromDirectoryBuildProps()
    {
        var props = XDocument.Load(Path.Combine(RepositoryRoot(), "Directory.Build.props"));
        var declared = props.Descendants("Version").Single().Value.Trim();

        AppInfo.Version.Should().Be(declared);
        AppInfo.SemanticVersion.Should().Be(SemanticVersion.Parse(declared)!);
    }

    [Theory]
    [InlineData("2.0.0+5f3e1c2ab", "2.0.0")]
    [InlineData("2.1.0-beta.1+5f3e1c2ab", "2.1.0-beta.1")]
    [InlineData("2.1.0", "2.1.0")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("not a version", null)]
    public void TheCommitHashTheSdkAppends_IsNotPartOfTheVersion(string? informational, string? expected) =>
        AppInfo.CleanVersion(informational).Should().Be(expected);

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void About_ShowsTheRealVersion(string culture)
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);
        string.Format(Strings.About_Text, AppInfo.Version).Should().Contain(AppInfo.Version).And.Contain("Omnimud");
    }

    // ── What the shell is allowed to open ──────────────────────────────────

    [Theory]
    [InlineData("http://github.com/kastwey/omnimud")]
    [InlineData("ftp://example.org/x")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ms-settings:privacy")]
    public void TheExternalLauncher_RefusesEverythingButHttpsAndMailto(string address) =>
        new ShellExternalLauncher().Open(new Uri(address)).Should().BeFalse();

    [Fact]
    public void TheExternalLauncher_RefusesRelativeAddresses() =>
        new ShellExternalLauncher().Open(new Uri("/x", UriKind.Relative)).Should().BeFalse();

    internal static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Omnimud.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Omnimud.sln was not found above " + AppContext.BaseDirectory);
    }
}
