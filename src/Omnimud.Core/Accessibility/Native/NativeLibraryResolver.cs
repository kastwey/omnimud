using System.Runtime.InteropServices;

namespace Omnimud.Core.Accessibility.Native;

/// <summary>
/// Resolves screen reader native DLLs from the architecture-specific folder
/// (dlls/x64, dlls/arm64, dlls/x86) shipped alongside the executable.
/// </summary>
internal static class NativeLibraryResolver
{
    private static bool _registered;
    private static readonly object _lock = new();

    /// <summary>
    /// Registers a DllImport resolver for the given assembly so that the listed
    /// library names are loaded from the architecture-specific dlls/ folder.
    /// </summary>
    public static void EnsureRegistered()
    {
        if (_registered) return;
        lock (_lock)
        {
            if (_registered) return;
            NativeLibrary.SetDllImportResolver(typeof(NativeLibraryResolver).Assembly, Resolve);
            _registered = true;
        }
    }

    private static IntPtr Resolve(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        // Only intercept the screen reader libraries
        if (!IsScreenReaderLibrary(libraryName))
            return IntPtr.Zero;

        var archDir = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "x64"
        };

        // Map import name to actual file name per architecture
        var fileName = ResolveFileName(libraryName, archDir);

        // Try local dlls/<arch>/ folder first (shipped with app)
        var localPath = Path.Combine(AppContext.BaseDirectory, "dlls", archDir, fileName);
        if (File.Exists(localPath) && NativeLibrary.TryLoad(localPath, out var local))
            return local;

        // Fall back to default system search (in case DLL is installed in PATH)
        return NativeLibrary.TryLoad(fileName, assembly, searchPath, out var sysHandle)
            ? sysHandle
            : IntPtr.Zero;
    }

    private static bool IsScreenReaderLibrary(string name) =>
        name.Equals("jaws", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("nvdaControllerClient", StringComparison.OrdinalIgnoreCase);

    private static string ResolveFileName(string importName, string archDir)
    {
        // For "jaws" import, pick the modern FSAPI.dll on x64/arm64
        // and the legacy jfwapi.dll on x86
        if (importName.Equals("jaws", StringComparison.OrdinalIgnoreCase))
            return archDir == "x86" ? "jfwapi.dll" : "FSAPI.dll";

        return importName + ".dll";
    }
}
