using Microsoft.Extensions.DependencyInjection;
using Omnimud.Core.Connection;
using Omnimud.Core.Options;
using Omnimud.Core.Security;
using Omnimud.Core.Sound;
using Omnimud.Core.Storage;
using Omnimud.Data;
using Omnimud.Data.Migrations;
using Omnimud.UI.Services;

namespace Omnimud.UI.Tests.Services;

/// <summary>The application's own container wires the proxy pieces together, over a real database and a real master key.</summary>
public sealed class ServiceConfiguratorProxyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "omnimud-di-proxy-" + Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider _provider;

    public ServiceConfiguratorProxyTests()
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
    public async Task APasswordSavedThroughTheOptionsService_ComesBackForTheMudAndForDownloads_AndIsNotInTheDatabaseInClear()
    {
        const string password = "s3cret-en-la-base-de-datos";
        var credentials = _provider.GetRequiredService<IProxyCredentialStore>();
        var options = _provider.GetRequiredService<IOptionsService>();
        await options.SaveAsync(OptionScope.Global, null, credentials.WithPassword(OmnimudOptions.Default with
        {
            ProxyType = ProxyMode.Manual, ProxyHost = "proxy.corp", ProxyPort = 3128, ProxyProtocol = ProxyProtocol.HttpConnect,
            ProxyUsername = "juan", UseProxyForMud = true
        }, password));

        var resolver = _provider.GetRequiredService<IProxySettingsResolver>();
        var resolved = await options.ResolveAsync(null, null);

        resolver.ForMud(resolved, "mud.example.org", 4000)
            .Should().Be(new ProxyConfig("proxy.corp", 3128, ProxyProtocol.HttpConnect, "juan", password));
        resolver.ForDownloads(resolved)
            .Should().Be(new DownloadProxySettings(DownloadProxyKind.Manual, new Uri("http://proxy.corp:3128"), "juan", password));

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var database = await File.ReadAllBytesAsync(_provider.GetRequiredService<AppPaths>().DatabasePath);
        System.Text.Encoding.UTF8.GetString(database).Should().NotContain(password);
        System.Text.Encoding.Unicode.GetString(database).Should().NotContain(password);
    }

    [Fact]
    public void TheContainer_BuildsTheRealPieces_AndTheWindowsThatUseThem()
    {
        _provider.GetRequiredService<IProxySettingsResolver>().Should().BeOfType<ProxySettingsResolver>();
        _provider.GetRequiredService<ISystemProxyResolver>().Should().BeOfType<SystemProxyResolver>();
        _provider.GetRequiredService<ISystemProxySource>().Should().BeOfType<WindowsSystemProxySource>();
        _provider.GetRequiredService<IProxyCredentialStore>().Should().BeOfType<ProxyCredentialStore>();
        _provider.GetRequiredService<IGameWindowFactory>().Should().NotBeNull();
        _provider.GetRequiredService<ISessionDialogs>().Should().NotBeNull();
    }
}
