using System.Runtime.ExceptionServices;

namespace Omnimud.UI.Tests;

/// <summary>WinForms controls need an STA thread; xUnit runs tests on MTA pool threads.</summary>
internal static class Sta
{
    public static void Run(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(60)))
            throw new TimeoutException("STA test did not finish in 60 s.");
        failure?.Throw();
    }
}
