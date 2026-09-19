using System.Globalization;
using NSubstitute;
using Omnimud.Data.Entities;
using Omnimud.Data.Repositories;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Tests.Presenters;

/// <summary>Against a real SQLite file: the four built-in sets come from the migrations.</summary>
public sealed class MessageRulesPresenterTests : IDisposable
{
    private sealed class Dialogs : IMessageRulesDialogs
    {
        public Queue<string?> Names { get; } = new();
        public List<string> InitialNames { get; } = [];
        public Func<MessageRuleEditorModel, bool> OnEditRule { get; set; } = _ => false;

        public string? AskSetName(string title, string initialName)
        {
            InitialNames.Add(initialName);
            return Names.Count > 0 ? Names.Dequeue() : null;
        }

        public bool EditRule(MessageRuleEditorModel model) => OnEditRule(model);
    }

    private readonly TempDatabase _db = new();
    private readonly ScriptedPrompts _prompts = new();
    private readonly Dialogs _dialogs = new();
    private readonly List<string> _spoken = [];
    private readonly MessageRulesPresenter _presenter;

    public MessageRulesPresenterTests()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("es");
        _presenter = new MessageRulesPresenter(_db.Rules, _prompts, _dialogs, _spoken.Add);
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> OwnSetAsync(string name = "Mío", params string[] patterns)
    {
        var id = await _db.Rules.AddRuleSetAsync(new MessageRuleSetEntity { Name = name });
        foreach (var pattern in patterns)
            await _db.Rules.AddRuleAsync(new MessageRuleEntity { RuleSetId = id, Pattern = pattern });
        await _presenter.LoadAsync();
        await _presenter.SelectSetAsync(id);
        return id;
    }

    private IEnumerable<string> Patterns => _presenter.Rules.Select(r => r.Pattern);

    // ── Loading and selection ──────────────────────────────────────────────

    [Fact]
    public async Task Load_ListsTheBuiltInSetsByName_AndSelectsTheFirst_NeverNothing()
    {
        await _presenter.LoadAsync();

        _presenter.Sets.Should().HaveCount(4).And.OnlyContain(s => s.IsBuiltIn);
        _presenter.Sets.Select(s => s.Name).Should().BeInAscendingOrder(StringComparer.CurrentCultureIgnoreCase);
        _presenter.SelectedSet.Should().Be(_presenter.Sets[0]);
        _presenter.Rules.Should().NotBeEmpty();
        _presenter.SelectedRuleIndex.Should().Be(0);
    }

    [Fact]
    public async Task BuiltInSets_AreMarkedInTheirDisplayName()
    {
        await _presenter.LoadAsync();

        MessageRulesPresenter.DisplayName(_presenter.Sets[0]).Should().Be(string.Format(Strings.MsgRules_SetBuiltIn, _presenter.Sets[0].Name));
        MessageRulesPresenter.DisplayName(new MessageRuleSetEntity { Name = "Mío" }).Should().Be("Mío");
    }

    // ── Built-in sets: only duplicate ──────────────────────────────────────

    [Fact]
    public async Task BuiltInSet_CanOnlyBeDuplicated()
    {
        await _presenter.LoadAsync();

        _presenter.CanDuplicateSet.Should().BeTrue();
        _presenter.CanModifySet.Should().BeFalse();
        _presenter.CanAddRule.Should().BeFalse();
        _presenter.CanModifyRule.Should().BeFalse();
        _presenter.CanMoveUp.Should().BeFalse();
        _presenter.CanMoveDown.Should().BeFalse();
    }

