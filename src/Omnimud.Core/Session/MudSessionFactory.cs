using Omnimud.Core.Actions;
using Omnimud.Core.Connection;
using Omnimud.Core.Options;
using Omnimud.Core.Scripting;
using Omnimud.Core.Sound;
using Omnimud.Core.Storage;

namespace Omnimud.Core.Session;

/// <summary>Folders and delays of a session. The delays exist so tests never wait for real.</summary>
public sealed record MudSessionSettings
{
    /// <summary>Default logs folder (data\logs); Options.LogDirectory overrides it.</summary>
    public required string LogDirectory { get; init; }

    /// <summary>Root of the per-MUD sound folders (data\sounds), used when the profile has no folder of its own.</summary>
    public required string SoundsDirectory { get; init; }

    /// <summary>Sounds shipped with the application.</summary>
    public required string AppSoundsDirectory { get; init; }

    /// <summary>Pause after sending save + quit on close, so the MUD can answer before the socket goes away.</summary>
    public TimeSpan QuitGraceDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Pause between the lines of the login script.</summary>
    public TimeSpan LoginLineDelay { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Scripts run in the background, but one that may call om.gag has to decide before its line
    /// is shown. This is the longest (real) time a line waits for such a script.
    /// </summary>
    public TimeSpan ScriptGagWait { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>What turns packages of the MUD into sections of the actions menu, in menu order.
    /// Another MUD with other packages only needs its translator added here.</summary>
    public IReadOnlyList<IActionMenuTranslator> ActionMenuTranslators { get; init; } = Actions.ActionMenuTranslators.Default;

    public static MudSessionSettings FromAppPaths(AppPaths paths) => new()
    {
        LogDirectory = paths.LogsDirectory,
        SoundsDirectory = paths.SoundsDirectory,
        AppSoundsDirectory = paths.AppSoundsDirectory
    };
}

public interface IMudSessionFactory
{
    /// <summary>A new session with its own connection, sound and script engine. Call InitializeAsync next.</summary>
    IMudSession Create(SessionProfile profile);
}

/// <summary>
/// Builds sessions. Store and options service are shared by the application; connection, sound
/// and script engine are created per session (the session owns and disposes them).
/// </summary>
public sealed class MudSessionFactory : IMudSessionFactory
{
    private readonly ISessionStore _store;
    private readonly IOptionsService _options;
    private readonly Func<IConnection> _connectionFactory;
    private readonly Func<ISessionSound> _soundFactory;
    private readonly Func<IScriptEngine> _scriptEngineFactory;
    private readonly TimeProvider _time;
    private readonly MudSessionSettings _settings;
    private readonly IProxySettingsResolver? _proxySettings;

    public MudSessionFactory(
        ISessionStore store,
        IOptionsService options,
        Func<IConnection> connectionFactory,
        Func<ISessionSound> soundFactory,
        Func<IScriptEngine> scriptEngineFactory,
        TimeProvider time,
        MudSessionSettings settings,
        IProxySettingsResolver? proxySettings = null)
    {
        _store = store;
        _options = options;
        _connectionFactory = connectionFactory;
        _soundFactory = soundFactory;
        _scriptEngineFactory = scriptEngineFactory;
        _time = time;
        _settings = settings;
        _proxySettings = proxySettings;
    }

    public IMudSession Create(SessionProfile profile)
        => new MudSession(profile, _connectionFactory(), _store, _options, _soundFactory(), _scriptEngineFactory(), _time, _settings, _proxySettings);
}
