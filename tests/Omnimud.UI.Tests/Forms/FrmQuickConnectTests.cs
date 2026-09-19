using System.Globalization;
using Omnimud.Core.Session;
using Omnimud.UI.Forms;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Tests.Accessibility;
using Omnimud.UI.Tests.Presenters;

namespace Omnimud.UI.Tests.Forms;

public sealed class FrmQuickConnectTests
{
    private readonly ScriptedPrompts _prompts = new();
    private readonly MemoryQuickConnectStore _store = new();

    public FrmQuickConnectTests() => Strings.Culture = CultureInfo.GetCultureInfo("es");

    private FrmQuickConnect Create()
    {
        var form = new FrmQuickConnect(_prompts, _store);
        UiPump.Wait(form.LoadAsync());
        return form;
    }

    private static T Get<T>(Form form, string name) where T : Control => (T)form.Controls.Find(name, true).Single();

    [Theory]
    [InlineData("es", false)]
    [InlineData("en", false)]
    [InlineData("es", true)]
    [InlineData("en", true)]
    public void Dialog_PassesTheAccessibilityAudit_InEveryLanguage(string culture, bool remembered) => Sta.Run(() =>
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);
        if (remembered) _store.SaveAsync(new QuickConnectSettings("mud.org", 4000, true, false, "ascii")).GetAwaiter().GetResult();
        using var form = Create();

