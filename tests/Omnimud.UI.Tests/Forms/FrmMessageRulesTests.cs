using System.Globalization;
using NSubstitute;
using Omnimud.Core.Session;
using Omnimud.Data.Entities;
using Omnimud.UI.Forms;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Services.Accessibility;
using Omnimud.UI.Tests.Accessibility;
using Omnimud.UI.Tests.Presenters;

namespace Omnimud.UI.Tests.Forms;

public sealed class FrmMessageRulesTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private readonly ScriptedPrompts _prompts = new();
    private readonly IAnnouncer _announcer = Substitute.For<IAnnouncer>();
    private readonly IMessageRulesDialogs _dialogs = Substitute.For<IMessageRulesDialogs>();

    public FrmMessageRulesTests() => Strings.Culture = CultureInfo.GetCultureInfo("es");

    public void Dispose() => _db.Dispose();

    private FrmMessageRules Create()
    {
        var form = new FrmMessageRules(_db.Rules, _prompts, _announcer, _dialogs);
        UiPump.Wait(form.LoadAsync());
        return form;
    }

    private int AddOwnSet(params string[] patterns)
    {
        var id = _db.Rules.AddRuleSetAsync(new MessageRuleSetEntity { Name = "Mío" }).GetAwaiter().GetResult();
        foreach (var pattern in patterns)
            _db.Rules.AddRuleAsync(new MessageRuleEntity { RuleSetId = id, Pattern = pattern }).GetAwaiter().GetResult();
        return id;
    }

    /// <summary>Works with and without a window handle (SelectedIndices needs one).</summary>
    private static IEnumerable<int> Selected(ListView list) => list.Items.Cast<ListViewItem>().Where(i => i.Selected).Select(i => i.Index);

    private static T Get<T>(Form form, string name) where T : Control => (T)form.Controls.Find(name, true).Single();

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void Window_PassesTheAccessibilityAudit_InEveryLanguage(string culture) => Sta.Run(() =>
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);
        using var form = Create();

        AccessibilityAudit.Check(form, isDialog: true).Should().BeEmpty();
    });

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void RuleEditor_PassesTheAccessibilityAudit_InEveryLanguage(string culture) => Sta.Run(() =>
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);
        using var form = new FrmAddEditMessageRule(new MessageRuleEditorModel(), _prompts);

        AccessibilityAudit.Check(form, isDialog: true).Should().BeEmpty();
    });

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void SetNameDialog_PassesTheAccessibilityAudit_InEveryLanguage(string culture) => Sta.Run(() =>
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);
        using var form = new FrmMessageRuleSetName(Strings.MsgRules_NewSetTitle, "Copia de Balzhur");

        AccessibilityAudit.Check(form, isDialog: true).Should().BeEmpty();
        form.SetName.Should().Be("Copia de Balzhur");
        ((TextBox)form.Controls.Find("_txtName", true).Single()).SelectionLength.Should().Be("Copia de Balzhur".Length);
    });

    [Fact]
    public void BuiltInSet_IsMarked_AndOnlyDuplicateIsEnabled() => Sta.Run(() =>
    {
        using var form = Create();

        var sets = Get<ListBox>(form, "_listSets");
        sets.Items.Count.Should().Be(4);
        sets.SelectedIndex.Should().Be(0, "a list is never left without a selected element");
        sets.SelectedItem!.ToString().Should().EndWith("(integrado)");
        Get<ListView>(form, "_listRules").Items.Count.Should().BeGreaterThan(0);
        Selected(Get<ListView>(form, "_listRules")).Should().Equal(0);

        Get<Button>(form, "_btnDuplicate").Enabled.Should().BeTrue();
        foreach (var name in new[] { "_btnRename", "_btnRemoveSet", "_btnAddRule", "_btnEditRule", "_btnRemoveRule", "_btnUp", "_btnDown", "_btnToggle" })
            Get<Button>(form, name).Enabled.Should().BeFalse(name);
        Get<Label>(form, "_lblStatus").Text.Should().Contain("integrado");
    });

    [Fact]
    public void OwnSet_ShowsItsRulesInOrder_WithTheStateAsText_AndEverythingEnabled() => Sta.Run(() =>
    {
        var id = AddOwnSet("uno", "dos", "tres");
        using var form = Create();

        UiPump.Wait(form.Presenter.SelectSetAsync(id));

        var rules = Get<ListView>(form, "_listRules");
        rules.Items.Cast<ListViewItem>().Select(i => i.Text).Should().Equal("uno", "dos", "tres");
        rules.Items[0].SubItems[1].Text.Should().Be(Strings.MsgRules_StateEnabled, "state is text, never only a colour or a glyph");
        Get<ListBox>(form, "_listSets").SelectedItem!.ToString().Should().Be("Mío");
        foreach (var name in new[] { "_btnRename", "_btnRemoveSet", "_btnAddRule", "_btnEditRule", "_btnRemoveRule", "_btnDown", "_btnToggle" })
            Get<Button>(form, name).Enabled.Should().BeTrue(name);
        Get<Button>(form, "_btnUp").Enabled.Should().BeFalse("the first rule cannot go up");
    });

    [Fact]
    public void Toggle_UpdatesTheRow_KeepsTheSelection_AndIsAnnounced() => Sta.Run(() =>
    {
        var id = AddOwnSet("uno", "dos");
        using var form = Create();
        UiPump.Wait(form.Presenter.SelectSetAsync(id));
        form.Presenter.SelectRule(1);

        UiPump.Wait(form.Presenter.ToggleRuleAsync());

        var rules = Get<ListView>(form, "_listRules");
        rules.Items[1].SubItems[1].Text.Should().Be(Strings.MsgRules_StateDisabled);
        Selected(rules).Should().Equal(1);
        _announcer.Received(1).Announce(Strings.MsgRules_RuleDisabled, AnnouncePriority.MostRecent);
        Get<Label>(form, "_lblStatus").Text.Should().Be(Strings.MsgRules_RuleDisabled);
    });

    [Fact]
    public void Move_ReordersTheList_AndTheSelectionFollowsTheRule() => Sta.Run(() =>
    {
        var id = AddOwnSet("uno", "dos", "tres");
        using var form = Create();
        UiPump.Wait(form.Presenter.SelectSetAsync(id));

        UiPump.Wait(form.Presenter.MoveDownAsync());

        var rules = Get<ListView>(form, "_listRules");
        rules.Items.Cast<ListViewItem>().Select(i => i.Text).Should().Equal("dos", "uno", "tres");
        Selected(rules).Should().Equal(1);
        _announcer.Received(1).Announce(string.Format(Strings.MsgRules_StatusRuleMoved, 2, 3), AnnouncePriority.MostRecent);
    });

    [Fact]
    public void Test_ShowsTheResultInTheResultBox_AndMovesTheFocusThere() => Sta.Run(() =>
    {
        var id = AddOwnSet("^(\\w+) dice: (.+)$");
        using var form = Create();
        UiPump.Wait(form.Presenter.SelectSetAsync(id));
        Get<TextBox>(form, "_txtSample").Text = "Pepe dice: hola";

        form.RunTest();

        Get<TextBox>(form, "_txtResult").Text.Should().Be(string.Format(Strings.MsgRule_TestMatch, "Pepe dice: hola"));
        Get<TextBox>(form, "_txtResult").ReadOnly.Should().BeTrue();
        form.ActiveControl.Should().BeSameAs(Get<TextBox>(form, "_txtResult"));

        Get<TextBox>(form, "_txtSample").Text = "nada que ver";
        form.RunTest();
        Get<TextBox>(form, "_txtResult").Text.Should().Be(Strings.MsgRule_TestNoMatch);
    });

    // ── Rule editor ────────────────────────────────────────────────────────

    [Fact]
    public void RuleEditor_LoadsTheRule_AndAcceptWritesItBackToTheModel() => Sta.Run(() =>
    {
        var model = new MessageRuleEditorModel(new MessageRuleEntity { Pattern = "viejo", Template = "$1", Channel = "chat", CaseSensitive = true, Enabled = false });
        using var form = new FrmAddEditMessageRule(model, _prompts);

        Get<TextBox>(form, "_txtPattern").Text.Should().Be("viejo");
        Get<TextBox>(form, "_txtTemplate").Text.Should().Be("$1");
        Get<TextBox>(form, "_txtChannel").Text.Should().Be("chat");
        Get<CheckBox>(form, "_chkCase").Checked.Should().BeTrue();
        Get<CheckBox>(form, "_chkEnabled").Checked.Should().BeFalse();
        Get<Label>(form, "_lblTemplateHint").Text.Should().Contain("$1").And.Contain("$0");

        Get<TextBox>(form, "_txtPattern").Text = "^nuevo (.+)$";
        Get<CheckBox>(form, "_chkEnabled").Checked = true;
        form.Accept().Should().BeTrue();

        form.DialogResult.Should().Be(DialogResult.OK);
        model.Should().BeEquivalentTo(new { Pattern = "^nuevo (.+)$", Template = "$1", Channel = "chat", CaseSensitive = true, Enabled = true });
    });

    [Fact]
    public void RuleEditor_InvalidRegex_IsAMessage_FocusOnThePattern_AndStaysOpen() => Sta.Run(() =>
    {
        using var form = new FrmAddEditMessageRule(new MessageRuleEditorModel(), _prompts);
        Get<TextBox>(form, "_txtPattern").Text = "(sin cerrar";

        form.Accept().Should().BeFalse();

        _prompts.Warnings.Should().ContainSingle().Which.Should().StartWith(Strings.MsgRule_PatternInvalid.Split('{')[0]);
        form.ActiveControl.Should().BeSameAs(Get<TextBox>(form, "_txtPattern"));
        form.DialogResult.Should().Be(DialogResult.None);
    });

    [Fact]
    public void RuleEditor_Test_UsesWhatIsOnScreen() => Sta.Run(() =>
    {
        using var form = new FrmAddEditMessageRule(new MessageRuleEditorModel(), _prompts);
        Get<TextBox>(form, "_txtPattern").Text = @"^(\w+) te dice: (.+)$";
        Get<TextBox>(form, "_txtTemplate").Text = "$1: $2";
        Get<TextBox>(form, "_txtSample").Text = "Gandalf te dice: corre";

        form.RunTest();

        Get<TextBox>(form, "_txtResult").Text.Should().Be(string.Format(Strings.MsgRule_TestMatch, "Gandalf: corre"));
        form.ActiveControl.Should().BeSameAs(Get<TextBox>(form, "_txtResult"));
        form.DialogResult.Should().Be(DialogResult.None, "testing never closes the dialog");

        Get<TextBox>(form, "_txtSample").Text = "otra cosa";
        form.RunTest();
        Get<TextBox>(form, "_txtResult").Text.Should().Be(Strings.MsgRule_TestNoMatch);
    });
}
