using System.Globalization;
using Omnimud.Core.Connection;
using Omnimud.Core.Options;
using Omnimud.Core.Scripting;
using Omnimud.Core.Session;
using Omnimud.Core.Sound;
using Omnimud.UI.Forms;

namespace Omnimud.UI.Services;

public interface IGameWindowFactory
{
    /// <summary>Creates the session and its window. The window owns the session and disposes it on close.</summary>
    Form Create(SessionProfile profile);
}

/// <summary>
/// Composition root of one game session. Connection, sound and script engine are created per
/// session (never shared), so several windows can be open at once without mixing state.
/// </summary>
internal sealed class GameWindowFactory(
    ISessionStore store,
    IOptionsService options,
    ISoundPlayer player,
    ISoundDownloader downloader,
    ISessionDialogs dialogs,
    MudSessionSettings settings,
    TimeProvider time,
    IProxySettingsResolver? proxySettings = null,
    IAppDialogs? appDialogs = null) : IGameWindowFactory
{
    public Form Create(SessionProfile profile)
    {
        // The session inherits the UI culture of the thread that creates it for its own messages.
        if (Resources.Strings.Culture is { } culture)
            CultureInfo.CurrentUICulture = culture;

        var sound = new SessionSound(player, downloader);
        var session = new MudSession(profile, new TelnetConnection(), store, options, sound,
            new LuaScriptEngine(time), time, settings, proxySettings);
        return new FrmGame(session, sound, dialogs, time, announcer: null, appDialogs);
    }
}
