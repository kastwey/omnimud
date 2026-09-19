using Omnimud.Core.Accessibility.Native;

namespace Omnimud.Core.Accessibility.Readers;

/// <summary>
/// Screen reader implementation backed by JAWS (FSAPI.dll on x64/arm64, jfwapi.dll on x86).
/// </summary>
public sealed class JawsScreenReader : IScreenReader
{
    static JawsScreenReader() => NativeLibraryResolver.EnsureRegistered();

    public string Name => "JAWS";

    public bool IsRunning
    {
        get
        {
            try
            {
                // GetVersion returns 0 if JAWS is not running
                return JawsNative.GetVersion() > 0;
            }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException)
            {
                // Some FSAPI builds may not export GetVersion; fall back to a no-op SayString
                try { return JawsNative.SayString(string.Empty, false); }
                catch { return false; }
            }
            catch { return false; }
        }
    }

    public bool Speak(string text, bool interrupt)
    {
        if (string.IsNullOrEmpty(text)) return false;
        try { return JawsNative.SayString(text, interrupt); }
        catch { return false; }
    }

    public bool StopSpeech()
    {
        try { return JawsNative.StopSpeech(); }
        catch { return false; }
    }

    public bool Braille(string text) => false; // JAWS API does not expose direct braille output
}
