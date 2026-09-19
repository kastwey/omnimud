using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Omnimud.Core.Options;
using Omnimud.Core.Session;
using Omnimud.Core.Sound;
using Omnimud.Core.Text;
using Omnimud.UI.Controls;
using Omnimud.UI.Forms;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;
using Omnimud.UI.Services.Accessibility;
using Omnimud.UI.Tests.Accessibility;

namespace Omnimud.UI.Tests.Forms;

public sealed class FrmGameTests
{
    private readonly IMudSession _session = Substitute.For<IMudSession>();
    private readonly ISessionSound _sound = Substitute.For<ISessionSound>();
    private readonly ISessionDialogs _dialogs = Substitute.For<ISessionDialogs>();
    private readonly IAnnouncer _announcer = Substitute.For<IAnnouncer>();
    private readonly FakeTimeProvider _time = new();
    private readonly List<SessionMessage> _messages = [];
    private readonly List<string> _history = [];

    public FrmGameTests()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("es");
        _session.Profile.Returns(new SessionProfile
        {
            Title = "Reinos", Host = "mud.example.org", Port = 23,
            MudId = 1, MudName = "Reinos", CharacterId = 7, CharacterName = "Aldara",
            SaveCommand = "salvar", QuitCommand = "abandonar",
        });
        _session.Options.Returns(OmnimudOptions.Default with { ConfirmBeforeExit = false });
        _session.State.Returns(SessionState.Connected);
        _session.Messages.Returns(_messages);
        _session.History.Returns(_history);
        _session.ActionMenu.Returns(Omnimud.Core.Actions.ActionMenu.Empty);
    }

    private FrmGame Create() => new(_session, _sound, _dialogs, _time, _announcer);

    private static T Get<T>(Form form, string name) where T : Control =>
        (T)form.Controls.Find(name, searchAllChildren: true).Single();

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void Window_PassesTheAccessibilityAudit_InEveryLanguage(string culture) => Sta.Run(() =>
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);
        using var form = Create();

        AccessibilityAudit.Check(form, isDialog: false).Should().BeEmpty();
    });

    [Fact]
    public void EveryBox_HasAVisibleLabelWithMnemonic_AndAnAccessibleName() => Sta.Run(() =>
    {
        using var form = Create();

        Get<Label>(form, "_lblInput").Text.Should().Be("&Texto a enviar:");
        Get<TextBox>(form, "_txtInput").AccessibleName.Should().Be("Texto a enviar");
        Get<TextBox>(form, "_txtInput").AccessibleDescription.Should().BeNullOrEmpty("a description is read aloud every time the box gets the focus");
        Get<Label>(form, "_lblOutput").Text.Should().Be("&Recibido:");
        Get<AnsiTerminalBox>(form, "_terminal").AccessibleName.Should().Be("Recibido");
        Get<Label>(form, "_lblMessages").Text.Should().Be("&Mensajes:");
        Get<AnsiTerminalBox>(form, "_rtbMessages").AccessibleName.Should().Be("Mensajes");
    });

    [Fact]
    public void TabOrder_IsInput_Received_Messages_Buttons() => Sta.Run(() =>
    {
        using var form = Create();
        int Tab(string name) => Get<Control>(form, name).TabIndex;

        Tab("_txtInput").Should().BeLessThan(Tab("_terminal"));
        Tab("_terminal").Should().BeLessThan(Tab("_rtbMessages"));
        Tab("_rtbMessages").Should().BeLessThan(Tab("_buttons"));
    });

    [Fact]
    public void EveryShortcutOfTheOriginalClient_IsAMenuItem() => Sta.Run(() =>
    {
        using var form = Create();
        var shortcuts = AllMenuItems(form.MainMenuStrip!).Select(i => i.ShortcutKeys).ToHashSet();

        shortcuts.Should().Contain([
            Keys.F1, Keys.F2, Keys.F3, Keys.F4, Keys.F5, Keys.F6, Keys.F7, Keys.F8, Keys.F9,
            Keys.Control | Keys.B, Keys.Control | Keys.S, Keys.Control | Keys.E,
            Keys.Control | Keys.C, Keys.Control | Keys.X, Keys.Control | Keys.V, Keys.Control | Keys.Z,
            // added by v2 and kept
            Keys.Control | Keys.L, Keys.Control | Keys.K, Keys.Control | Keys.M, Keys.Alt | Keys.S]);
    });

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
    public void ActionButton_TextAndAccessibleName_FollowTheSessionState() => Sta.Run(() =>
    {
        _session.State.Returns(SessionState.Connecting);
        using var form = Create();
        form.CreateControl();
        var button = Get<Button>(form, "_btnAction");
        button.Text.Should().Be("&Cancelar");
        button.AccessibleName.Should().Be("Cancelar");

        _session.StateChanged += Raise.Event<Action<SessionState>>(SessionState.Connected);
        button.Text.Should().Be("&Desconectar");
        button.AccessibleName.Should().Be("Desconectar");
        button.AccessibleDescription.Should().NotBeNullOrEmpty();

        _session.StateChanged += Raise.Event<Action<SessionState>>(SessionState.Offline);
        button.Text.Should().Be("&Cerrar");
        button.AccessibleName.Should().Be("Cerrar");
    });

    [Fact]
    public void Disconnected_DisablesInput_Connected_EnablesIt() => Sta.Run(() =>
    {
        using var form = Create();
        form.CreateControl();
        var input = Get<TextBox>(form, "_txtInput");
        input.Enabled.Should().BeTrue();

        _session.StateChanged += Raise.Event<Action<SessionState>>(SessionState.Disconnected);
        input.Enabled.Should().BeFalse();
    });

    [Fact]
    public void PasswordMode_MasksInput_AndRenamesIt_ThenRestores() => Sta.Run(() =>
    {
        using var form = Create();
        form.CreateControl();
        var input = Get<TextBox>(form, "_txtInput");

        _session.PasswordModeChanged += Raise.Event<Action<bool>>(true);
        input.UseSystemPasswordChar.Should().BeTrue();
        input.Multiline.Should().BeFalse("a multiline TextBox ignores the password character");
        input.AccessibleName.Should().Be("Contraseña");

        _session.PasswordModeChanged += Raise.Event<Action<bool>>(false);
        input.UseSystemPasswordChar.Should().BeFalse();
        input.Multiline.Should().BeTrue();
        input.AccessibleName.Should().Be("Texto a enviar");
    });

    [Fact]
    public void CtrlDigit_SpeaksThatMessage_Interrupting() => Sta.Run(() =>
    {
        _messages.Add(new SessionMessage(1, DateTime.Now, "Ana te dice: hola"));
        _messages.Add(new SessionMessage(2, DateTime.Now, "Luis te dice: adios"));
        using var form = Create();

        form.HandleReviewKey(Keys.Control | Keys.D1).Should().BeTrue();
        _time.Advance(TimeSpan.FromSeconds(1));
        form.HandleReviewKey(Keys.Control | Keys.NumPad2).Should().BeTrue();

        Received.InOrder(() =>
        {
            _announcer.Announce("1: Luis te dice: adios", AnnouncePriority.Interrupt);
            _announcer.Announce("2: Ana te dice: hola", AnnouncePriority.Interrupt);
        });
    });

    [Fact]
    public void AltGrDigit_IsNotTakenAsMessageReview() => Sta.Run(() =>
    {
        using var form = Create();

        form.HandleReviewKey(Keys.Control | Keys.Alt | Keys.D2).Should().BeFalse();

        _announcer.DidNotReceiveWithAnyArgs().Announce(default!, default);
    });

    [Fact]
    public void CtrlOem5_WalksMessagesBackwards() => Sta.Run(() =>
    {
        _messages.Add(new SessionMessage(1, DateTime.Now, "primero"));
        _messages.Add(new SessionMessage(2, DateTime.Now, "segundo"));
        using var form = Create();

        form.HandleReviewKey(Keys.Control | Keys.Oem5);
        form.HandleReviewKey(Keys.Control | Keys.Oem5);

        Received.InOrder(() =>
        {
            _announcer.Announce("1: segundo", AnnouncePriority.Interrupt);
            _announcer.Announce("2: primero", AnnouncePriority.Interrupt);
        });
    });

    [Fact]
    public void SubmitInput_SendsTheTextToTheSession_AndClearsTheBox() => Sta.Run(() =>
    {
        using var form = Create();
        var input = Get<TextBox>(form, "_txtInput");
        input.Text = "mirar";

        form.SubmitInput();

        _session.Received(1).SubmitInputAsync("mirar");
        input.Text.Should().BeEmpty();
    });

    [Fact]
    public void SubmitInput_Empty_IsForwarded_SoTheSessionRepeatsTheLastCommand() => Sta.Run(() =>
    {
        using var form = Create();

        form.SubmitInput();

        _session.Received(1).SubmitInputAsync(string.Empty);
    });

    [Fact]
    public void History_UpGoesToOlder_DownComesBack_AndPastTheNewestClears() => Sta.Run(() =>
    {
        _history.AddRange(["norte", "sur", "mirar"]);
        using var form = Create();
        var input = Get<TextBox>(form, "_txtInput");

        form.BrowseHistory(-1);
        input.Text.Should().Be("mirar");
        input.SelectionLength.Should().Be(5, "the recalled command is selected so the screen reader reads it");
        form.BrowseHistory(-1);
        input.Text.Should().Be("sur");
        form.BrowseHistory(+1);
        input.Text.Should().Be("mirar");
        form.BrowseHistory(+1);
        input.Text.Should().BeEmpty();

        _sound.Received(4).PlayUiSound("click");
    });

    [Fact]
    public void History_Up_StopsAtTheOldest() => Sta.Run(() =>
    {
        _history.AddRange(["norte", "sur"]);
        using var form = Create();
        var input = Get<TextBox>(form, "_txtInput");

        for (var i = 0; i < 5; i++) form.BrowseHistory(-1);

        input.Text.Should().Be("norte");
    });

    [Fact]
    public void History_IsNotBrowsable_InPasswordMode() => Sta.Run(() =>
    {
        _history.Add("secreto-anterior");
        _session.PasswordMode.Returns(true);
        using var form = Create();

        form.BrowseHistory(-1);

        Get<TextBox>(form, "_txtInput").Text.Should().BeEmpty();
    });

    // ── Movement mode (F2) ─────────────────────────────────────────────────

    /// <summary>Movement mode on, with the Spanish defaults as effective commands (Home, End, 5 and 0 have none).</summary>
    private void MovementOn(bool on = true)
    {
        _session.MovementMode.Returns(on);
        _session.HasMovement(Arg.Any<int>()).Returns(call => MovementKeys.HasDefault(call.Arg<int>()));
    }

    [Theory]
    [InlineData(Keys.Up, 10)]
    [InlineData(Keys.Down, 11)]
    [InlineData(Keys.Left, 12)]
    [InlineData(Keys.Right, 13)]
    [InlineData(Keys.PageUp, 14)]
    [InlineData(Keys.PageDown, 15)]
    public void MovementMode_EmptyBox_ArrowsAndPagesMove_AndAreSwallowed(Keys key, int code) => Sta.Run(() =>
    {
        MovementOn();
        _history.Add("mirar");
        using var form = Create();

        form.HandleInputKey(key).Should().BeTrue();

        _session.Received(1).ExecuteMovementAsync(code);
        Get<TextBox>(form, "_txtInput").Text.Should().BeEmpty("the arrow did not browse the history");
    });

    [Fact]
    public void MovementMode_HomeAndEnd_MoveOnlyWhenTheyHaveACommand() => Sta.Run(() =>
    {
        MovementOn();
        using var form = Create();

        form.HandleInputKey(Keys.Home).Should().BeFalse("no command: the key keeps its normal behaviour");
        form.HandleInputKey(Keys.End).Should().BeFalse();
        _session.DidNotReceiveWithAnyArgs().ExecuteMovementAsync(default);

        _session.HasMovement(16).Returns(true);
        form.HandleInputKey(Keys.Home).Should().BeTrue();
        _session.Received(1).ExecuteMovementAsync(16);
    });

    [Theory]
    [InlineData(Keys.Up)]
    [InlineData(Keys.Left)]
    [InlineData(Keys.Right)]
    [InlineData(Keys.PageUp)]
    [InlineData(Keys.PageDown)]
    [InlineData(Keys.Home)]
    public void MovementMode_WithTextInTheBox_NavigationKeysDoNotMove(Keys key) => Sta.Run(() =>
    {
        MovementOn();
        _session.HasMovement(16).Returns(true);
        using var form = Create();
        Get<TextBox>(form, "_txtInput").Text = "decir hola";

        form.HandleInputKey(key);

        _session.DidNotReceiveWithAnyArgs().ExecuteMovementAsync(default);
    });

    [Theory]
    [InlineData(Keys.Left)]
    [InlineData(Keys.Right)]
    [InlineData(Keys.Home)]
    [InlineData(Keys.End)]
    [InlineData(Keys.PageUp)]
    public void MovementMode_WithTextInTheBox_CursorKeysAreNotSwallowed(Keys key) => Sta.Run(() =>
    {
        MovementOn();
        using var form = Create();
        Get<TextBox>(form, "_txtInput").Text = "decir hola";

        form.HandleInputKey(key).Should().BeFalse();
    });

    [Fact]
    public void MovementMode_WithTextInTheBox_UpArrowStillBrowsesTheHistory() => Sta.Run(() =>
    {
        MovementOn();
        _history.AddRange(["norte", "mirar"]);
        using var form = Create();
        var input = Get<TextBox>(form, "_txtInput");
        input.Text = "dec";

        form.HandleInputKey(Keys.Up).Should().BeTrue();

        input.Text.Should().Be("mirar");
        _session.DidNotReceiveWithAnyArgs().ExecuteMovementAsync(default);
    });

    [Theory]
    [InlineData(Keys.Shift | Keys.Up)]
    [InlineData(Keys.Alt | Keys.Up)]
    [InlineData(Keys.Shift | Keys.Left)]
    [InlineData(Keys.Control | Keys.Left)]
    [InlineData(Keys.Control | Keys.Home)]
    [InlineData(Keys.Shift | Keys.PageDown)]
    [InlineData(Keys.Control | Keys.Alt | Keys.Right)]
    [InlineData(Keys.Shift | Keys.NumPad8)]
    [InlineData(Keys.Alt | Keys.NumPad8)]
    public void MovementMode_WithModifiers_NothingMoves_AndTheKeyIsNotSwallowed(Keys keyData) => Sta.Run(() =>
    {
        MovementOn();
        _session.HasMovement(Arg.Any<int>()).Returns(true);
        using var form = Create();

        form.HandleInputKey(keyData).Should().BeFalse();

        _session.DidNotReceiveWithAnyArgs().ExecuteMovementAsync(default);
    });

    [Theory]
    [InlineData(Keys.Up)]
    [InlineData(Keys.Left)]
    [InlineData(Keys.PageUp)]
    [InlineData(Keys.NumPad8)]
    public void MovementModeOff_NothingMoves(Keys key) => Sta.Run(() =>
    {
        MovementOn(false);
        using var form = Create();

        form.HandleInputKey(key);

        _session.DidNotReceiveWithAnyArgs().ExecuteMovementAsync(default);
    });

    [Theory]
    [InlineData(Keys.Left)]
    [InlineData(Keys.Right)]
    [InlineData(Keys.PageUp)]
    [InlineData(Keys.PageDown)]
    [InlineData(Keys.Home)]
    [InlineData(Keys.End)]
    [InlineData(Keys.NumPad8)]
    public void MovementModeOff_KeysAreNotSwallowed(Keys key) => Sta.Run(() =>
    {
        MovementOn(false);
        using var form = Create();

        form.HandleInputKey(key).Should().BeFalse();
    });

    [Fact]
    public void MovementModeOff_EmptyBox_UpArrowBrowsesTheHistory_AsAlways() => Sta.Run(() =>
    {
        MovementOn(false);
        _history.AddRange(["norte", "mirar"]);
        using var form = Create();

        form.HandleInputKey(Keys.Up).Should().BeTrue();

        Get<TextBox>(form, "_txtInput").Text.Should().Be("mirar");
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CtrlArrows_BrowseTheHistory_WithMovementModeOnOrOff(bool movementMode) => Sta.Run(() =>
    {
        MovementOn(movementMode);
        _history.AddRange(["norte", "sur", "mirar"]);
        using var form = Create();
        var input = Get<TextBox>(form, "_txtInput");

        form.HandleInputKey(Keys.Control | Keys.Up).Should().BeTrue();
        input.Text.Should().Be("mirar");
        form.HandleInputKey(Keys.Control | Keys.Up).Should().BeTrue();
        input.Text.Should().Be("sur");
        form.HandleInputKey(Keys.Control | Keys.Down).Should().BeTrue();
        input.Text.Should().Be("mirar");
        form.HandleInputKey(Keys.Control | Keys.Down).Should().BeTrue();
        input.Text.Should().BeEmpty();

        _session.DidNotReceiveWithAnyArgs().ExecuteMovementAsync(default);
    });

    [Fact]
    public void MovementMode_AfterRecallingHistoryWithCtrlUp_PlainArrowsEditAgain_UntilTheBoxIsEmpty() => Sta.Run(() =>
    {
        MovementOn();
        _history.Add("mirar");
        using var form = Create();

        form.HandleInputKey(Keys.Control | Keys.Up);
        form.HandleInputKey(Keys.Left).Should().BeFalse("there is text now: the arrow moves the cursor");
        form.HandleInputKey(Keys.Escape).Should().BeTrue();
        form.HandleInputKey(Keys.Left).Should().BeTrue("empty again: the arrow is a movement key");

        _session.Received(1).ExecuteMovementAsync(12);
    });

    [Fact]
    public void CtrlNumPad_ReadsTheMessage_AndNeverMoves() => Sta.Run(() =>
    {
        MovementOn();
        _messages.Add(new SessionMessage(1, DateTime.Now, "Ana te dice: hola"));
        using var form = Create();

        form.HandleReviewKey(Keys.Control | Keys.NumPad1).Should().BeTrue("the window takes it before the input box");
        form.HandleInputKey(Keys.Control | Keys.NumPad1).Should().BeFalse();

        _announcer.Received(1).Announce("1: Ana te dice: hola", AnnouncePriority.Interrupt);
        _session.DidNotReceiveWithAnyArgs().ExecuteMovementAsync(default);
    });

    [Theory]
    [InlineData("")]
    [InlineData("decir hola")]
    public void MovementMode_NumPad_MovesAlways_WithOrWithoutText(string text) => Sta.Run(() =>
    {
        MovementOn();
        using var form = Create();
        var input = Get<TextBox>(form, "_txtInput");
        input.Text = text;

        form.HandleInputKey(Keys.NumPad8).Should().BeTrue();
        form.HandleInputKey(Keys.NumPad3).Should().BeTrue();

        Received.InOrder(() =>
        {
            _session.ExecuteMovementAsync(8);
            _session.ExecuteMovementAsync(3);
        });
        input.Text.Should().Be(text);
    });

    [Fact]
    public void MovementMode_NumPadKeyWithoutCommand_IsNotSwallowed_SoItTypesItsDigit() => Sta.Run(() =>
    {
        MovementOn();
        using var form = Create();

        form.HandleInputKey(Keys.NumPad5).Should().BeFalse();
        form.HandleInputKey(Keys.NumPad0).Should().BeFalse();

        _session.DidNotReceiveWithAnyArgs().ExecuteMovementAsync(default);
    });

    [Fact]
    public void MovementMode_TopRowDigits_AreNeverMovement() => Sta.Run(() =>
    {
        MovementOn();
        using var form = Create();

        form.HandleInputKey(Keys.D8).Should().BeFalse();

        _session.DidNotReceiveWithAnyArgs().ExecuteMovementAsync(default);
    });

    [Theory]
    [InlineData(Keys.Up)]
    [InlineData(Keys.NumPad8)]
    public void MovementMode_WhileTypingAPassword_NothingMoves(Keys key) => Sta.Run(() =>
    {
        MovementOn();
        _session.PasswordMode.Returns(true);
        using var form = Create();

        form.HandleInputKey(key);

        _session.DidNotReceiveWithAnyArgs().ExecuteMovementAsync(default);
    });

    [Fact]
    public void InputKeys_EnterSubmits_EscapeClears_OtherKeysPassThrough() => Sta.Run(() =>
    {
        using var form = Create();
        var input = Get<TextBox>(form, "_txtInput");
        input.Text = "mirar";

        form.HandleInputKey(Keys.A).Should().BeFalse();
        form.HandleInputKey(Keys.Enter).Should().BeTrue();
        _session.Received(1).SubmitInputAsync("mirar");

        input.Text = "a medias";
        form.HandleInputKey(Keys.Escape).Should().BeTrue();
        input.Text.Should().BeEmpty();

        form.HandleInputKey(Keys.Shift | Keys.Enter).Should().BeTrue();
        _session.Received(1).SendBlankLineAsync();
    });

    [Theory]
    [InlineData(Keys.Up, MovementKey.ArrowUp)]
    [InlineData(Keys.Down, MovementKey.ArrowDown)]
    [InlineData(Keys.Left, MovementKey.ArrowLeft)]
    [InlineData(Keys.Right, MovementKey.ArrowRight)]
    [InlineData(Keys.PageUp, MovementKey.PageUp)]
    [InlineData(Keys.PageDown, MovementKey.PageDown)]
    [InlineData(Keys.Home, MovementKey.Home)]
    [InlineData(Keys.End, MovementKey.End)]
    [InlineData(Keys.NumPad0, MovementKey.NumPad0)]
    [InlineData(Keys.NumPad9, MovementKey.NumPad9)]
    public void MovementKeyMap_KnowsEveryKey(Keys key, MovementKey expected) =>
        MovementKeyMap.FromKey(key).Should().Be(expected);

    [Theory]
    [InlineData(Keys.D8)]
    [InlineData(Keys.A)]
    [InlineData(Keys.Enter)]
    [InlineData(Keys.Insert)]
    [InlineData(Keys.Delete)]
    [InlineData(Keys.Decimal)]
    public void MovementKeyMap_IgnoresEverythingElse(Keys key) =>
        MovementKeyMap.FromKey(key).Should().BeNull();

    [Fact]
    public void SessionLines_ArePaintedInReceived_AndMessagesInMessages() => Sta.Run(() =>
    {
        using var form = Create();
        form.CreateControl();

        _session.LineReceived += Raise.Event<Action<SessionLine>>(
            new SessionLine([new StyledSegment("Estás en una plaza.", AnsiStyle.Default)], "Estás en una plaza.", SessionLineKind.Mud));
        _session.MessageAdded += Raise.Event<Action<SessionMessage>>(new SessionMessage(1, DateTime.Now, "Ana te dice: hola"));

        Get<AnsiTerminalBox>(form, "_terminal").Text.Should().Be("Estás en una plaza.");
        Get<AnsiTerminalBox>(form, "_rtbMessages").Text.Should().Be("Ana te dice: hola");
    });

    [Fact]
    public void Announcements_FromTheSession_ReachTheAnnouncer() => Sta.Run(() =>
    {
        using var form = Create();
        form.CreateControl();

        _session.Announce += Raise.Event<Action<string, AnnouncePriority>>("Un orco llega.", AnnouncePriority.Queue);
        _time.Advance(TimeSpan.FromMilliseconds(100)); // queued text is batched for a moment before it is spoken

        _announcer.Received(1).Announce("Un orco llega.", AnnouncePriority.Queue);
    });

    // Needs a desktop where a window can really become the active one: excluded in CI (see TestCategories).
    [Fact]
    [Trait(TestCategories.Category, TestCategories.InteractiveDesktop)]
    public void WindowActivation_IsReportedToTheSession() => Sta.Run(() =>
    {
        using var form = Create();
        form.Show();
        form.Activate();
        Application.DoEvents();
        _session.Received().IsWindowActive = true;
        form.Hide();
    });

    [Fact]
    public void Options_AreApplied_ToCursorBehaviourAndScreenReaderMode() => Sta.Run(() =>
    {
        _session.Options.Returns(OmnimudOptions.Default with
        {
            ConfirmBeforeExit = false,
            CursorOnReceived = CursorBehavior.Keep,
            CursorOnMessages = CursorBehavior.GoToEnd,
            ScreenReader = ScreenReaderMode.Nvda,
            MaxLines = 2500,
        });
        _session.InitializeAsync().Returns(Task.CompletedTask);
        _session.ConnectAsync().Returns(Task.CompletedTask);
        using var form = Create();

        form.Show();
        Application.DoEvents();

        Get<AnsiTerminalBox>(form, "_terminal").CursorBehavior.Should().Be(CursorBehavior.Keep);
        Get<AnsiTerminalBox>(form, "_terminal").MaxLines.Should().Be(2500);
        Get<AnsiTerminalBox>(form, "_rtbMessages").CursorBehavior.Should().Be(CursorBehavior.GoToEnd);
        _announcer.Received().Mode = ScreenReaderMode.Nvda;
        form.Hide();
    });

    [Theory]
    [InlineData(0, 0, 0, 5, "5 segundos.")]
    [InlineData(0, 0, 1, 1, "1 minuto, 1 segundo.")]
    [InlineData(0, 2, 5, 0, "2 horas, 5 minutos.")]
    [InlineData(1, 0, 0, 3, "1 día, 3 segundos.")]
    [InlineData(0, 0, 0, 0, "0 segundos.")]
    public void ConnectedTime_IsSpelledOut_WithProperSpacing(int d, int h, int m, int s, string expected)
    {
        Strings.Culture = CultureInfo.GetCultureInfo("es");
        FrmGame.FormatElapsed(new TimeSpan(d, h, m, s)).Should().Be(expected);
    }
}
