using FluentAssertions;
using Omnimud.Core.Storage;

namespace Omnimud.Core.Tests.Storage;

public sealed class AppPathsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("omnimud-apppaths-").FullName;

    private string BaseDirectory => Path.Combine(_root, "app");
    private string FallbackDirectory => Path.Combine(_root, "user profile", "Omnimud");

    public AppPathsTests()
    {
        Directory.CreateDirectory(BaseDirectory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public void Resolve_WritableBaseDirectory_IsPortable_AndCreatesTheDataFolder()
    {
        var paths = AppPaths.Resolve(BaseDirectory, FallbackDirectory);

        paths.IsPortable.Should().BeTrue();
        paths.BaseDirectory.Should().Be(BaseDirectory);
        paths.DataDirectory.Should().Be(Path.Combine(BaseDirectory, "data"));
        Directory.Exists(paths.DataDirectory).Should().BeTrue();
        Directory.Exists(FallbackDirectory).Should().BeFalse("the per-user directory is not touched when portable works");
        Directory.EnumerateFileSystemEntries(paths.DataDirectory).Should().BeEmpty("the write probe cleans up after itself");
    }

    [Fact]
    public void Resolve_EverythingHangsFromTheDataDirectory_ExceptTheShippedSounds()
    {
        var paths = AppPaths.Resolve(BaseDirectory, FallbackDirectory);

        paths.DatabasePath.Should().Be(Path.Combine(paths.DataDirectory, "omnimud.db"));
        paths.MasterKeyPath.Should().Be(Path.Combine(paths.DataDirectory, "master.key"));
        paths.SoundsDirectory.Should().Be(Path.Combine(paths.DataDirectory, "sounds"));
        paths.LogsDirectory.Should().Be(Path.Combine(paths.DataDirectory, "logs"));
        paths.AppSoundsDirectory.Should().Be(Path.Combine(BaseDirectory, "sounds"));
    }

    [Fact]
    public void Resolve_IsRepeatable_AndKeepsExistingData()
    {
        var first = AppPaths.Resolve(BaseDirectory, FallbackDirectory);
        File.WriteAllText(first.DatabasePath, "datos");

        var second = AppPaths.Resolve(BaseDirectory, FallbackDirectory);

        second.DataDirectory.Should().Be(first.DataDirectory);
        File.ReadAllText(second.DatabasePath).Should().Be("datos");
    }

    [Fact]
    public void Resolve_DataNotWritable_FallsBackToThePerUserDirectory()
    {
        // A FILE called "data" makes the portable folder impossible to create or write, like a read-only install folder.
        File.WriteAllText(Path.Combine(BaseDirectory, "data"), "no soy una carpeta");

        var paths = AppPaths.Resolve(BaseDirectory, FallbackDirectory);

        paths.IsPortable.Should().BeFalse();
        paths.DataDirectory.Should().Be(FallbackDirectory);
        Directory.Exists(FallbackDirectory).Should().BeTrue("it is created, parents included");
        paths.DatabasePath.Should().Be(Path.Combine(FallbackDirectory, "omnimud.db"));
        paths.BaseDirectory.Should().Be(BaseDirectory);
        paths.AppSoundsDirectory.Should().Be(Path.Combine(BaseDirectory, "sounds"), "shipped sounds still come from the install folder");
        File.ReadAllText(Path.Combine(BaseDirectory, "data")).Should().Be("no soy una carpeta");
    }

    [Theory]
    [InlineData(null, "x")]
    [InlineData("", "x")]
    [InlineData("  ", "x")]
    [InlineData("x", null)]
    [InlineData("x", " ")]
    public void Resolve_RejectsEmptyDirectories(string? baseDirectory, string? fallbackDirectory)
    {
        var resolve = () => AppPaths.Resolve(baseDirectory!, fallbackDirectory!);

        resolve.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ImportLegacyData_CopiesDatabaseAndKey_Once()
    {
        var legacy = CreateLegacy(database: "bd antigua", key: "clave antigua");
        var paths = AppPaths.Resolve(BaseDirectory, FallbackDirectory);

        paths.ImportLegacyDataFrom(legacy).Should().BeTrue();

        File.ReadAllText(paths.DatabasePath).Should().Be("bd antigua");
        File.ReadAllText(paths.MasterKeyPath).Should().Be("clave antigua");
        File.Exists(Path.Combine(legacy, "omnimud.db")).Should().BeTrue("it is a copy, the old data stays as a backup");

        // Second start: the user has worked with the portable copy in the meantime.
        File.WriteAllText(paths.DatabasePath, "bd nueva");
        File.WriteAllText(Path.Combine(legacy, "omnimud.db"), "bd antigua modificada");

        paths.ImportLegacyDataFrom(legacy).Should().BeFalse();
        File.ReadAllText(paths.DatabasePath).Should().Be("bd nueva");
    }

    [Fact]
    public void ImportLegacyData_NeverOverwritesAnExistingDatabase_NorItsKey()
    {
        var legacy = CreateLegacy(database: "bd antigua", key: "clave antigua");
        var paths = AppPaths.Resolve(BaseDirectory, FallbackDirectory);
        File.WriteAllText(paths.DatabasePath, "bd portable");
        File.WriteAllText(paths.MasterKeyPath, "clave portable");

        paths.ImportLegacyDataFrom(legacy).Should().BeFalse();

        File.ReadAllText(paths.DatabasePath).Should().Be("bd portable");
        File.ReadAllText(paths.MasterKeyPath).Should().Be("clave portable");
    }

    [Fact]
    public void ImportLegacyData_KeepsAnExistingKey_EvenWhenTheDatabaseIsImported()
    {
        var legacy = CreateLegacy(database: "bd antigua", key: "clave antigua");
        var paths = AppPaths.Resolve(BaseDirectory, FallbackDirectory);
        File.WriteAllText(paths.MasterKeyPath, "clave portable");

        paths.ImportLegacyDataFrom(legacy).Should().BeTrue();

        File.ReadAllText(paths.DatabasePath).Should().Be("bd antigua");
        File.ReadAllText(paths.MasterKeyPath).Should().Be("clave portable");
    }

    [Fact]
    public void ImportLegacyData_WithoutKey_CopiesOnlyTheDatabase()
    {
        var legacy = CreateLegacy(database: "bd antigua", key: null);
        var paths = AppPaths.Resolve(BaseDirectory, FallbackDirectory);

        paths.ImportLegacyDataFrom(legacy).Should().BeTrue();

        File.Exists(paths.DatabasePath).Should().BeTrue();
        File.Exists(paths.MasterKeyPath).Should().BeFalse();
    }

    [Fact]
    public void ImportLegacyData_NothingToImport_ReturnsFalse()
    {
        var paths = AppPaths.Resolve(BaseDirectory, FallbackDirectory);

        paths.ImportLegacyDataFrom(Path.Combine(_root, "no existe")).Should().BeFalse();
        paths.ImportLegacyDataFrom(CreateLegacy(database: null, key: "solo clave")).Should().BeFalse();

        File.Exists(paths.DatabasePath).Should().BeFalse();
        File.Exists(paths.MasterKeyPath).Should().BeFalse("a key without its database is useless");
    }

    [Fact]
    public void ImportLegacyData_WhenNotPortable_DoesNothing()
    {
        // In fallback mode the data directory IS the legacy directory: there is nothing to import from itself.
        File.WriteAllText(Path.Combine(BaseDirectory, "data"), "bloqueo");
        var legacy = CreateLegacy(database: "bd antigua", key: null);
        var paths = AppPaths.Resolve(BaseDirectory, FallbackDirectory);

        paths.ImportLegacyDataFrom(legacy).Should().BeFalse();

        File.Exists(paths.DatabasePath).Should().BeFalse();
    }

    private string CreateLegacy(string? database, string? key)
    {
        var legacy = Path.Combine(_root, "legacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(legacy);
        if (database is not null) File.WriteAllText(Path.Combine(legacy, "omnimud.db"), database);
        if (key is not null) File.WriteAllText(Path.Combine(legacy, "master.key"), key);
        return legacy;
    }
}
