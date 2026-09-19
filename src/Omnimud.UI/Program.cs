using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Omnimud.Core.Options;
using Omnimud.Core.Reports;
using Omnimud.Core.Storage;
using Omnimud.Data;
using Omnimud.Data.Migrations;
using Omnimud.UI.Forms;
using Omnimud.UI.Services;

namespace Omnimud.UI;

internal static class Program
{
    // Must be synchronous: with "async Task Main" the compiler-generated entry point loses
    // [STAThread] and the UI thread ends up MTA, which breaks clipboard, common dialogs and
    // the COM plumbing screen readers rely on.
    [STAThread]
    private static void Main()
    {
        // windows-1252, iso-8859-1... are not available in modern .NET without this.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ErrorReporter.Show(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            // The thread that failed is about to die: wait until the user has seen the dialog.
            if (e.ExceptionObject is Exception ex) ErrorReporter.ShowAndWait(ex);
        };
        // Until the services exist the dialog has no report button; see below.
        ErrorReporter.Configure(report: null);

        var legacyDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Omnimud");
        var paths = AppPaths.Resolve(AppContext.BaseDirectory, legacyDirectory);
        paths.ImportLegacyDataFrom(legacyDirectory);

        var services = new ServiceCollection();
        ServiceConfigurator.ConfigureServices(services, paths);

        using var provider = services.BuildServiceProvider();

        // Run database migrations before showing UI
        var factory = provider.GetRequiredService<IDbConnectionFactory>();
        using (var connection = factory.Create())
        {
            Task.Run(() => MigrationRunner.RunAsync(connection)).GetAwaiter().GetResult();
        }

        // Language of the interface, before any window (or localized message) is created.
        new LanguageService().ApplyFromOptions(provider.GetRequiredService<IOptionsService>());

        // From here on "Report this error" opens the report dialog with the exception.
        var appDialogs = provider.GetRequiredService<IAppDialogs>();
        ErrorReporter.Configure((exception, _) => appDialogs.ShowReport(Form.ActiveForm, ReportKind.Error, exception));

        Application.Run(provider.GetRequiredService<FrmLauncher>());
    }
}
