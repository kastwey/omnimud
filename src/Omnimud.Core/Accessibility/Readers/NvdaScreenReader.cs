using Omnimud.Core.Accessibility.Native;

namespace Omnimud.Core.Accessibility.Readers;

/// <summary>
/// Screen reader implementation backed by NVDA (nvdaControllerClient.dll).
/// </summary>
public sealed class NvdaScreenReader : IScreenReader
{
    static NvdaScreenReader() => NativeLibraryResolver.EnsureRegistered();

    public string Name => "NVDA";

    public bool IsRunning
    {
        get
        {
            try { return NvdaNative.TestIfRunning() == 0; }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
            catch { return false; }
        }
    }

    public bool Speak(string text, bool interrupt)
    {
        if (string.IsNullOrEmpty(text)) return false;
        try
        {
            if (interrupt) NvdaNative.CancelSpeech();
            return NvdaNative.SpeakText(text) == 0;
        }
        catch { return false; }
    }

    public bool StopSpeech()
    {
        try { return NvdaNative.CancelSpeech() == 0; }
        catch { return false; }
    }

    public bool Braille(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        try { return NvdaNative.BrailleMessage(text) == 0; }
        catch { return false; }
    }
}
