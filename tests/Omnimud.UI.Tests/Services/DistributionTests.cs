using System.Xml.Linq;

namespace Omnimud.UI.Tests.Services;

/// <summary>
/// The promises of the distribution files, checked from their text so nobody breaks them by accident:
/// the release script never packages user data, the workflows ask for the minimum and use no secrets of our own,
/// and the installer needs no administrator and asks before deleting the user's data.
/// </summary>
public sealed class DistributionTests
{
    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine([HelpAndAppInfoTests.RepositoryRoot(), .. parts]));

    [Fact]
    public void ProjectFile_ShipsTheManualsNextToTheExecutable_OutsideTheSingleFile()
    {
        var project = XDocument.Parse(Read("src", "Omnimud.UI", "Omnimud.UI.csproj"));
        var manual = project.Descendants("Content").Single(c => ((string?)c.Attribute("Include"))?.Contains(@"docs\manual\manual.*.html") == true);

        manual.Element("Link")!.Value.Should().StartWith(@"docs\");
        manual.Element("CopyToOutputDirectory")!.Value.Should().Be("PreserveNewest");
        manual.Element("CopyToPublishDirectory")!.Value.Should().Be("PreserveNewest");
        manual.Element("ExcludeFromSingleFile")!.Value.Should().Be("true");
    }

    [Fact]
    public void ReleaseScript_TakesTheVersionFromDirectoryBuildProps_Tests_AndNeverPackagesUserData()
    {
        var script = Read("tools", "release.ps1");

        script.Should().Contain("Directory.Build.props").And.Contain("dotnet test").And.Contain("-warnaserror");
        script.Should().Contain("PublishProfile=portable-win-x64");
        script.Should().Contain("-win-x64-portable.zip").And.Contain("SHA256");
        script.Should().Contain("User data must never be packaged");
        script.Should().NotContain("git push").And.NotContain("git tag").And.NotContain("gh release", "publishing is the workflow's job, never the script's");
    }

    [Fact]
    public void CiWorkflow_BuildsWithoutWarnings_Tests_AndOnlyReadsTheRepository()
    {
        var ci = Read(".github", "workflows", "ci.yml");

        ci.Should().Contain("runs-on: windows-latest");
        ci.Should().Contain("dotnet restore").And.Contain("-warnaserror").And.Contain("dotnet test");
        ci.Should().Contain($"Category!={TestCategories.InteractiveDesktop}");
        ci.Should().Contain("contents: read").And.NotContain("contents: write");
        ci.Should().NotContain("secrets.");
    }

    [Fact]
    public void ReleaseWorkflow_RunsOnVersionTags_WithMinimalPermissions_AndNoSecretsOfOurOwn()
    {
        var release = Read(".github", "workflows", "release.yml");

        release.Should().Contain("tags: ['v*']");
        release.Should().Contain("tools/release.ps1");
        release.Should().Contain("gh @arguments").And.Contain("'release', 'create'");
        release.Should().NotContain("secrets.", "only the token of the workflow itself is used");
        release.Should().Contain("${{ github.token }}");

        var permissions = release[release.IndexOf("permissions:", StringComparison.Ordinal)..];
        permissions = permissions[..permissions.IndexOf("concurrency:", StringComparison.Ordinal)];
        permissions.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Should().Equal("permissions:", "contents: write");
    }

    [Fact]
    public void Installer_IsPerUser_WithoutAdministrator_AndAsksBeforeDeletingTheData()
    {
        var iss = Read("tools", "installer", "omnimud.iss");

        iss.Should().Contain("PrivilegesRequired=lowest");
        iss.Should().Contain(@"DefaultDirName={localappdata}\Programs\Omnimud");
        iss.Should().NotContain("{commonpf").And.NotContain("{pf}").And.NotContain("PrivilegesRequired=admin");
        iss.Should().Contain("compiler:Languages\\Spanish.isl").And.Contain("compiler:Default.isl");
        iss.Should().Contain("CurUninstallStepChanged").And.Contain("MB_DEFBUTTON2", "the default answer is to KEEP the data");
        iss.Should().Contain("UninstallSilent", "a silent uninstall never deletes the data");
        iss.Should().Contain(@"Excludes: ""\data\*");
        File.ReadAllBytes(Path.Combine(HelpAndAppInfoTests.RepositoryRoot(), "tools", "installer", "omnimud.iss")).Take(3)
            .Should().Equal([0xEF, 0xBB, 0xBF], "Inno Setup needs the UTF-8 BOM to read the Spanish messages right");
    }
}
