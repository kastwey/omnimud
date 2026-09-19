using Microsoft.Extensions.DependencyInjection;
using Omnimud.Core.Options;
using Omnimud.Core.Reports;
using Omnimud.Core.Storage;
using Omnimud.Core.Updates;
using Omnimud.Data;
using Omnimud.Data.Entities;
using Omnimud.Data.Migrations;
using Omnimud.Data.Repositories;
using Omnimud.UI.Presenters;
using Omnimud.UI.Services;

namespace Omnimud.UI.Tests.Services;

/// <summary>The application's own container wires the phase 7 pieces, over a real database. Nothing here goes to the network or opens anything.</summary>
public sealed class ServiceConfiguratorPhase7Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "omnimud-di-f7-" + Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider _provider;

    public ServiceConfiguratorPhase7Tests()
    {
        Directory.CreateDirectory(_dir);
        var services = new ServiceCollection();
        ServiceConfigurator.ConfigureServices(services, AppPaths.Resolve(_dir, Path.Combine(_dir, "fallback")));
        _provider = services.BuildServiceProvider();
        using var connection = _provider.GetRequiredService<IDbConnectionFactory>().Create();
        MigrationRunner.RunAsync(connection).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _provider.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void EveryPiece_CanBeResolved()
    {
        _provider.GetRequiredService<IUpdateChecker>().Should().BeOfType<ProxyAwareUpdateChecker>();
        _provider.GetRequiredService<IAppDialogs>().Should().BeOfType<AppDialogs>();
        _provider.GetRequiredService<IAppDialogs>().Help.Should().BeOfType<HelpService>();
        _provider.GetRequiredService<IExternalLauncher>().Should().BeOfType<ShellExternalLauncher>();
        _provider.GetRequiredService<IClipboardService>().Should().BeOfType<WinFormsClipboard>();
        _provider.GetRequiredService<IPersonalInfoStore>().Should().BeOfType<OptionPersonalInfoStore>();
        _provider.GetRequiredService<IGameWindowFactory>().Should().NotBeNull("the game windows get the application dialogs from the container");
    }

    [Fact]
    public async Task AFreshInstallation_DoesNotCheckForUpdatesOnStartup()
    {
        var options = await _provider.GetRequiredService<IOptionsService>().ResolveAsync(null, null);
        options.CheckUpdatesOnStartup.Should().BeFalse();
    }

    [Fact]
    public async Task PersonalInformation_IsStoredInTheDatabase_AwayFromTheOptions()
    {
        var store = _provider.GetRequiredService<IPersonalInfoStore>();
        await store.SaveAsync(new PersonalInfo("María", "maria@example.org"));

        (await store.LoadAsync()).Should().Be(new PersonalInfo("María", "maria@example.org"));
        (await _provider.GetRequiredService<IOptionsService>().HasOwnOptionsAsync(OptionScope.Global, null)).Should().BeFalse();
    }

    [Fact]
    public async Task TheReportDialogOfTheApplication_KnowsWhatMustNeverAppear()
    {
        var muds = _provider.GetRequiredService<IMudRepository>();
        var characters = _provider.GetRequiredService<ICharacterRepository>();
        var mudId = await muds.AddAsync(new MudEntity { Name = "Reinos de Prueba", Host = "mud.privado.example", Port = 4000, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await characters.AddAsync(new CharacterEntity { MudId = mudId, Name = "Aldarion", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await _provider.GetRequiredService<IOptionsService>().SaveAsync(OptionScope.Global, null,
            new OmnimudOptions { ProxyType = ProxyMode.Manual, ProxyHost = "proxy.privado.example", ProxyPort = 3128, ProxyUsername = "usuarioproxy" });

        var terms = await _provider.GetRequiredService<IPrivateTermsProvider>().GetAsync();
        terms.Should().Contain(["mud.privado.example", "Reinos de Prueba", "Aldarion", "proxy.privado.example", "usuarioproxy"]);

        // The real composition: an exception that names all of them, as a connection error would.
        var dialogs = (AppDialogs)_provider.GetRequiredService<IAppDialogs>();
        var presenter = dialogs.CreateReportPresenter(ReportKind.Error,
            new InvalidOperationException("No se pudo conectar a mud.privado.example:4000 (Reinos de Prueba) como Aldarion por proxy.privado.example con usuarioproxy"));

        var everything = presenter.Title + "\n" + presenter.Preview;
        foreach (var term in terms)
            everything.Should().NotContainEquivalentOf(term);
        everything.Should().Contain("InvalidOperationException").And.Contain(AppInfo.Version);
    }
}