        AccessibilityAudit.Check(form, isDialog: true).Should().BeEmpty();
    });

    [Fact]
    public void TheFirstTime_HostAndPortAreEmpty_AndTheHostHasTheFocus() => Sta.Run(() =>
    {
        using var form = Create();

        Get<TextBox>(form, "_txtHost").Text.Should().BeEmpty("no more 'localhost' by default");
        Get<TextBox>(form, "_txtPort").Text.Should().BeEmpty("no more 23 by default");
        Get<ComboBox>(form, "_cboEncoding").Text.Should().Be("utf-8");
        Get<ComboBox>(form, "_cboEncoding").Items.Cast<string>().Should().Equal("utf-8", "iso-8859-1", "windows-1252", "ascii");
        Get<CheckBox>(form, "_chkTls").Checked.Should().BeFalse();
        Get<CheckBox>(form, "_chkValidate").Enabled.Should().BeFalse();
        Get<CheckBox>(form, "_chkValidate").Checked.Should().BeTrue();
        form.ActiveControl.Should().BeSameAs(Get<TextBox>(form, "_txtHost"));
        form.Profile.Should().BeNull();
    });

    [Fact]
    public void CertificateCheck_IsOnlyEnabledWithTls() => Sta.Run(() =>
    {
        using var form = Create();

        Get<CheckBox>(form, "_chkTls").Checked = true;
        Get<CheckBox>(form, "_chkValidate").Enabled.Should().BeTrue();
        Get<CheckBox>(form, "_chkTls").Checked = false;
        Get<CheckBox>(form, "_chkValidate").Enabled.Should().BeFalse();
    });

    [Fact]
    public void Accept_ReturnsACompleteProfile_AndRemembersIt() => Sta.Run(() =>
    {
        using var form = Create();
        Get<TextBox>(form, "_txtHost").Text = " mud.example.org ";
        Get<TextBox>(form, "_txtPort").Text = "4443";
        Get<CheckBox>(form, "_chkTls").Checked = true;
        Get<CheckBox>(form, "_chkValidate").Checked = false;
        Get<ComboBox>(form, "_cboEncoding").Text = "iso-8859-1";

        UiPump.Wait(form.AcceptAsync()).Should().BeTrue();

        form.DialogResult.Should().Be(DialogResult.OK);
        form.Profile.Should().BeEquivalentTo(new SessionProfile
        {
            Title = "mud.example.org:4443", Host = "mud.example.org", Port = 4443, UseTls = true, ValidateCertificate = false, Encoding = "iso-8859-1",
        });
        (form.Host, form.Port, form.UseTls, form.ValidateCertificate, form.MudEncoding).Should().Be(("mud.example.org", 4443, true, false, "iso-8859-1"));
        _store.LoadAsync().GetAwaiter().GetResult().Should().Be(new QuickConnectSettings("mud.example.org", 4443, true, false, "iso-8859-1"));
    });

    [Fact]
    public void TheNextTime_ItComesFilledWithTheLastConnection_WithTheHostSelected() => Sta.Run(() =>
    {
        using (var first = Create())
        {
            Get<TextBox>(first, "_txtHost").Text = "mud.example.org";
            Get<TextBox>(first, "_txtPort").Text = "4443";
            Get<CheckBox>(first, "_chkTls").Checked = true;
            Get<CheckBox>(first, "_chkValidate").Checked = false;
            Get<ComboBox>(first, "_cboEncoding").Text = "windows-1252";
            UiPump.Wait(first.AcceptAsync()).Should().BeTrue();
        }

        using var next = Create();

        Get<TextBox>(next, "_txtHost").Text.Should().Be("mud.example.org");
        Get<TextBox>(next, "_txtHost").SelectionLength.Should().Be("mud.example.org".Length, "typing replaces the remembered host");
        Get<TextBox>(next, "_txtPort").Text.Should().Be("4443");
        Get<CheckBox>(next, "_chkTls").Checked.Should().BeTrue();
        Get<CheckBox>(next, "_chkValidate").Checked.Should().BeFalse();
        Get<CheckBox>(next, "_chkValidate").Enabled.Should().BeTrue();
        Get<ComboBox>(next, "_cboEncoding").Text.Should().Be("windows-1252");
        next.ActiveControl.Should().BeSameAs(Get<TextBox>(next, "_txtHost"));
    });

    [Fact]
    public void Cancelling_RemembersNothing() => Sta.Run(() =>
    {
        using (var form = Create())
        {
            Get<TextBox>(form, "_txtHost").Text = "a-medias.org";
            form.DialogResult = DialogResult.Cancel;
        }

        _store.LoadAsync().GetAwaiter().GetResult().Should().BeNull();
    });

    [Theory]
    [InlineData("_txtHost")]
    [InlineData("_txtPort")]
    public void TextBoxes_SelectAllTheirText_WhenTheyGetTheFocus(string name) => Sta.Run(() =>
    {
        using var form = Create();
        var box = (SelectAllTextBox)Get<TextBox>(form, name);
        box.Text = "contenido";
        box.Select(3, 0);

        box.SimulateEnter();

        (box.SelectionStart, box.SelectionLength).Should().Be((0, "contenido".Length));
    });

    [Theory]
    [InlineData("", "4000", "utf-8", "_txtHost")]
    [InlineData("mud.org", "", "utf-8", "_txtPort")]
    [InlineData("mud.org", "0", "utf-8", "_txtPort")]
    [InlineData("mud.org", "70000", "utf-8", "_txtPort")]
    [InlineData("mud.org", "abc", "utf-8", "_txtPort")]
    [InlineData("mud.org", "4000", "klingon", "_cboEncoding")]
    public void Accept_WithAProblem_ShowsTheMessage_MovesTheFocus_AndStaysOpen(string host, string port, string encoding, string focused) => Sta.Run(() =>
    {
        using var form = Create();
        Get<TextBox>(form, "_txtHost").Text = host;
        Get<TextBox>(form, "_txtPort").Text = port;
        Get<ComboBox>(form, "_cboEncoding").Text = encoding;

        UiPump.Wait(form.AcceptAsync()).Should().BeFalse();

        _prompts.Warnings.Should().ContainSingle();
        form.ActiveControl.Should().BeSameAs(Get<Control>(form, focused));
        form.DialogResult.Should().Be(DialogResult.None);
        form.Profile.Should().BeNull();
        ((Button)form.AcceptButton!).Enabled.Should().BeTrue("problems are told on accepting, the button is never disabled");
    });
}
