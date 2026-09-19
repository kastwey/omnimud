namespace Omnimud.Core.Storage;

/// <summary>
/// Resolves where user data lives. Omnimud is portable: everything goes in a "data" folder
/// next to the executable. Only when that folder is not writable (e.g. Program Files) does it
/// fall back to a per-user directory.
/// </summary>
public sealed class AppPaths
{
    private const string DataFolderName = "data";

    private AppPaths(string baseDirectory, string dataDirectory, bool isPortable)
    {
        BaseDirectory = baseDirectory;
        DataDirectory = dataDirectory;
        IsPortable = isPortable;
    }

    /// <summary>Directory of the executable.</summary>
    public string BaseDirectory { get; }

    public string DataDirectory { get; }

    /// <summary>True when data lives next to the executable.</summary>
    public bool IsPortable { get; }

    public string DatabasePath => Path.Combine(DataDirectory, "omnimud.db");
    public string MasterKeyPath => Path.Combine(DataDirectory, "master.key");
    public string SoundsDirectory => Path.Combine(DataDirectory, "sounds");
    public string LogsDirectory => Path.Combine(DataDirectory, "logs");

    /// <summary>Sounds shipped with the application (click, pop, url, error...).</summary>
    public string AppSoundsDirectory => Path.Combine(BaseDirectory, "sounds");

    public static AppPaths Resolve(string baseDirectory, string fallbackDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackDirectory);

        var portable = Path.Combine(baseDirectory, DataFolderName);
        if (TryEnsureWritable(portable))
            return new AppPaths(baseDirectory, portable, isPortable: true);

        Directory.CreateDirectory(fallbackDirectory);
        return new AppPaths(baseDirectory, fallbackDirectory, isPortable: false);
    }

    /// <summary>
    /// One-time import of data created by earlier builds that stored everything in the
    /// per-user directory. Never overwrites existing portable data.
    /// </summary>
    public bool ImportLegacyDataFrom(string legacyDirectory)
    {
        if (!IsPortable || File.Exists(DatabasePath)) return false;
        var legacyDb = Path.Combine(legacyDirectory, "omnimud.db");
        if (!File.Exists(legacyDb)) return false;

        File.Copy(legacyDb, DatabasePath);
        var legacyKey = Path.Combine(legacyDirectory, "master.key");
        if (File.Exists(legacyKey) && !File.Exists(MasterKeyPath))
            File.Copy(legacyKey, MasterKeyPath);
        return true;
    }

    private static bool TryEnsureWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
