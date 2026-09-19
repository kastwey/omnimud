using Omnimud.Core.Session;

namespace Omnimud.UI.Services;

/// <summary>
/// From a keyboard key to a movement key code, and the rule that says when a key press is a
/// movement. No window needed: the game window only asks.
/// </summary>
internal static class MovementKeyMap
{
    /// <summary>The movement key for a key code (no modifiers), or null if the key is not one.</summary>
    public static MovementKey? FromKey(Keys keyCode) => keyCode switch
    {
        >= Keys.NumPad0 and <= Keys.NumPad9 => (MovementKey)(keyCode - Keys.NumPad0),
        Keys.Up => MovementKey.ArrowUp,
        Keys.Down => MovementKey.ArrowDown,
        Keys.Left => MovementKey.ArrowLeft,
        Keys.Right => MovementKey.ArrowRight,
        Keys.PageUp => MovementKey.PageUp,
        Keys.PageDown => MovementKey.PageDown,
        Keys.Home => MovementKey.Home,
        Keys.End => MovementKey.End,
        _ => null,
    };

    /// <summary>
    /// The movement a key press means, or null when the key keeps its normal behaviour. It moves when:
    /// movement mode is on, no password is being typed (digits and arrows belong to the password
    /// box then), there is no modifier (Ctrl+numpad reads messages, Ctrl+arrows browse the history,
    /// Shift+arrows select), the key has an effective command, and — for the keys that also edit
    /// text: arrows, Page Up/Down, Home, End — the text to send is empty.
    /// </summary>
    public static MovementKey? Resolve(Keys keyData, IMudSession session, bool inputIsEmpty)
    {
        if ((keyData & Keys.Modifiers) != Keys.None) return null;
        if (!session.MovementMode || session.PasswordMode) return null;
        if (FromKey(keyData & Keys.KeyCode) is not { } key) return null;
        if (!MovementKeys.IsNumPad((int)key) && !inputIsEmpty) return null;
        return session.HasMovement((int)key) ? key : null;
    }
}