    [Fact]
    public async Task BuiltInSet_EveryChangeIsRefusedWithAnExplanation_AndNothingIsWritten()
    {
        await _presenter.LoadAsync();
        var set = _presenter.SelectedSet!;
        var before = (await _db.Rules.GetRulesAsync(set.Id)).Select(r => (r.Pattern, r.Enabled)).ToList();
        _dialogs.Names.Enqueue("Otro nombre");
        _dialogs.OnEditRule = _ => throw new InvalidOperationException("The editor must not open for a built-in set.");

        await _presenter.RenameSetAsync();
        await _presenter.RemoveSetAsync();
        await _presenter.AddRuleAsync();
        await _presenter.EditRuleAsync();
        await _presenter.RemoveRuleAsync();
        await _presenter.MoveDownAsync();
        await _presenter.ToggleRuleAsync();

        _prompts.Infos.Should().HaveCount(7).And.OnlyContain(m => m == string.Format(Strings.MsgRules_BuiltInReadOnly, set.Name));
        _prompts.Confirms.Should().BeEmpty();
        (await _db.Rules.GetRuleSetByIdAsync(set.Id))!.Name.Should().Be(set.Name);
        (await _db.Rules.GetRulesAsync(set.Id)).Select(r => (r.Pattern, r.Enabled)).Should().Equal(before);
    }

    [Fact]
    public async Task Duplicate_BuiltIn_CreatesAnEditableCopy_Selected_WithTheSameRules()
    {
        await _presenter.LoadAsync();
        var source = _presenter.SelectedSet!;
        var rules = _presenter.Rules.Select(r => r.Pattern).ToList();
        _dialogs.Names.Enqueue("Mi copia");

        await _presenter.DuplicateSetAsync();

        _dialogs.InitialNames.Should().Equal(string.Format(Strings.MsgRules_CopyName, source.Name));
        _presenter.SelectedSet!.Name.Should().Be("Mi copia");
        _presenter.SelectedSet.IsBuiltIn.Should().BeFalse();
        _presenter.CanModifySet.Should().BeTrue();
        Patterns.Should().Equal(rules);
    }

    // ── Sets ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddSet_SelectsIt_AndReportsIt()
    {
        await _presenter.LoadAsync();
        _dialogs.Names.Enqueue("  Nuevo  ");
        var changes = 0;
        _presenter.Changed += () => changes++;

        await _presenter.AddSetAsync();

        _presenter.SelectedSet!.Name.Should().Be("Nuevo");
        _presenter.Rules.Should().BeEmpty();
        _presenter.SelectedRuleIndex.Should().Be(-1);
        _presenter.Status.Should().Be(string.Format(Strings.MsgRules_StatusSetAdded, "Nuevo"));
        changes.Should().Be(1);
    }

    [Fact]
    public async Task AddSet_Cancelled_ChangesNothing()
    {
        await _presenter.LoadAsync();

        await _presenter.AddSetAsync();

        _presenter.Sets.Should().HaveCount(4);
    }

    [Fact]
    public async Task AddSet_DuplicateName_IsAMessage_AndAsksAgainWithWhatWasTyped()
    {
        await _presenter.LoadAsync();
        var taken = _presenter.Sets[0].Name;
        _dialogs.Names.Enqueue(taken.ToUpperInvariant());
        _dialogs.Names.Enqueue("Libre");

        await _presenter.AddSetAsync();

        _prompts.Warnings.Should().Equal(string.Format(Strings.MsgRules_DuplicateName, taken.ToUpperInvariant()));
        _dialogs.InitialNames.Should().Equal("", taken.ToUpperInvariant());
        _presenter.SelectedSet!.Name.Should().Be("Libre");
    }

    [Fact]
    public async Task AddSet_BlankName_IsAMessage_AndAsksAgain()
    {
        await _presenter.LoadAsync();
        _dialogs.Names.Enqueue("   ");
        _dialogs.Names.Enqueue(null);

        await _presenter.AddSetAsync();

        _prompts.Warnings.Should().Equal(Strings.MsgRules_NameRequired);
        _presenter.Sets.Should().HaveCount(4);
    }

    [Fact]
    public async Task Rename_KeepsTheSetSelected_WithItsRules()
    {
        var id = await OwnSetAsync("Mío", "uno", "dos");
        _dialogs.Names.Enqueue("Tuyo");

        await _presenter.RenameSetAsync();

        _dialogs.InitialNames.Should().Equal("Mío");
        _presenter.SelectedSet.Should().BeEquivalentTo(new { Id = id, Name = "Tuyo", IsBuiltIn = false });
        Patterns.Should().Equal("uno", "dos");
    }

