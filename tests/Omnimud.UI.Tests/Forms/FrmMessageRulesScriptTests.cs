using System.Globalization;
using NSubstitute;
using Omnimud.Core.Scripting;
using Omnimud.Core.Session;
using Omnimud.Data.Entities;
using Omnimud.Data.Seed;
using Omnimud.UI.Forms;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Services.Accessibility;
using Omnimud.UI.Tests.Accessibility;
using Omnimud.UI.Tests.Presenters;

namespace Omnimud.UI.Tests.Forms;

/// <summary>The message rules window with sets of type Lua script.</summary>
public sealed class FrmMessageRulesScriptTests : IDisposable
{
    private const string GoodScript = "for _, line in ipairs(om.lines) do\n  if om.match(line, [[ dice ']]) then om.message(line) end\nend";

    private readonly TempDatabase _db = new();
    private readonly ScriptedPrompts _prompts = new();
    private readonly IAnnouncer _announcer = Substitute.For<IAnnouncer>();
    private readonly IMessageRulesDialogs _dialogs = Substitute.For<IMessageRulesDialogs>();

    public FrmMessageRulesScriptTests() => Strings.Culture = CultureInfo.GetCultureInfo("es");

    public void Dispose() => _db.Dispose();

    private FrmMessageRules Create(IScriptEngine? engine = null)
    {
        var form = new FrmMessageRules(_db.Rules, _prompts, _announcer, _dialogs, engine);
        UiPump.Wait(form.LoadAsync());
        return form;
    }

    private int AddSet(string name, string? script, params string[] patterns)
    {
        var id = _db.Rules.AddRuleSetAsync(new MessageRuleSetEntity { Name = name, Script = script }).GetAwaiter().GetResult();
        foreach (var pattern in patterns)
            _db.Rules.AddRuleAsync(new MessageRuleEntity { RuleSetId = id, Pattern = pattern }).GetAwaiter().GetResult();
        return id;
    }

    private static T Get<T>(Form form, string name) where T : Control => (T)form.Controls.Find(name, true).Single();

    private static readonly string[] PatternControls = ["_lblRules", "_listRules", "_btnAddRule", "_btnEditRule", "_btnRemoveRule", "_btnUp", "_btnDown", "_btnToggle", "_lblKeys"];
    private static readonly string[] ScriptControls = ["_lblScript", "_txtScript", "_lblTabHint", "_btnSaveScript"];

    /// <summary>Visible as set by the form (the window is never shown in tests, so Control.Visible always says false).</summary>
    private static bool Shown(Form form, string name)
    {
        var control = form.Controls.Find(name, true).Single();
        var getState = typeof(Control).GetMethod("GetState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Control.GetState not found");
        var visibleFlag = Enum.ToObject(getState.GetParameters()[0].ParameterType, 2); // States.Visible
        return (bool)getState.Invoke(control, [visibleFlag])!;
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var limit = Environment.TickCount64 + 10_000;
        while (!condition())
        {
            if (Environment.TickCount64 > limit) throw new TimeoutException("The window never got there.");
            Application.DoEvents();
            Thread.Sleep(1);
        }
    }

    // ── Accessibility, in every state and language ─────────────────────────

    [Theory]
    [InlineData("es", "builtin")]
    [InlineData("en", "builtin")]
    [InlineData("es", "script")]
    [InlineData("en", "script")]
    [InlineData("es", "patterns")]
    [InlineData("en", "patterns")]
    public void Window_PassesTheAccessibilityAudit_InEveryStateAndLanguage(string culture, string state) => Sta.Run(() =>
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);
        var scriptId = AddSet("Con script", GoodScript);
        var patternId = AddSet("Con patrones", null, "uno", "dos");
        using var form = Create();
        if (state == "script") UiPump.Wait(form.Presenter.SelectSetAsync(scriptId));
        if (state == "patterns") UiPump.Wait(form.Presenter.SelectSetAsync(patternId));

