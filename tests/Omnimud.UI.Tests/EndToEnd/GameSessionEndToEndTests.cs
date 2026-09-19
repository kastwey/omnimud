using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Omnimud.Core.Options;
using Omnimud.Core.Session;
using Omnimud.Core.Storage;
using Omnimud.Data;
using Omnimud.Data.Entities;
using Omnimud.Data.Migrations;
using Omnimud.Data.Repositories;
using Omnimud.UI.Controls;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;

namespace Omnimud.UI.Tests.EndToEnd;

/// <summary>
/// The real application wiring (DI container, SQLite file, session, window) against a fake MUD
/// on a local TCP port. Guards against the failure mode of the previous rewrite: engines that
/// existed and were tested but were never connected to the game window.
/// </summary>
public sealed class GameSessionEndToEndTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "omnimud-e2e-" + Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider _provider;
    private readonly FakeMud _mud = new();
    private int _mudId;
    private int _characterId;

    public GameSessionEndToEndTests()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("es");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        Directory.CreateDirectory(_dir);
        var paths = AppPaths.Resolve(_dir, Path.Combine(_dir, "fallback"));
        var services = new ServiceCollection();
        ServiceConfigurator.ConfigureServices(services, paths);
        _provider = services.BuildServiceProvider();

        using var connection = _provider.GetRequiredService<IDbConnectionFactory>().Create();
        MigrationRunner.RunAsync(connection).GetAwaiter().GetResult();
        Seed().GetAwaiter().GetResult();
    }

    private async Task Seed()
    {
        // The close confirmation is a modal dialog nobody would answer here. Also proves the window honours options.
        await _provider.GetRequiredService<IOptionsService>().SaveAsync(OptionScope.Global, null,
            OmnimudOptions.Default with { ConfirmBeforeExit = false, LogType = LogMode.PerSession });

        _mudId = await _provider.GetRequiredService<IMudRepository>().AddAsync(new MudEntity
        {
            Name = "MudDePrueba", Host = "127.0.0.1", Port = _mud.Port, Encoding = "iso-8859-1",
        });
        _characterId = await _provider.GetRequiredService<ICharacterRepository>().AddAsync(new CharacterEntity
        {
            MudId = _mudId, Name = "Aldara",
        });
        await _provider.GetRequiredService<IAliasRepository>().AddAsync(new AliasEntity
        {
            CharacterId = _characterId, Command = "m", Action = "mirar", Enabled = true,
        });
        await _provider.GetRequiredService<ITriggerRepository>().AddAsync(new TriggerEntity
        {
            Id = Guid.NewGuid().ToString(), CharacterId = _characterId, Name = "saludo",
            Pattern = "%s te saluda.", PatternType = 2, Action = "saludar %1", ActionType = 0, Enabled = true,
        });
    }

    private SessionProfile Profile() => new()
    {
        Title = "Aldara - MudDePrueba", Host = "127.0.0.1", Port = _mud.Port, Encoding = "iso-8859-1",
        MudId = _mudId, MudName = "MudDePrueba", CharacterId = _characterId, CharacterName = "Aldara",
    };

    [Fact]
    public void ConnectedWindow_ShowsMudText_ExpandsAliases_FiresTriggers_AndKeepsDataPortable() => Sta.Run(() =>
    {
        using var window = _provider.GetRequiredService<IGameWindowFactory>().Create(Profile());
        window.Show();

        // Auto-login without login script: the character name is sent on connect, as the original did.
        PumpUntil(() => _mud.Received.Contains("Aldara"), "el nombre del personaje llega al MUD");

        _mud.Send("[1;32mEstás en la plaza de Añil.[0m\r\n");
        var terminal = (AnsiTerminalBox)window.Controls.Find("_terminal", true).Single();
        PumpUntil(() => terminal.Text.Contains("Estás en la plaza de Añil."), "el texto del MUD se pinta, decodificado y sin códigos ANSI");
        terminal.Text.Should().NotContain("");

        var input = (TextBox)window.Controls.Find("_txtInput", true).Single();
        input.Text = "m norte";
        ((Omnimud.UI.Forms.FrmGame)window).SubmitInput();
        PumpUntil(() => _mud.Received.Contains("mirar norte"), "el alias 'm' se expande antes de enviar");

        _mud.Send("Gandalf te saluda.\r\n");
        PumpUntil(() => _mud.Received.Contains("saludar Gandalf"), "el trigger con comodín responde con su captura");

        // GMCP: the MUD offers it (IAC WILL 201), the client accepts and subscribes to channels, and a
        // Comm.Channel.Text package lands in the Messages box without any message rule configured.
        _mud.Send("ÿûÉ");
        PumpUntil(() => _mud.Received.Contains("Core.Supports.Set [\"Comm.Channel 1\",\"Char.Inventory 1\",\"Room.Info 1\"]"), "el cliente acepta GMCP y se suscribe a canales, inventario y sala");
        _mud.Send("ÿúÉComm.Channel.Text {\"channel\":\"chat\",\"talker\":\"Bob\",\"text\":\"hola desde gmcp\"}ÿð");
        var messages = (AnsiTerminalBox)window.Controls.Find("_rtbMessages", true).Single();
        PumpUntil(() => messages.Text.Contains("[chat] Bob: hola desde gmcp"), "el mensaje GMCP llega al cuadro de Mensajes");
        terminal.Text.Should().NotContain("Comm.Channel", "GMCP travels out of band and never shows up as text");

        // Actions menu: the MUD describes the inventory (a leaf and a group) and the room; the window grows an
        // "Acciones" menu with both sections, and choosing an entry sends its cmd as if it had been typed.
        var game = (Omnimud.UI.Forms.FrmGame)window;
        var actionsMenu = (ToolStripMenuItem)window.MainMenuStrip!.Items["_miActions"]!;
        actionsMenu.Available.Should().BeFalse("the MUD has not offered any action yet");
        window.MainMenuStrip.Items.Cast<ToolStripItem>().Select(i => i.Text).Take(2).Should().Equal("&Acciones", "&Partida");
        _mud.SendGmcp("Char.Inventory", """
            { "items": [
                { "id": "espada", "short": "una espada larga", "actions": [
                    { "action": "dejar", "label": "Dejar", "cmd": "dejar espada" },
                    { "action": "examinar", "label": "Examinar", "cmd": "examinar espada" } ] },
                { "id": "pocion", "short": "poción (2)", "children": [
                    { "id": "pocion", "short": "poción (1)", "actions": [ { "action": "beber", "label": "Beber", "cmd": "beber pocion 1" } ] },
                    { "id": "pocion", "short": "poción (2)", "actions": [ { "action": "beber", "label": "Beber", "cmd": "beber pocion 2" } ] } ] } ] }
            """);
        _mud.SendGmcp("Room.Info", """{ "id": "/room/plaza", "short": "Plaza de Añil", "long": "Una plaza.", "exits": [ "este", "oeste" ], "items": [ "una fuente" ] }""");
        PumpUntil(() => actionsMenu.Available, "el menú Acciones aparece cuando el MUD ofrece acciones");
        ToolStripMenuItem Entry(ToolStripMenuItem parent, string text) => parent.DropDownItems.OfType<ToolStripMenuItem>().Single(i => i.Text == text);
        PumpUntil(() =>
        {
            game.FillActionsMenu(); // what opening the menu does
            return actionsMenu.DropDownItems.Count == 2;
        }, "el menú Acciones tiene Inventario y Salidas");
        actionsMenu.DropDownItems.Cast<ToolStripItem>().Select(i => i.Text).Should().Equal("Inventario", "Salidas");
        Entry(actionsMenu, "Inventario").DropDownItems.Cast<ToolStripItem>().Select(i => i.Text).Should().Equal("una espada larga", "poción (2)");
        Entry(Entry(actionsMenu, "Inventario"), "una espada larga").DropDownItems.Cast<ToolStripItem>().Select(i => i.Text).Should().Equal("Dejar", "Examinar");
        Entry(Entry(Entry(Entry(actionsMenu, "Inventario"), "poción (2)"), "poción (2)"), "Beber").PerformClick();
        PumpUntil(() => SentLines().Contains("beber pocion 2"), "la acción de la segunda poción del grupo envía su cmd como línea completa");
        Entry(Entry(actionsMenu, "Salidas"), "oeste").PerformClick();
        PumpUntil(() => SentLines().Contains("oeste"), "elegir una salida la envía");
        PumpUntil(() => window.ActiveControl == input, "tras elegir una acción el foco vuelve al cuadro de envío");
        terminal.Text.Should().NotContain("Char.Inventory").And.NotContain("Room.Info");
        input.Text.Should().BeEmpty("an action never goes through the input box");

        // Movement mode (F2) works out of the box: on an empty input box the arrows walk.
        game.HandleInputKey(Keys.Up);
        input.Text.Should().Be("m norte", "with movement mode off the up arrow recalls the last command");
        input.Clear();
        var movementItem = AllMenuItems(window.MainMenuStrip!).Single(i => i.ShortcutKeys == Keys.F2);
        movementItem.PerformClick();
        PumpUntil(() => movementItem.Checked, "F2 activa el modo movimiento");
        input.Clear();
        game.HandleInputKey(Keys.Up).Should().BeTrue();
        PumpUntil(() => SentLines().Contains("norte"), "la flecha arriba envía 'norte' sin haber configurado nada");
        game.HandleInputKey(Keys.PageDown).Should().BeTrue();
        PumpUntil(() => SentLines().Contains("abajo"), "Av Pág envía 'abajo'");
        input.Text.Should().BeEmpty("movement keys never touch the input box or the history");
        input.Text = "texto a medias";
        game.HandleInputKey(Keys.Left).Should().BeFalse("with text in the box the arrows edit it");
        input.Clear();

        File.Exists(Path.Combine(_dir, "data", "omnimud.db")).Should().BeTrue("all user data lives next to the executable");

        Directory.GetFiles(Path.Combine(_dir, "data", "logs"), "*.log", SearchOption.AllDirectories).Should().NotBeEmpty("the session log is written inside the portable data folder");

        window.Close();
        PumpUntil(() => window.IsDisposed, "la ventana termina de cerrarse tras desconectar");
    });

    /// <summary>Whole lines received by the fake MUD, so "norte" is not satisfied by "mirar norte".</summary>
    private string[] SentLines() =>
        System.Text.RegularExpressions.Regex.Replace(_mud.Received, "\u00FF\u00FA.*?\u00FF\u00F0|\u00FF[\u00FB-\u00FE].", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline)
            .Split('\n').Select(l => l.Trim('\r', ' ')).ToArray();

    private static IEnumerable<ToolStripMenuItem> AllMenuItems(MenuStrip strip)
    {
        var pending = new Stack<ToolStripMenuItem>(strip.Items.OfType<ToolStripMenuItem>());
        while (pending.Count > 0)
        {
            var item = pending.Pop();
            yield return item;
            foreach (var child in item.DropDownItems.OfType<ToolStripMenuItem>()) pending.Push(child);
        }
    }

    [Fact]
    public void TheContainer_BuildsTheLauncher_WithEveryDialogDependencyResolved() => Sta.Run(() =>
    {
        // Catches a missing registration at test time instead of at the user's first click.
        using var launcher = _provider.GetRequiredService<Omnimud.UI.Forms.FrmLauncher>();
        launcher.Should().NotBeNull();
        _provider.GetRequiredService<ISessionDialogs>().Should().NotBeNull();
        Omnimud.UI.Tests.Accessibility.AccessibilityAudit.Check(launcher, isDialog: false).Should().BeEmpty();
    });

    private static void PumpUntil(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("No ocurrió a tiempo: " + what);
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    public void Dispose()
    {
        _mud.Dispose();
        _provider.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Accepts one client, records what it sends (Latin-1) and lets the test send text.</summary>
    private sealed class FakeMud : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly StringBuilder _received = new();
        private readonly CancellationTokenSource _cts = new();
        private NetworkStream? _stream;

        public FakeMud()
        {
            _listener.Start();
            _ = AcceptAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public string Received
        {
            get { lock (_received) return _received.ToString(); }
        }

        private async Task AcceptAsync()
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _stream = client.GetStream();
                var buffer = new byte[4096];
                int read;
                while ((read = await _stream.ReadAsync(buffer, _cts.Token)) > 0)
                    lock (_received) _received.Append(Encoding.Latin1.GetString(buffer, 0, read));
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException or SocketException)
            {
            }
        }

        /// <summary>A GMCP package, in UTF-8 as the protocol says, whatever the encoding of the MUD's text.</summary>
        public void SendGmcp(string package, string payload)
            => Send([255, 250, 201, .. Encoding.UTF8.GetBytes(package + " " + payload), 255, 240]);

        public void Send(string text) => Send(Encoding.Latin1.GetBytes(text));

        private void Send(byte[] bytes)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (_stream is null && DateTime.UtcNow < deadline) Thread.Sleep(10);
            _stream!.Write(bytes, 0, bytes.Length);
        }

        public void Dispose()
        {
            _cts.Cancel();
            _stream?.Dispose();
            _listener.Stop();
        }
    }
}
