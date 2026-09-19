using System.Text.RegularExpressions;
using System.Windows.Forms.Automation;
using Omnimud.Core.Accessibility;
using Omnimud.Core.Accessibility.Readers;
using Omnimud.Core.Options;
using Omnimud.Core.Session;

namespace Omnimud.UI.Services.Accessibility;

/// <summary>Speaks text through the user's screen reader.</summary>
public interface IAnnouncer
{
    ScreenReaderMode Mode { get; set; }

    /// <summary>When true nothing is announced (Ctrl+M).</summary>
    bool Muted { get; set; }

    /// <summary>Returns true if some channel accepted the text.</summary>
    bool Announce(string text, AnnouncePriority priority);

    void StopSpeech();
}

/// <summary>Raises a UI Automation notification event. Abstracted so the announcer can be tested.</summary>
public interface IUiaNotifier
{
    bool Raise(string text, AutomationNotificationProcessing processing);
}

/// <summary>
/// UI Automation notifications first: any screen reader that listens to UIA gets the text
/// without vendor libraries. If the event cannot be raised (old Windows, no UIA provider) or
/// the user picked a specific reader, the native JAWS / NVDA libraries are used.
/// </summary>
public sealed partial class Announcer : IAnnouncer
{
    private readonly IUiaNotifier _uia;
    private readonly ScreenReaderApi _auto;
    private readonly ScreenReaderApi _jaws;
    private readonly ScreenReaderApi _nvda;

    public Announcer(IUiaNotifier uia)
        : this(uia, new ScreenReaderApi(), new ScreenReaderApi([new JawsScreenReader()]), new ScreenReaderApi([new NvdaScreenReader()]))
    {
    }

    public Announcer(IUiaNotifier uia, ScreenReaderApi autoDetect, ScreenReaderApi jaws, ScreenReaderApi nvda)
    {
        _uia = uia;
        _auto = autoDetect;
        _jaws = jaws;
        _nvda = nvda;
    }

    public ScreenReaderMode Mode { get; set; } = ScreenReaderMode.Automatic;

    public bool Muted { get; set; }

    public bool Announce(string text, AnnouncePriority priority)
    {
        if (Muted || Mode == ScreenReaderMode.None) return false;

        text = Clean(text);
        if (text.Length == 0) return false;

        var interrupt = priority == AnnouncePriority.Interrupt;
        switch (Mode)
        {
            case ScreenReaderMode.Jaws:
                return _jaws.Speak(text, interrupt);
            case ScreenReaderMode.Nvda:
                return _nvda.Speak(text, interrupt);
            default:
                return _uia.Raise(text, ToProcessing(priority)) || _auto.Speak(text, interrupt);
        }
    }

    public void StopSpeech()
    {
        switch (Mode)
        {
            case ScreenReaderMode.Jaws: _jaws.StopSpeech(); break;
            case ScreenReaderMode.Nvda: _nvda.StopSpeech(); break;
            case ScreenReaderMode.Automatic:
                // UIA has no "be quiet": an empty high-priority notification flushes what is pending.
                if (!_auto.StopSpeech())
                    _uia.Raise(" ", AutomationNotificationProcessing.ImportantMostRecent);
                break;
        }
    }

    internal static AutomationNotificationProcessing ToProcessing(AnnouncePriority priority) => priority switch
    {
        AnnouncePriority.Interrupt => AutomationNotificationProcessing.ImportantMostRecent,
        AnnouncePriority.MostRecent => AutomationNotificationProcessing.MostRecent,
        _ => AutomationNotificationProcessing.All,
    };

    /// <summary>A screen reader must never receive escape sequences or control characters.</summary>
    internal static string Clean(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        text = AnsiSequence().Replace(text, string.Empty);
        text = ControlChars().Replace(text, " ");
        return text.Trim();
    }

    [GeneratedRegex(@"\x1b\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex AnsiSequence();

    [GeneratedRegex(@"[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]")]
    private static partial Regex ControlChars();
}

/// <summary>Raises the notification from a control's accessible object. Call on the UI thread.</summary>
public sealed class ControlUiaNotifier(Control source) : IUiaNotifier
{
    public bool Raise(string text, AutomationNotificationProcessing processing)
    {
        if (source.IsDisposed || !source.IsHandleCreated) return false;
        if (source.InvokeRequired)
        {
            try { return (bool)source.Invoke(() => Raise(text, processing)); }
            catch (ObjectDisposedException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        return source.AccessibilityObject.RaiseAutomationNotification(
            AutomationNotificationKind.Other, processing, text);
    }
}
