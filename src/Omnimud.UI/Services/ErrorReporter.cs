using Omnimud.UI.Forms;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Services;

/// <summary>
/// Last-resort handler: an unexpected error must never close the client silently in the middle of a game.
/// Shows an accessible dialog (<see cref="FrmUnexpectedError"/>) that lets the user report the error, copy its
/// details or simply carry on. The rules (any thread, never in cascade, fall back to a message box) live in
/// <see cref="UnexpectedErrorCoordinator"/>.
/// </summary>
internal static class ErrorReporter
{
    private static readonly object Gate = new();
    private static UnexpectedErrorCoordinator? _coordinator;

    /// <summary>
    /// Call once on the UI thread at startup. <paramref name="report"/> opens the report dialog for an exception;
    /// without it the dialog has no "Report this error" button action (it just closes).
    /// </summary>
    public static void Configure(Action<Exception, string>? report)
    {
        // Installed explicitly so that errors raised on other threads before the first window exists can be marshalled too.
        if (SynchronizationContext.Current is not WindowsFormsSynchronizationContext)
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        var context = SynchronizationContext.Current;
        var uiThread = Environment.CurrentManagedThreadId;
        lock (Gate)
            _coordinator = Create(report, () => context, () => Environment.CurrentManagedThreadId == uiThread);
    }

    /// <summary>For tests: put a coordinator with fakes in place (or null to go back to the default).</summary>
    internal static void Use(UnexpectedErrorCoordinator? coordinator)
    {
        lock (Gate) _coordinator = coordinator;
    }

    private static UnexpectedErrorCoordinator Coordinator
    {
        get
        {
            lock (Gate)
            {
                // Not configured (a form used outside the application): show on whatever thread reports.
                return _coordinator ??= Create(report: null, () => null, isUiThread: null);
            }
        }
    }

    private static UnexpectedErrorCoordinator Create(Action<Exception, string>? report, Func<SynchronizationContext?> context, Func<bool>? isUiThread) =>
        new(() => new FrmUnexpectedError(canReport: report is not null), FallbackMessageBox, report, context, sanitizer: null, isUiThread);

    /// <param name="owner">Only used to reach the UI thread when the reporter was never configured; the dialog is owned by the active window.</param>
    public static void Show(Exception ex, IWin32Window? owner = null)
    {
        if (owner is Control { IsDisposed: false, IsHandleCreated: true, InvokeRequired: true } control)
        {
            try
            {
                control.BeginInvoke(() => Coordinator.Handle(ex));
                return;
            }
            catch (InvalidOperationException)
            {
                // The window went away meanwhile: report from here.
            }
        }
        Coordinator.Handle(ex);
    }

    /// <summary>For a thread that is about to die (AppDomain.UnhandledException): waits until the user has seen the dialog.</summary>
    public static void ShowAndWait(Exception ex) => Coordinator.HandleAndWait(ex);

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

    private static void FallbackMessageBox(string message) =>
        MessageBox.Show(string.Format(Strings.Error_Unexpected, message), Strings.App_Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
}
