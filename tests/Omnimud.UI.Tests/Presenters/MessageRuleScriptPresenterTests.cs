using System.Globalization;
using Omnimud.Core.Scripting;
using Omnimud.Data.Entities;
using Omnimud.Data.Seed;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Tests.Presenters;

/// <summary>Rule sets of type Lua script: the tester (no WinForms) and the presenter, against a real SQLite file and real Lua.</summary>
public sealed class MessageRuleScriptPresenterTests : IDisposable
{
    private sealed class Dialogs : IMessageRulesDialogs
    {
        public Queue<NewMessageRuleSet?> NewSets { get; } = new();
        public Queue<string?> Names { get; } = new();
        public List<bool> InitialTypes { get; } = [];

        public string? AskSetName(string title, string initialName) => Names.Count > 0 ? Names.Dequeue() : null;

        public NewMessageRuleSet? AskNewSet(string title, string initialName, bool initialIsScript)
        {
            InitialTypes.Add(initialIsScript);
            return NewSets.Count > 0 ? NewSets.Dequeue() : null;
        }

        public bool EditRule(MessageRuleEditorModel model) => false;
    }

    private const string GoodScript = "for _, line in ipairs(om.lines) do\n  if om.match(line, [[ dice ']]) then om.message(line) end\nend";

    private readonly TempDatabase _db = new();
    private readonly ScriptedPrompts _prompts = new();
    private readonly Dialogs _dialogs = new();
    private readonly List<string> _spoken = [];
    private readonly LuaScriptEngine _engine = new();
    private readonly MessageRulesPresenter _presenter;
    private readonly MessageRuleScriptTester _tester;