    [Fact]
    public async Task RemoveSet_AsksFirst_AndSelectsTheNeighbour()
    {
        await OwnSetAsync("Mío");
        var index = _presenter.Sets.ToList().FindIndex(s => s.Name == "Mío");

        await _presenter.RemoveSetAsync();

        _prompts.Confirms.Should().Equal(string.Format(Strings.MsgRules_ConfirmRemoveSet, "Mío"));
        _presenter.Sets.Should().HaveCount(4);
        _presenter.SelectedSet.Should().Be(_presenter.Sets[Math.Min(index, 3)]);
    }

    [Fact]
    public async Task RemoveSet_AnsweringNo_KeepsIt()
    {
        await OwnSetAsync("Mío");
        _prompts.DefaultConfirm = false;

        await _presenter.RemoveSetAsync();

        _presenter.Sets.Should().HaveCount(5);
        _presenter.SelectedSet!.Name.Should().Be("Mío");
    }

    // ── Rules ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddRule_AppendsAtTheEnd_AndSelectsIt()
    {
        await OwnSetAsync("Mío", "uno", "dos");
        _dialogs.OnEditRule = m => { m.Pattern = "tres"; m.Template = "$0!"; m.Channel = "chat"; return true; };

        await _presenter.AddRuleAsync();

        Patterns.Should().Equal("uno", "dos", "tres");
        _presenter.SelectedRuleIndex.Should().Be(2);
        _presenter.SelectedRule.Should().BeEquivalentTo(new { Pattern = "tres", Template = "$0!", Channel = "chat", Enabled = true });
    }

    [Fact]
    public async Task EditRule_KeepsPositionAndSelection()
    {
        await OwnSetAsync("Mío", "uno", "dos", "tres");
        _presenter.SelectRule(1);
        _dialogs.OnEditRule = m => { m.Pattern.Should().Be("dos"); m.Pattern = "DOS"; m.CaseSensitive = true; return true; };

        await _presenter.EditRuleAsync();

        Patterns.Should().Equal("uno", "DOS", "tres");
        _presenter.SelectedRuleIndex.Should().Be(1);
        _presenter.SelectedRule!.CaseSensitive.Should().BeTrue();
    }

    [Fact]
    public async Task EditRule_Cancelled_ChangesNothing()
    {
        await OwnSetAsync("Mío", "uno");
        _dialogs.OnEditRule = m => { m.Pattern = "otro"; return false; };

        await _presenter.EditRuleAsync();

        Patterns.Should().Equal("uno");
    }

    [Theory]
    [InlineData(0, "dos")]
    [InlineData(1, "tres")]
    [InlineData(2, "dos")]
    public async Task RemoveRule_SelectsTheNeighbour(int remove, string expectedSelected)
    {
        await OwnSetAsync("Mío", "uno", "dos", "tres");
        _presenter.SelectRule(remove);

        await _presenter.RemoveRuleAsync();

        _presenter.Rules.Should().HaveCount(2);
        _presenter.SelectedRule!.Pattern.Should().Be(expectedSelected);
    }

