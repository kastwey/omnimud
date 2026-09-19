using System.Globalization;
using System.Windows.Automation;
using NSubstitute;
using Omnimud.Core.Options;
using Omnimud.Core.Session;
using Omnimud.Core.Sound;
using Omnimud.Core.Text;
using Omnimud.UI.Forms;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;
using Omnimud.UI.Services.Accessibility;

namespace Omnimud.UI.Tests.Accessibility;

/// <summary>
/// Looks at the game window from the outside through UI Automation, the way a screen reader
/// does: the window runs its own message loop on an STA thread and the test queries it from
/// another thread.
/// </summary>
public sealed class GameWindowUiaTests
{
    private sealed record BoxInfo(string Name, string ControlType, bool HasTextPattern, bool HasValuePattern, string HelpText, string LabeledBy, Msaa.Info Msaa);

    private static Dictionary<string, BoxInfo> Inspect()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("es");
        var session = Substitute.For<IMudSession>();
        session.Profile.Returns(new SessionProfile { Title = "Prueba", Host = "localhost", Port = 23 });
        session.Options.Returns(OmnimudOptions.Default with { ConfirmBeforeExit = false });
        session.State.Returns(SessionState.Connected);
        session.Messages.Returns([]);
        session.History.Returns([]);
        session.ActionMenu.Returns(Omnimud.Core.Actions.ActionMenu.Empty);
        session.InitializeAsync().Returns(Task.CompletedTask);
        session.ConnectAsync().Returns(Task.CompletedTask);
        session.CloseAsync(Arg.Any<bool>()).Returns(Task.CompletedTask);

        FrmGame? form = null;
        var ready = new ManualResetEventSlim();
        var ui = new Thread(() =>
        {
            form = new FrmGame(session, Substitute.For<ISessionSound>(), Substitute.For<ISessionDialogs>(),
                TimeProvider.System, Substitute.For<IAnnouncer>());
            form.Shown += (_, _) =>
            {
                for (var i = 0; i < 30; i++)
                    session.LineReceived += Raise.Event<Action<SessionLine>>(
                        new SessionLine([new StyledSegment($"Linea de texto numero {i}", AnsiStyle.Default)], $"Linea de texto numero {i}", SessionLineKind.Mud));
                ready.Set();
            };
            Application.Run(form);
        });
        ui.SetApartmentState(ApartmentState.STA);
        ui.IsBackground = true;
        ui.Start();
        ready.Wait(TimeSpan.FromSeconds(20)).Should().BeTrue();
        Thread.Sleep(300);

        var result = new Dictionary<string, BoxInfo>();
        try
        {
            var handle = (IntPtr)form!.Invoke(() => form.Handle);
            var root = AutomationElement.FromHandle(handle);
            foreach (var id in new[] { "_txtInput", "_terminal", "_rtbMessages" })
            {
                var element = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id));
                element.Should().NotBeNull($"{id} must be reachable through UI Automation");
                var current = element!.Current;
                result[id] = new BoxInfo(
                    current.Name,
                    current.ControlType.ProgrammaticName,
                    element.TryGetCurrentPattern(TextPattern.Pattern, out _),
                    element.TryGetCurrentPattern(ValuePattern.Pattern, out _),
                    current.HelpText,
                    current.LabeledBy?.Current.Name ?? string.Empty,
                    Msaa.Read((IntPtr)form.Invoke(() => form.Controls.Find(id, true)[0].Handle)));
            }
        }
        finally
        {
            form!.BeginInvoke(form.Close);
            ui.Join(TimeSpan.FromSeconds(10));
        }
        return result;
    }

    [Fact]
    public void TextBoxes_AreExposedAsNamedEditableText_NotAsABlobOfStaticText()
    {
        var boxes = Inspect();

        boxes["_txtInput"].Name.Should().Be("Texto a enviar");
        boxes["_terminal"].Name.Should().Be("Recibido");
        boxes["_rtbMessages"].Name.Should().Be("Mensajes");

        foreach (var (id, box) in boxes)
        {
            // A name that is the content means the reader speaks the whole text on focus and never the label.
            box.Name.Should().NotContain("Linea de texto", $"{id} must be named by its label, not by its content");
            box.HasTextPattern.Should().BeTrue($"{id} must offer the text pattern so the reader reviews it line by line");
            // MSAA is what NVDA uses for these boxes: a named, editable-text object.
            box.Msaa.Name.Should().Be(box.Name, $"{id}: MSAA and UIA must agree on the name");
            box.Msaa.Role.Should().Be(Msaa.RoleText, $"{id} must have the editable text role");
            (box.Msaa.State & Msaa.StateReadOnly).Should().Be(0, $"{id}: NVDA reads the whole content of a read-only edit box when it gets the focus");
            box.Msaa.Description.Should().BeNullOrEmpty($"{id}: a description is read aloud on every focus");
            box.HelpText.Should().BeNullOrEmpty($"{id}: help text is read aloud on every focus");
            if (id != "_txtInput")
                box.HasValuePattern.Should().BeFalse($"{id}: a value holding the whole text is read in full when the box gets the focus");
        }
    }
}
