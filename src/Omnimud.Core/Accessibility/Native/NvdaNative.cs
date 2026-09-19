using System.Runtime.InteropServices;

namespace Omnimud.Core.Accessibility.Native;

/// <summary>
/// Raw P/Invoke bindings for the NVDA controller client (nvdaControllerClient.dll).
/// All functions return 0 on success and a non-zero error code on failure.
/// </summary>
internal static class NvdaNative
{
    private const string LibraryName = "nvdaControllerClient";

    /// <summary>Tests whether NVDA is currently running. Returns 0 if running.</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, EntryPoint = "nvdaController_testIfRunning")]
    public static extern int TestIfRunning();

    /// <summary>Speaks the given text via NVDA.</summary>
    [DllImport(LibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall, EntryPoint = "nvdaController_speakText")]
    public static extern int SpeakText(string text);

    /// <summary>Cancels any current NVDA speech.</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, EntryPoint = "nvdaController_cancelSpeech")]
    public static extern int CancelSpeech();

    /// <summary>Displays the given text on the braille display via NVDA.</summary>
    [DllImport(LibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall, EntryPoint = "nvdaController_brailleMessage")]
    public static extern int BrailleMessage(string text);

    /// <summary>Returns the NVDA process ID, or 0 if NVDA is not running.</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, EntryPoint = "nvdaController_getProcessId")]
    public static extern int GetProcessId();
}
