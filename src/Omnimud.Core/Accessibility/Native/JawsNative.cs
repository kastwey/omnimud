using System.Runtime.InteropServices;

namespace Omnimud.Core.Accessibility.Native;

/// <summary>
/// Raw P/Invoke bindings for the JAWS API (FSAPI.dll on x64/arm64, jfwapi.dll on x86).
/// The actual file is resolved at runtime by <see cref="NativeLibraryResolver"/>.
/// </summary>
internal static class JawsNative
{
    private const string LibraryName = "jaws";

    /// <summary>
    /// Speaks the given string. If <paramref name="interrupt"/> is true, current
    /// speech is interrupted; otherwise the text is queued.
    /// </summary>
    [DllImport(LibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl, EntryPoint = "JFWSayString")]
    public static extern bool SayString(string text, bool interrupt);

    /// <summary>Stops all current speech.</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "JFWStopSpeech")]
    public static extern bool StopSpeech();

    /// <summary>Returns the JAWS version (or 0 if unavailable).</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "JFWGetVersion")]
    public static extern int GetVersion();

    /// <summary>Runs an arbitrary JAWS script by name.</summary>
    [DllImport(LibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl, EntryPoint = "JFWRunScript")]
    public static extern bool RunScript(string scriptName);

    /// <summary>Runs an arbitrary JAWS function by name.</summary>
    [DllImport(LibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl, EntryPoint = "JFWRunFunction")]
    public static extern bool RunFunction(string functionName);
}
