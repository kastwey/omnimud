using Omnimud.Core.Session;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Services;

/// <summary>
/// Quick review of the Messages box from anywhere in the game window, as in the original client:
///  · Ctrl+1..9, Ctrl+0 (top row or numpad): speaks message N counting from the most recent
///    (0 = 10). Digits pressed less than 400 ms apart are concatenated, so Ctrl+1, Ctrl+5
///    speaks message 15. Up to three digits; a fourth is rejected.
///  · Ctrl+º: walks backwards. First press speaks message 1; each further press within 800 ms
///    moves to 2, 3... After a pause it starts again at 1.
///  · Escape cancels a pending digit sequence.
/// </summary>
public sealed class MessageReviewer(TimeProvider time)
{
    private static readonly TimeSpan DigitWindow = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan SequenceWindow = TimeSpan.FromMilliseconds(800);
    private const int MaxDigits = 3;

    private int _number;
    private int _digits;
    private long _lastDigitAt;
    private int _sequence;
    private long _lastSequenceAt;

    /// <summary>Returns the text to speak, or null when the key must be rejected with a beep.</summary>
    public string? PressDigit(int digit, IReadOnlyList<SessionMessage> messages)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(digit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(digit, 9);

        var now = time.GetTimestamp();
        var continuing = _digits > 0 && time.GetElapsedTime(_lastDigitAt, now) < DigitWindow;

        if (continuing)
        {
            if (_digits >= MaxDigits)
            {
                Cancel();
                return null;
            }
            _number = _number * 10 + digit;
            _digits++;
        }
        else
        {
            // A lone 0 means the tenth message.
            _number = digit == 0 ? 10 : digit;
            _digits = 1;
        }

        _lastDigitAt = now;
        _sequence = 0;
        return Describe(_number, messages);
    }

    public string PressSequential(IReadOnlyList<SessionMessage> messages)
    {
        var now = time.GetTimestamp();
        var continuing = _sequence > 0 && time.GetElapsedTime(_lastSequenceAt, now) < SequenceWindow;
        _sequence = continuing ? _sequence + 1 : 1;
        _lastSequenceAt = now;
        _digits = 0;

        var text = Describe(_sequence, messages);
        // Past the oldest message: stay there so the next press repeats the warning instead of growing forever.
        if (_sequence > messages.Count) _sequence = Math.Max(messages.Count, 1);
        return text;
    }

    public void Cancel()
    {
        _number = 0;
        _digits = 0;
    }

    private static string Describe(int number, IReadOnlyList<SessionMessage> messages)
    {
        if (messages.Count == 0) return Strings.Client_ReviewNoMessages;
        if (number > messages.Count)
            return messages.Count == 1
                ? Strings.Client_ReviewOnlyOne
                : string.Format(Strings.Client_ReviewOnlyN, messages.Count);

        // Messages are stored oldest first; number 1 is the most recent.
        var message = messages[^number];
        return string.Format(Strings.Client_ReviewItem, number, message.Text);
    }
}
