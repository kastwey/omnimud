using Microsoft.Extensions.DependencyInjection;
using Omnimud.Core.Connection;
using Omnimud.Core.Options;
using Omnimud.Core.Security;
using Omnimud.Core.Session;
using Omnimud.Core.Sound;
using Omnimud.Core.Storage;
using Omnimud.Data;
using Omnimud.Data.Exchange;
using Omnimud.Data.Options;
using Omnimud.Data.Repositories;
using Omnimud.Data.Session;
using Omnimud.UI.Forms;
using Omnimud.UI.Services.Audio;

namespace Omnimud.UI.Services;

internal static class ServiceConfigurator
{
    /// <summary>
    /// Only stateless or application-wide services live here. Everything with per-connection
    /// state (connection, telnet, triggers, scripts, sound, log) is created per session by
    /// <see cref="GameWindowFactory"/>.
    /// </summary>
    public static void ConfigureServices(IServiceCollection services, AppPaths paths)
    {
        services.AddSingleton(paths);
        services.AddSingleton(TimeProvider.System);

        // Data layer
        services.AddSingleton<IDbConnectionFactory>(_ => new SqliteConnectionFactory(paths.DatabasePath));
        services.AddSingleton<IMudRepository, SqliteMudRepository>();
        services.AddSingleton<ICharacterRepository, SqliteCharacterRepository>();
        services.AddSingleton<IAliasRepository, SqliteAliasRepository>();
        services.AddSingleton<ITriggerRepository, SqliteTriggerRepository>();
        services.AddSingleton<IPathRepository, SqlitePathRepository>();
        services.AddSingleton<IDirectionRepository, SqliteDirectionRepository>();
        services.AddSingleton<IMovementRepository, SqliteMovementRepository>();
        services.AddSingleton<IMessageRuleRepository, SqliteMessageRuleRepository>();
        services.AddSingleton<IOptionRepository, SqliteOptionRepository>();

        services.AddSingleton<OptionsService>();
        services.AddSingleton<IOptionsService>(sp => sp.GetRequiredService<OptionsService>());
        services.AddSingleton<ISessionStore, SessionStore>();
        services.AddSingleton<IExchangeService>(sp =>
            new ExchangeService(sp.GetRequiredService<IDbConnectionFactory>(), sp.GetRequiredService<OptionsService>()));

        services.AddSingleton<IPasswordProtector>(_ => new AesPasswordProtector(MasterKeyProvider.GetOrCreate(paths.MasterKeyPath)));

        // Proxy: one place decides what the MUD connection and the downloads go through.
        services.AddSingleton<IProxyCredentialStore, ProxyCredentialStore>();
        services.AddSingleton<ISystemProxySource, WindowsSystemProxySource>();
        services.AddSingleton<ISystemProxyResolver, SystemProxyResolver>();
        services.AddSingleton<IProxySettingsResolver, ProxySettingsResolver>();

        // Sound: one audio device for the whole application, one SessionSound per session.
        services.AddSingleton<HttpClient>();
        services.AddSingleton<ISoundDownloader, HttpSoundDownloader>();
        services.AddSingleton<ISoundPlayer>(_ => new NAudioSoundPlayer());

        // Sessions and windows
        services.AddSingleton(MudSessionSettings.FromAppPaths(paths));
        // Message boxes and file pickers behind an interface; owned by whatever form is active.
        services.AddSingleton<IUserPrompts>(_ => new WinFormsUserPrompts());
        services.AddSingleton<ISessionDialogs, SessionDialogs>();
        services.AddSingleton<IGameWindowFactory, GameWindowFactory>();
        services.AddTransient<FrmLauncher>();
    }
}