        AccessibilityAudit.Check(form, isDialog: true).Should().BeEmpty();
        form.Presenter.IsScriptSet.Should().Be(state != "patterns");
    });

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void NewSetDialog_WithTheTypeSelector_PassesTheAccessibilityAudit(string culture) => Sta.Run(() =>
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);
        using var form = new FrmMessageRuleSetName(Strings.MsgRules_NewSetTitle, string.Empty, isScript: false);

        AccessibilityAudit.Check(form, isDialog: true).Should().BeEmpty();
        var type = Get<ComboBox>(form, "_cboType");
        type.Items.Cast<string>().Should().Equal(Strings.MsgRules_TypePatterns, Strings.MsgRules_TypeScript);
        type.DropDownStyle.Should().Be(ComboBoxStyle.DropDownList);
        form.IsScript.Should().BeFalse();
        type.SelectedIndex = 1;
        form.IsScript.Should().BeTrue();

        using var nameOnly = new FrmMessageRuleSetName(Strings.MsgRules_RenameSetTitle, "Mío");
        nameOnly.Controls.Find("_cboType", true).Should().BeEmpty();
        nameOnly.IsScript.Should().BeFalse();
    });

    [Fact]
    public void ScriptBox_IsAPlainMultilineTextBox_Monospaced_WithReturnsAndTabs_AndAVisibleHint() => Sta.Run(() =>
    {
        var id = AddSet("Con script", GoodScript);
        using var form = Create();
        UiPump.Wait(form.Presenter.SelectSetAsync(id));

        var box = Get<TextBox>(form, "_txtScript");
        box.GetType().Should().Be(typeof(TextBox), "no RichTextBox in dialogs");
        (box.Multiline, box.AcceptsReturn, box.AcceptsTab, box.WordWrap).Should().Be((true, true, true, false));
        box.Font.FontFamily.Name.Should().Be(FontFamily.GenericMonospace.Name);
        box.AccessibleName.Should().Be("Script Lua del conjunto");
        box.AccessibleDescription.Should().BeNullOrEmpty("it would be read on every focus");
        box.Height.Should().BeGreaterThan(250);
        box.Text.Should().Be(GoodScript.Replace("\n", "\r\n"));
        box.ReadOnly.Should().BeFalse();

        Get<Label>(form, "_lblScript").Text.Should().Contain("&");
        Get<Label>(form, "_lblTabHint").Text.Should().Be(Strings.MsgRules_ScriptTabHint).And.Contain("Ctrl+Tab");
        Shown(form, "_lblTabHint").Should().BeTrue();

        var sample = Get<TextBox>(form, "_txtSample");
        (sample.Multiline, sample.AcceptsReturn).Should().Be((true, true), "a script is tried against a block of several lines");
    });

    // ── The area on the right follows the type of the set ──────────────────

    [Fact]
    public void TheRightHandArea_ShowsTheScript_OrTheRules_DependingOnTheTypeOfTheSet() => Sta.Run(() =>
    {
        var scriptId = AddSet("Con script", GoodScript);
        var patternId = AddSet("Con patrones", null, "uno");
        using var form = Create();

        UiPump.Wait(form.Presenter.SelectSetAsync(scriptId));
        ScriptControls.Should().OnlyContain(name => Shown(form, name));
        PatternControls.Should().OnlyContain(name => !Shown(form, name));
        Get<ComboBox>(form, "_cboType").SelectedIndex.Should().Be(1);
        Get<ComboBox>(form, "_cboType").Enabled.Should().BeTrue();

        UiPump.Wait(form.Presenter.SelectSetAsync(patternId));
        ScriptControls.Should().OnlyContain(name => !Shown(form, name));
        PatternControls.Should().OnlyContain(name => Shown(form, name));
        Get<ComboBox>(form, "_cboType").SelectedIndex.Should().Be(0);
        Get<TextBox>(form, "_txtSample").AcceptsReturn.Should().BeFalse("Enter tests the line, as before");
    });

    [Fact]
    public void BuiltInSet_ShowsItsLuaScript_ReadOnly_TypeAndSaveDisabled_DuplicateEnabled() => Sta.Run(() =>
    {
        using var form = Create();
        UiPump.Wait(form.Presenter.SelectSetAsync(form.Presenter.Sets.Single(s => s.Name == "Callandor").Id));

        var box = Get<TextBox>(form, "_txtScript");
        box.Text.Should().Be(BuiltInMessageRuleSets.ScriptOf("Callandor")!.Replace("\n", "\r\n"));
        box.ReadOnly.Should().BeTrue();
        Get<ComboBox>(form, "_cboType").Enabled.Should().BeFalse();
        Get<Button>(form, "_btnSaveScript").Enabled.Should().BeFalse();
        Get<Button>(form, "_btnDuplicate").Enabled.Should().BeTrue();
        Get<Button>(form, "_btnTest").Enabled.Should().BeTrue();
        Get<Label>(form, "_lblStatus").Text.Should().Contain("integrado");
    });

    [Fact]
    public void ChangingTheTypeInTheSelector_SwitchesTheSet_AndIsAnnounced_DecliningPutsTheSelectorBack() => Sta.Run(() =>
    {
        var id = AddSet("Mío", null, "uno");
        using var form = Create();
        UiPump.Wait(form.Presenter.SelectSetAsync(id));
        var type = Get<ComboBox>(form, "_cboType");

        type.SelectedIndex = 1;
        PumpUntil(() => form.Presenter.IsScriptSet);

        Get<TextBox>(form, "_txtScript").Text.Should().Be(MessageRuleScriptTester.Template.Replace("\n", "\r\n"));
        _announcer.Received(1).Announce(string.Format(Strings.MsgRules_TypeChangedScript, "Mío"), AnnouncePriority.MostRecent);

        _prompts.ConfirmAnswers.Enqueue(false);
        type.SelectedIndex = 0;
        PumpUntil(() => type.SelectedIndex == 1);
        form.Presenter.IsScriptSet.Should().BeTrue("the user said no to discarding the script");

        _prompts.ConfirmAnswers.Enqueue(true);
        type.SelectedIndex = 0;
        PumpUntil(() => !form.Presenter.IsScriptSet);
        Get<ListView>(form, "_listRules").Items.Count.Should().Be(1, "the rules were never lost");
    });

    // ── Saving ─────────────────────────────────────────────────────────────

    [Fact]
    public void Save_SyntaxError_IsAMessageWithTheLine_FocusOnTheScript_CaretOnThatLine() => Sta.Run(() =>
    {
        var id = AddSet("Mío", GoodScript);
        using var form = Create();
        UiPump.Wait(form.Presenter.SelectSetAsync(id));
        var box = Get<TextBox>(form, "_txtScript");
        box.Text = "om.message('uno')\r\nom.message('dos')\r\nif then\r\nom.message('cuatro')";

        UiPump.Wait(form.SaveScriptAsync());

        _prompts.Warnings.Should().ContainSingle().Which.Should().StartWith("Error en la línea 3 del script:");
        form.ActiveControl.Should().BeSameAs(box);
        box.SelectionStart.Should().Be(box.Text.IndexOf("if then", StringComparison.Ordinal));
        box.SelectedText.Should().Be("if then");
        _db.Rules.GetRuleSetByIdAsync(id).GetAwaiter().GetResult()!.Script.Should().Be(GoodScript);
    });

    [Fact]
    public void Save_GoodScript_IsStored_Announced_AndTheTextStaysAsTyped() => Sta.Run(() =>
    {
        var id = AddSet("Mío", GoodScript);
        using var form = Create();
        UiPump.Wait(form.Presenter.SelectSetAsync(id));
        var box = Get<TextBox>(form, "_txtScript");
        box.Text = "-- mio\r\nom.message(om.lines[1])";
        box.Select(5, 0);

        UiPump.Wait(form.SaveScriptAsync());

        _prompts.Warnings.Should().BeEmpty();
        _db.Rules.GetRuleSetByIdAsync(id).GetAwaiter().GetResult()!.Script.Should().Be("-- mio\nom.message(om.lines[1])");
        _announcer.Received(1).Announce(string.Format(Strings.MsgRules_StatusScriptSaved, "Mío"), AnnouncePriority.MostRecent);
        Get<Label>(form, "_lblStatus").Text.Should().Be(string.Format(Strings.MsgRules_StatusScriptSaved, "Mío"));
        box.Text.Should().Be("-- mio\r\nom.message(om.lines[1])");
        box.SelectionStart.Should().Be(5, "saving does not send the caret to the top");
        form.Presenter.IsScriptDirty.Should().BeFalse();
    });

    [Fact]
    public void TypingInTheScriptBox_MakesItDirty_AndClosingOffersToSave() => Sta.Run(() =>
    {
        var id = AddSet("Mío", GoodScript);
        using var form = Create();
        UiPump.Wait(form.Presenter.SelectSetAsync(id));
        form.Presenter.IsScriptDirty.Should().BeFalse();

        Get<TextBox>(form, "_txtScript").Text = "om.message('cambiado')";
        form.Presenter.IsScriptDirty.Should().BeTrue();

        _prompts.ConfirmAnswers.Enqueue(true);
        UiPump.Wait(form.Presenter.LeaveScriptAsync());
        _prompts.Confirms.Should().ContainSingle().Which.Should().Be(string.Format(Strings.MsgRules_ConfirmSaveScript, "Mío"));
        _db.Rules.GetRuleSetByIdAsync(id).GetAwaiter().GetResult()!.Script.Should().Be("om.message('cambiado')");
    });

    // ── Test ───────────────────────────────────────────────────────────────

    [Fact]
    public void Test_RunsTheScriptInTheBox_OverTheSampleBlock_ShowsNumberedMessages_AndAnnouncesThem() => Sta.Run(() =>
    {
        var id = AddSet("Mío", GoodScript);
        using var form = Create();
        UiPump.Wait(form.Presenter.SelectSetAsync(id));
        Get<TextBox>(form, "_txtSample").Text = "Un orco llega.\r\nAna dice 'hola'\r\nBeto dice 'adios'\r\n";

        UiPump.Wait(form.RunTestAsync());

        var result = Get<TextBox>(form, "_txtResult");
        result.Text.Should().Be("Mensajes: 2\r\n1. Ana dice 'hola'\r\n2. Beto dice 'adios'");
        (result.ReadOnly, result.Multiline).Should().Be((true, true));
        _announcer.Received(1).Announce("Mensajes: 2. 1. Ana dice 'hola'. 2. Beto dice 'adios'", AnnouncePriority.MostRecent);
        Get<Button>(form, "_btnTest").Enabled.Should().BeTrue();

        Get<TextBox>(form, "_txtSample").Text = "Nada que ver.";
        UiPump.Wait(form.RunTestAsync());
        result.Text.Should().Be(Strings.MsgRules_ScriptTestNone);
        _announcer.Received(1).Announce(Strings.MsgRules_ScriptTestNone, AnnouncePriority.MostRecent);
    });

    [Fact]
    public void Test_UsesTheUnsavedText_AndAnErrorLeavesTheCaretOnItsLine() => Sta.Run(() =>
    {
        var id = AddSet("Mío", GoodScript);
        using var form = Create();
        UiPump.Wait(form.Presenter.SelectSetAsync(id));
        var box = Get<TextBox>(form, "_txtScript");
        box.Text = "local t = nil\r\nom.message(t.campo)";
        Get<TextBox>(form, "_txtSample").Text = "una linea";

        UiPump.Wait(form.RunTestAsync());

        Get<TextBox>(form, "_txtResult").Text.Should().StartWith("Error: ").And.Contain("line 2");
        box.SelectedText.Should().Be("om.message(t.campo)");
        _db.Rules.GetRuleSetByIdAsync(id).GetAwaiter().GetResult()!.Script.Should().Be(GoodScript, "testing never saves");
    });

    [Fact]
    public void Test_ABuiltInScript_WithAWrappedMessage() => Sta.Run(() =>
    {
        using var form = Create();
        UiPump.Wait(form.Presenter.SelectSetAsync(form.Presenter.Sets.Single(s => s.Name == "Callandor").Id));
        Get<TextBox>(form, "_txtSample").Text = "Ana dice 'hola, cuanto\r\ntiempo sin verte'\r\nUn orco llega.";

        UiPump.Wait(form.RunTestAsync());

        Get<TextBox>(form, "_txtResult").Text.Should().Be("Mensajes: 1\r\n1. Ana dice 'hola, cuanto\r\n   tiempo sin verte'");
    });

    [Fact]
    public void TheWindow_UsesTheEngineItIsGiven_AndDoesNotDisposeIt_ButDisposesItsOwn() => Sta.Run(() =>
    {
        var engine = Substitute.For<IScriptEngine>();
        var id = AddSet("Mío", GoodScript);
        using (var form = Create(engine))
        {
            UiPump.Wait(form.Presenter.SelectSetAsync(id));
            UiPump.Wait(form.SaveScriptAsync());
            engine.Received(1).Validate(Arg.Any<string>());
        }
        engine.DidNotReceive().Dispose();

        using var own = Create();
        own.Should().NotBeNull();
    });
}