    public MessageRuleScriptPresenterTests()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("es");
        _presenter = new MessageRulesPresenter(_db.Rules, _prompts, _dialogs, _spoken.Add, _engine);
        _tester = new MessageRuleScriptTester(_engine);
    }

    public void Dispose()
    {
        _engine.Dispose();
        _db.Dispose();
    }

    private async Task<int> OwnScriptSetAsync(string? script = GoodScript, string name = "Mío")
    {
        var id = await _db.Rules.AddRuleSetAsync(new MessageRuleSetEntity { Name = name, Script = script });
        await _presenter.LoadAsync();
        await _presenter.SelectSetAsync(id);
        return id;
    }

    // ── Tester ─────────────────────────────────────────────────────────────

    [Fact]
    public void Tester_Validate_EmptyIsAnError_SyntaxErrorsCarryTheirLine_GoodScriptsPass()
    {
        _tester.Validate("  \n ").Should().BeEquivalentTo(new { Field = MessageRuleScriptTester.FieldScript, Message = Strings.MsgRules_ScriptEmpty, Line = (int?)null });
        _tester.Validate(GoodScript).Should().BeNull();

        var issue = _tester.Validate("om.message('a')\nom.message('b')\nif then")!;
        issue.Line.Should().Be(3);
        issue.Message.Should().StartWith(string.Format(Strings.MsgRules_ScriptErrorAtLine, 3, string.Empty).TrimEnd());

        new MessageRuleScriptTester(null).Validate("esto no es lua (").Should().BeNull("without an engine there is nothing to check with");
    }

    [Fact]
    public void Tester_Template_IsAValidScript_InEveryLanguage()
    {
        foreach (var culture in new[] { "es", "en" })
        {
            Strings.Culture = CultureInfo.GetCultureInfo(culture);
            _tester.Validate(MessageRuleScriptTester.Template).Should().BeNull();
            MessageRuleScriptTester.Template.Should().Contain("om.lines").And.Contain("om.message");
        }
    }

    [Fact]
    public async Task Tester_Test_NumbersTheMessages_OnePerLine_AndIndentsWrappedOnes()
    {
        const string script = "om.message(om.lines[1] .. '\\n' .. om.lines[2]) om.message(om.lines[3])";

        var result = await _tester.TestAsync(script, "Ana dice 'hola\r\nque tal'\r\nBeto llega.\r\n");

        result.Success.Should().BeTrue();
        result.Messages.Should().Equal("Ana dice 'hola\nque tal'", "Beto llega.");
        result.Text.Should().Be("Mensajes: 2\n1. Ana dice 'hola\n   que tal'\n2. Beto llega.");
        result.ErrorLine.Should().BeNull();
    }

    [Fact]
    public async Task Tester_Test_TheSampleIsABlock_TheFinalLineBreakIsNotOneMoreLine()
    {
        (await _tester.TestAsync("om.message(#om.lines .. '')", "uno\ndos\n")).Messages.Should().Equal("2");
        (await _tester.TestAsync("om.message(#om.lines .. '')", "uno\n\ndos")).Messages.Should().Equal("3");
    }

    [Fact]
    public async Task Tester_Test_NoMessage_EmptySample_AndErrors()
    {
        (await _tester.TestAsync(GoodScript, "Nada que ver.")).Should().BeEquivalentTo(new { Success = true, Text = Strings.MsgRules_ScriptTestNone });
        (await _tester.TestAsync(GoodScript, "  ")).Should().BeEquivalentTo(new { Success = false, Text = Strings.MsgRule_TestEmpty });
        (await _tester.TestAsync(" ", "texto")).Should().BeEquivalentTo(new { Success = false, Text = Strings.MsgRules_ScriptEmpty });

        var syntax = await _tester.TestAsync("om.message('a')\nif then", "texto");
        syntax.Success.Should().BeFalse();
        syntax.ErrorLine.Should().Be(2);

        var runtime = await _tester.TestAsync("local t = nil\nom.message(t.x)", "texto");
        runtime.Success.Should().BeFalse();
        runtime.ErrorLine.Should().Be(2);
        runtime.Text.Should().StartWith("Error: ").And.Contain("line 2");

        var loop = await _tester.TestAsync("while true do end", "texto");
        loop.Success.Should().BeFalse();
        loop.Text.Should().StartWith("Error: ");

        var waiting = await _tester.TestAsync("om.sleep(5)", "texto");
        waiting.Text.Should().Contain("om.sleep");

        (await new MessageRuleScriptTester(null).TestAsync(GoodScript, "texto")).Text.Should().Be(Strings.MsgRules_ScriptTestUnavailable);
    }

    [Fact]
    public async Task Tester_Test_TheBuiltInCallandorScript_CapturesAWrappedMessage()
    {
        var result = await _tester.TestAsync(BuiltInMessageRuleSets.ScriptOf("Callandor"),
            "Un orco llega.\nAna dice 'hola, cuanto\ntiempo sin verte'\nEl orco se va.");

        result.Messages.Should().Equal("Ana dice 'hola, cuanto\ntiempo sin verte'");
    }

    // ── Presenter: type of the set ─────────────────────────────────────────

    [Fact]
    public async Task BuiltInSets_AreScripts_ReadOnly_ButCanBeTested()
    {
        await _presenter.LoadAsync();
        var callandor = _presenter.Sets.Single(s => s.Name == "Callandor");
        await _presenter.SelectSetAsync(callandor.Id);

        _presenter.IsScriptSet.Should().BeTrue();
        _presenter.ScriptDraft.Should().Be(BuiltInMessageRuleSets.ScriptOf("Callandor"));
        (_presenter.CanChangeType, _presenter.CanSaveScript, _presenter.IsScriptDirty).Should().Be((false, false, false));

        _presenter.ScriptDraft = "om.message('cambiado')";
        (await _presenter.SaveScriptAsync()).Should().BeFalse();
        _prompts.Infos.Should().ContainSingle().Which.Should().Contain("Callandor");
        await _presenter.SetTypeAsync(false);
        (await _db.Rules.GetRuleSetByIdAsync(callandor.Id))!.Script.Should().Be(BuiltInMessageRuleSets.ScriptOf("Callandor"));

        await _presenter.SelectSetAsync(_presenter.Sets.First(s => s.Id != callandor.Id).Id);
        await _presenter.SelectSetAsync(callandor.Id);
        var test = await _presenter.TestScriptAsync("Ana dice 'hola'\nOtra cosa.");
        test.Messages.Should().Equal("Ana dice 'hola'");
        _spoken.Last().Should().Be("Mensajes: 1. 1. Ana dice 'hola'");
    }

    [Fact]
    public async Task Duplicate_OfABuiltInSet_IsAnEditableScript()
    {
        await _presenter.LoadAsync();
        await _presenter.SelectSetAsync(_presenter.Sets.Single(s => s.Name == "Simauria").Id);
        _dialogs.Names.Enqueue("Mi Simauria");

        await _presenter.DuplicateSetAsync();

        _presenter.SelectedSet!.Name.Should().Be("Mi Simauria");
        (_presenter.IsScriptSet, _presenter.CanSaveScript, _presenter.CanChangeType).Should().Be((true, true, true));
        _presenter.ScriptDraft.Should().Be(BuiltInMessageRuleSets.ScriptOf("Simauria"));
        _presenter.Rules.Should().NotBeEmpty("the reference patterns travel with the copy");

        _presenter.ScriptDraft += "\n-- adaptado a mi MUD";
        (await _presenter.SaveScriptAsync()).Should().BeTrue();
        (await _db.Rules.GetRuleSetByIdAsync(_presenter.SelectedSet.Id))!.Script.Should().EndWith("-- adaptado a mi MUD");
    }

    [Fact]
    public async Task NewSet_AsksNameAndType_AScriptSetStartsFromTheTemplate()
    {
        await _presenter.LoadAsync();
        _dialogs.NewSets.Enqueue(new NewMessageRuleSet("Con script", true));
        await _presenter.AddSetAsync();

        _presenter.SelectedSet!.Name.Should().Be("Con script");
        _presenter.IsScriptSet.Should().BeTrue();
        _presenter.ScriptDraft.Should().Be(MessageRuleScriptTester.Template);
        _dialogs.InitialTypes.Should().Equal(false);

        _dialogs.NewSets.Enqueue(new NewMessageRuleSet("Con patrones", false));
        await _presenter.AddSetAsync();
        _presenter.IsScriptSet.Should().BeFalse();
        _presenter.SelectedSet!.Script.Should().BeNull();
    }

    [Fact]
    public async Task NewSet_DuplicateName_AsksAgain_RememberingTheChosenType()
    {
        await OwnScriptSetAsync(name: "Ocupado");
        _dialogs.NewSets.Enqueue(new NewMessageRuleSet("ocupado", true));
        _dialogs.NewSets.Enqueue(new NewMessageRuleSet("Libre", true));

        await _presenter.AddSetAsync();

        _dialogs.InitialTypes.Should().Equal(false, true);
        _prompts.Warnings.Should().ContainSingle();
        _presenter.SelectedSet!.Name.Should().Be("Libre");
        _presenter.IsScriptSet.Should().BeTrue();
    }

    [Fact]
    public async Task SetType_ToScript_StartsFromTheTemplate_AndIsAnnounced_KeepingTheRules()
    {
        var id = await _db.Rules.AddRuleSetAsync(new MessageRuleSetEntity { Name = "Mío" });
        await _db.Rules.AddRuleAsync(new MessageRuleEntity { RuleSetId = id, Pattern = "te dice" });
        await _presenter.LoadAsync();
        await _presenter.SelectSetAsync(id);
        var changes = 0;
        _presenter.Changed += () => changes++;

        await _presenter.SetTypeAsync(true);

        _presenter.IsScriptSet.Should().BeTrue();
        (await _db.Rules.GetRuleSetByIdAsync(id))!.Script.Should().Be(MessageRuleScriptTester.Template);
        _presenter.Rules.Should().ContainSingle();
        _spoken.Should().Equal(string.Format(Strings.MsgRules_TypeChangedScript, "Mío"));
        changes.Should().Be(1);

        await _presenter.SetTypeAsync(true); // already a script: nothing happens
        changes.Should().Be(1);
    }

    [Fact]
    public async Task SetType_BackToPatterns_AsksFirst_BecauseTheScriptIsDiscarded()
    {
        var id = await OwnScriptSetAsync();

        _prompts.ConfirmAnswers.Enqueue(false);
        await _presenter.SetTypeAsync(false);
        _presenter.IsScriptSet.Should().BeTrue();
        _prompts.Confirms.Should().ContainSingle().Which.Should().Be(string.Format(Strings.MsgRules_ConfirmDiscardScript, "Mío"));

        _prompts.ConfirmAnswers.Enqueue(true);
        await _presenter.SetTypeAsync(false);
        _presenter.IsScriptSet.Should().BeFalse();
        (await _db.Rules.GetRuleSetByIdAsync(id))!.Script.Should().BeNull();
        _spoken.Last().Should().Be(string.Format(Strings.MsgRules_TypeChangedPatterns, "Mío"));
    }

    // ── Presenter: saving ──────────────────────────────────────────────────

    [Fact]
    public async Task SaveScript_ValidatesWithLua_ASyntaxErrorIsAMessageWithTheLine_AndNothingIsSaved()
    {
        var id = await OwnScriptSetAsync();
        EditorIssue? rejected = null;
        _presenter.ScriptRejected += issue => rejected = issue;
        _presenter.ScriptDraft = "om.message('a')\r\nom.message('b')\r\nif then\r\n";

        (await _presenter.SaveScriptAsync()).Should().BeFalse();

        rejected!.Line.Should().Be(3);
        _prompts.Warnings.Should().ContainSingle().Which.Should().Be(rejected.Message).And.Contain("3");
        (await _db.Rules.GetRuleSetByIdAsync(id))!.Script.Should().Be(GoodScript);
        _presenter.IsScriptDirty.Should().BeTrue("the text stays in the box to be fixed");
    }

    [Fact]
    public async Task SaveScript_Empty_IsRejected_PointingToTheTypeSelector()
    {
        await OwnScriptSetAsync();
        _presenter.ScriptDraft = "   ";

        (await _presenter.SaveScriptAsync()).Should().BeFalse();

        _prompts.Warnings.Should().Equal(Strings.MsgRules_ScriptEmpty);
    }

    [Fact]
    public async Task SaveScript_Good_IsStoredWithUnixLineEnds_Announced_AndNoLongerDirty()
    {
        var id = await OwnScriptSetAsync();
        _presenter.ScriptDraft = "-- nuevo\r\nom.message(om.lines[1])\r\n";
        _presenter.IsScriptDirty.Should().BeTrue();

        (await _presenter.SaveScriptAsync()).Should().BeTrue();

        (await _db.Rules.GetRuleSetByIdAsync(id))!.Script.Should().Be("-- nuevo\nom.message(om.lines[1])\n");
        _presenter.IsScriptDirty.Should().BeFalse();
        _spoken.Should().Equal(string.Format(Strings.MsgRules_StatusScriptSaved, "Mío"));
        _presenter.Status.Should().Be(_spoken[0]);
    }

    [Fact]
    public async Task ScriptDraft_WithOnlyDifferentLineEnds_IsNotDirty()
    {
        await OwnScriptSetAsync();
        _presenter.ScriptDraft = GoodScript.Replace("\n", "\r\n");
        _presenter.IsScriptDirty.Should().BeFalse();
    }

    [Fact]
    public async Task LeavingASetWithUnsavedScript_OffersToSave_Yes_No_AndInvalid()
    {
        var id = await OwnScriptSetAsync();
        var other = _presenter.Sets.First(s => s.Id != id).Id;

        // No: the changes are dropped.
        _presenter.ScriptDraft = "om.message('descartado')";
        _prompts.ConfirmAnswers.Enqueue(false);
        await _presenter.SelectSetAsync(other);
        _presenter.SelectedSet!.Id.Should().Be(other);
        (await _db.Rules.GetRuleSetByIdAsync(id))!.Script.Should().Be(GoodScript);

        // Yes: saved, then the other set is selected.
        await _presenter.SelectSetAsync(id);
        _presenter.ScriptDraft = "om.message('guardado')";
        _prompts.ConfirmAnswers.Enqueue(true);
        await _presenter.SelectSetAsync(other);
        _presenter.SelectedSet!.Id.Should().Be(other);
        (await _db.Rules.GetRuleSetByIdAsync(id))!.Script.Should().Be("om.message('guardado')");

        // Yes, but it does not compile: stay where we were, with the text.
        await _presenter.SelectSetAsync(id);
        _presenter.ScriptDraft = "if then";
        _prompts.ConfirmAnswers.Enqueue(true);
        await _presenter.SelectSetAsync(other);
        _presenter.SelectedSet!.Id.Should().Be(id);
        _presenter.ScriptDraft.Should().Be("if then");
        (await _presenter.LeaveScriptAsync()).Should().BeFalse("closing the window asks the same question");
    }

    [Fact]
    public async Task Rename_KeepsTheScript()
    {
        var id = await OwnScriptSetAsync();
        _dialogs.Names.Enqueue("Otro nombre");

        await _presenter.RenameSetAsync();

        (await _db.Rules.GetRuleSetByIdAsync(id))!.Should().BeEquivalentTo(new { Name = "Otro nombre", Script = GoodScript });
    }

    // ── Presenter: test ────────────────────────────────────────────────────

    [Fact]
    public async Task TestScript_UsesTheDraft_EvenUnsaved_AndAnnouncesTheOutcome()
    {
        await OwnScriptSetAsync();
        _presenter.ScriptDraft = "om.message('borrador: ' .. om.lines[2])";

        var result = await _presenter.TestScriptAsync("uno\ndos");

        result.Messages.Should().Equal("borrador: dos");
        _spoken.Should().Equal("Mensajes: 1. 1. borrador: dos");

        _presenter.ScriptDraft = GoodScript;
        (await _presenter.TestScriptAsync("nada")).Text.Should().Be(Strings.MsgRules_ScriptTestNone);
        _spoken.Last().Should().Be(Strings.MsgRules_ScriptTestNone);

        _presenter.ScriptDraft = "om.message('a')\nif then";
        var broken = await _presenter.TestScriptAsync("uno");
        broken.ErrorLine.Should().Be(2);
        _spoken.Last().Should().Be(broken.Text);
    }
}
