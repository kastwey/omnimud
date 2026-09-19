using Omnimud.UI.Resources;

namespace Omnimud.UI.Services;

/// <summary>
/// Last-resort handler: an unexpected error must never close the client silently
/// in the middle of a game. Shows the problem and lets the user carry on.
/// </summary>
internal static class ErrorReporter
{
    public static void Show(Exception ex, IWin32Window? owner = null)
    {
        var message = string.Format(Strings.Error_Unexpected, ex.Message);
        if (owner is Control { IsDisposed: false, InvokeRequired: true } control)
        {
            control.BeginInvoke(() => Show(ex, owner));
            return;
        }

        MessageBox.Show(owner, message, Strings.App_Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    /// <summary>
    /// Runs an async UI action (an "async void" event handler body) reporting failures
    /// instead of letting them tear the process down.
    /// </summary>
    public static async void Run(IWin32Window? owner, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Show(ex, owner);
        }
    }
}