    [Fact]
    public async Task RemoveRule_TheLastOne_LeavesNoSelection()
    {
        await OwnSetAsync("Mío", "uno");

        await _presenter.RemoveRuleAsync();

        _presenter.Rules.Should().BeEmpty();
        _presenter.SelectedRuleIndex.Should().Be(-1);
        _presenter.CanModifyRule.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveRule_AnsweringNo_KeepsIt()
    {
        await OwnSetAsync("Mío", "uno");
        _prompts.DefaultConfirm = false;

        await _presenter.RemoveRuleAsync();

        Patterns.Should().Equal("uno");
    }

    [Fact]
    public async Task Move_ChangesTheEvaluationOrder_FollowsTheRule_AndAnnouncesThePosition()
    {
        var id = await OwnSetAsync("Mío", "uno", "dos", "tres");
        _presenter.SelectRule(0);

        await _presenter.MoveDownAsync();
        await _presenter.MoveDownAsync();

        Patterns.Should().Equal("dos", "tres", "uno");
        _presenter.SelectedRule!.Pattern.Should().Be("uno");
        (await _db.Rules.GetRulesAsync(id)).Select(r => r.Pattern).Should().Equal("dos", "tres", "uno");
        _spoken.Should().Equal(string.Format(Strings.MsgRules_StatusRuleMoved, 2, 3), string.Format(Strings.MsgRules_StatusRuleMoved, 3, 3));

        await _presenter.MoveUpAsync();
        Patterns.Should().Equal("dos", "uno", "tres");
    }

    [Fact]
    public async Task Move_PastTheEnds_OnlyAnnouncesIt()
    {
        await OwnSetAsync("Mío", "uno", "dos");
        _presenter.SelectRule(0);
        await _presenter.MoveUpAsync();
        _presenter.SelectRule(1);
        await _presenter.MoveDownAsync();

        Patterns.Should().Equal("uno", "dos");
        _spoken.Should().Equal(Strings.MsgRules_AlreadyFirst, Strings.MsgRules_AlreadyLast);
    }

    [Fact]
    public async Task CanMove_DependsOnThePosition()
    {
        await OwnSetAsync("Mío", "uno", "dos");

        _presenter.SelectRule(0);
        (_presenter.CanMoveUp, _presenter.CanMoveDown).Should().Be((false, true));
        _presenter.SelectRule(1);
        (_presenter.CanMoveUp, _presenter.CanMoveDown).Should().Be((true, false));
    }

    [Fact]
    public async Task Toggle_DisablesAndEnables_Persisting_AndAnnouncingEachChange()
    {
        var id = await OwnSetAsync("Mío", "uno", "dos");
        _presenter.SelectRule(1);

        await _presenter.ToggleRuleAsync();
        (await _db.Rules.GetRulesAsync(id))[1].Enabled.Should().BeFalse();
        _presenter.SelectedRuleIndex.Should().Be(1);

        await _presenter.ToggleRuleAsync();
        (await _db.Rules.GetRulesAsync(id))[1].Enabled.Should().BeTrue();

        _spoken.Should().Equal(Strings.MsgRules_RuleDisabled, Strings.MsgRules_RuleEnabled);
        _presenter.Status.Should().Be(Strings.MsgRules_RuleEnabled);
    }

    [Fact]
    public async Task TestSet_UsesTheEnabledRulesInOrder()
    {
        await OwnSetAsync("Mío", "^nada$", "hola");
        _presenter.TestSet("hola mundo").RuleNumber.Should().Be(2);

        _presenter.SelectRule(1);
        await _presenter.ToggleRuleAsync();

        _presenter.TestSet("hola mundo").Text.Should().Be(Strings.MsgRule_TestNoMatch);
    }

    [Fact]
    public void TestSet_WithoutSets_SaysSo()
    {
        var empty = Substitute.For<IMessageRuleRepository>();
        var presenter = new MessageRulesPresenter(empty, _prompts, _dialogs);

        presenter.TestSet("hola").Text.Should().Be(Strings.MsgRules_NoSetSelected);
    }

    [Fact]
    public async Task EmptyDatabase_NoSelection_AndNothingIsEnabled()
    {
        var empty = Substitute.For<IMessageRuleRepository>();
        empty.GetRuleSetsAsync().Returns([]);
        var presenter = new MessageRulesPresenter(empty, _prompts, _dialogs);

        await presenter.LoadAsync();
        await presenter.DuplicateSetAsync();
        await presenter.RemoveSetAsync();
        await presenter.AddRuleAsync();

        presenter.SelectedSet.Should().BeNull();
        presenter.CanDuplicateSet.Should().BeFalse();
        presenter.CanAddRule.Should().BeFalse();
        await empty.DidNotReceiveWithAnyArgs().AddRuleAsync(default!);
    }
}
