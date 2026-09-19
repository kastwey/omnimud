using Omnimud.Core.Reports;

namespace Omnimud.UI.Services;

public enum UnexpectedErrorChoice
{
    Continue,
    Report
}

/// <summary>The "unexpected error" dialog as the coordinator sees it. The real one is <c>FrmUnexpectedError</c>.</summary>
public interface IUnexpectedErrorView : IDisposable
{
    /// <summary>Adds the details of one more error to a view that may already be on screen. UI thread only.</summary>
    void AddError(string details);

    /// <summary>Everything added so far, as one text.</summary>
    string Details { get; }

    /// <summary>Modal. Returns what the user chose.</summary>
    UnexpectedErrorChoice ShowModal();
}

/// <summary>
/// What happens when something nobody expected goes wrong. It must never make things worse:
/// <list type="bullet">
/// <item>works from any thread (everything is moved to the UI thread);</item>
/// <item>never in cascade: while one dialog is open, further errors are added to its details instead of opening more;</item>
/// <item>re-entrant safe: if the dialog itself fails, a plain message box is used; if that fails too, nothing more is tried;</item>
/// <item>the application carries on afterwards.</item>
/// </list>
/// No WinForms here beyond what the delegates bring, so every rule is tested without windows.
/// </summary>
public sealed class UnexpectedErrorCoordinator
{
    private readonly Func<IUnexpectedErrorView> _viewFactory;
    private readonly Action<string> _fallback;
    private readonly Action<Exception, string>? _report;
    private readonly Func<SynchronizationContext?> _uiContext;
    private readonly Func<bool>? _isUiThread;
    private readonly ReportSanitizer _sanitizer;
    private readonly object _gate = new();
    private readonly List<string> _pending = [];
    private IUnexpectedErrorView? _open;
    private bool _showing;

    /// <param name="viewFactory">Creates the dialog. Called on the UI thread.</param>
    /// <param name="fallback">Last resort: shows a plain message (a message box). Must not throw, but is guarded anyway.</param>
    /// <param name="report">Opens the report dialog for an exception (second argument: the details of everything that happened).</param>
    /// <param name="uiContext">The synchronization context of the UI thread; null result = no UI thread yet (show where we are).</param>
    /// <param name="isUiThread">Says whether the caller already is on the UI thread; null = compare the current context with <paramref name="uiContext"/>.</param>
    public UnexpectedErrorCoordinator(Func<IUnexpectedErrorView> viewFactory, Action<string> fallback,
        Action<Exception, string>? report, Func<SynchronizationContext?> uiContext, ReportSanitizer? sanitizer = null, Func<bool>? isUiThread = null)
    {
        _isUiThread = isUiThread;
        ArgumentNullException.ThrowIfNull(viewFactory);
        ArgumentNullException.ThrowIfNull(fallback);
        ArgumentNullException.ThrowIfNull(uiContext);
        _viewFactory = viewFactory;
        _fallback = fallback;
        _report = report;
        _uiContext = uiContext;
        _sanitizer = sanitizer ?? ReportSanitizer.ForThisMachine();
    }

    /// <summary>True while the dialog is on screen.</summary>
    public bool IsShowing { get { lock (_gate) return _showing; } }

    /// <summary>From any thread. Returns at once when called off the UI thread.</summary>
    public void Handle(Exception exception) => Dispatch(exception, wait: false);

    /// <summary>From any thread; waits until the user has dismissed the dialog (for a thread that is about to die).</summary>
    public void HandleAndWait(Exception exception) => Dispatch(exception, wait: true);

    /// <summary>The text shown in the details box and copied by "Copy details": type, message and stack, already without user paths.</summary>
    public string Describe(Exception exception)
    {
        try
        {
            return _sanitizer.Sanitize(exception.ToString());
        }
        catch (Exception)
        {
            return exception.GetType().FullName ?? "Exception";
        }
    }

    private void Dispatch(Exception exception, bool wait)
    {
        if (exception is null) return;
        SynchronizationContext? context;
        try { context = _uiContext(); }
        catch (Exception) { context = null; }

        if (context is null || (_isUiThread?.Invoke() ?? ReferenceEquals(SynchronizationContext.Current, context)))
        {
            Show(exception);
            return;
        }

        try
        {
            if (wait) context.Send(_ => Show(exception), null);
            else context.Post(_ => Show(exception), null);
        }
        catch (Exception)
        {
            // The UI thread is gone (shutting down): tell the user from here.
            Fallback(exception);
        }
    }

    private void Show(Exception exception)
    {
        var details = Describe(exception);
        lock (_gate)
        {
            if (_showing)
            {
                // Never in cascade: the dialog that is already open gets the details.
                if (_open is null) _pending.Add(details);
                else TryAdd(_open, details);
                return;
            }
            _showing = true;
        }

        var choice = UnexpectedErrorChoice.Continue;
        var everything = details;
        try
        {
            var view = _viewFactory();
            try
            {
                lock (_gate)
                {
                    _open = view;
                    view.AddError(details);
                    foreach (var pending in _pending) view.AddError(pending);
                    _pending.Clear();
                }
                choice = view.ShowModal();
                everything = view.Details;
            }
            finally
            {
                lock (_gate) _open = null;
                try { view.Dispose(); } catch (Exception) { /* a dialog that cannot even be disposed changes nothing */ }
            }
        }
        catch (Exception)
        {
            // The dialog itself failed: fall back to the simplest thing that can tell the user.
            Fallback(exception);
            choice = UnexpectedErrorChoice.Continue;
        }
        finally
        {
            lock (_gate)
            {
                _pending.Clear();
                _showing = false;
            }
        }

        if (choice == UnexpectedErrorChoice.Report && _report is not null)
        {
            try { _report(exception, everything); }
            catch (Exception) { Fallback(exception); }
        }
    }

    private static void TryAdd(IUnexpectedErrorView view, string details)
    {
        try { view.AddError(details); }
        catch (Exception) { /* losing the details of a second error is better than a third one */ }
    }

    private void Fallback(Exception exception)
    {
        try { _fallback(exception.Message); }
        catch (Exception) { /* nothing else can be done without making it worse */ }
    }
}
