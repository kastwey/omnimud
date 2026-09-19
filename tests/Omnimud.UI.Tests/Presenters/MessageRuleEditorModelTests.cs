using System.Globalization;
using Omnimud.Data.Entities;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Tests.Presenters;

public sealed class MessageRuleEditorModelTests
{
    public MessageRuleEditorModelTests() => Strings.Culture = CultureInfo.GetCultureInfo("es");

    private static MessageRuleEntity Rule(string pattern, string template = "$0", bool caseSensitive = false, string? channel = null, bool enabled = true) =>
        new() { Pattern = pattern, Template = template, CaseSensitive = caseSensitive, Channel = channel, Enabled = enabled };

    // ── Validation ─────────────────────────────────────────────────────────

    [Fact]
    public void NewRule_Defaults()
    {
        var model = new MessageRuleEditorModel();

        model.Should().BeEquivalentTo(new { Pattern = "", Template = "$0", CaseSensitive = false, Channel = "", Enabled = true, IsNew = true });
    }

    [Fact]
    public void Pattern_IsRequired()
    {
        new MessageRuleEditorModel().Validate()
            .Should().BeEquivalentTo(new FieldError<MessageRuleField>(MessageRuleField.Pattern, Strings.MsgRule_PatternRequired));
    }

    [Theory]
    [InlineData("(sin cerrar")]
    [InlineData("[a-")]
    [InlineData("*")]
    public void InvalidRegex_IsRejectedOnThePatternField_WithTheReason(string pattern)
    {
        var error = new MessageRuleEditorModel { Pattern = pattern }.Validate();

        error!.Field.Should().Be(MessageRuleField.Pattern);
        error.Message.Should().StartWith(Strings.MsgRule_PatternInvalid.Split('{')[0]);
        error.Message.Length.Should().BeGreaterThan(Strings.MsgRule_PatternInvalid.Length - 3, "the reason given by the regex engine is included");
    }

    [Fact]
    public void ValidRegex_IsAccepted() =>
        new MessageRuleEditorModel { Pattern = @"^(?<sender>\w+) te dice: (.+)$" }.Validate().Should().BeNull();

    // ── Entity mapping ─────────────────────────────────────────────────────

    [Fact]
    public void ToEntity_New_AppendsToTheGivenSet()
    {
        var model = new MessageRuleEditorModel { Pattern = "a", Template = "", Channel = "  chat ", CaseSensitive = true, Enabled = false };

        model.ToEntity(5).Should().BeEquivalentTo(new MessageRuleEntity
        {
            Id = 0, RuleSetId = 5, SortOrder = 0, Pattern = "a", Template = "$0", Channel = "chat", CaseSensitive = true, Enabled = false,
        });
    }

    [Fact]
    public void ToEntity_Edit_KeepsIdSetAndPosition()
    {
        var existing = new MessageRuleEntity { Id = 8, RuleSetId = 3, SortOrder = 4, Pattern = "viejo", Template = "$1", Channel = "c", Enabled = true };
        var model = new MessageRuleEditorModel(existing) { Pattern = "nuevo", Channel = "" };

        model.IsNew.Should().BeFalse();
        model.ToEntity(99).Should().BeEquivalentTo(new MessageRuleEntity
        {
            Id = 8, RuleSetId = 3, SortOrder = 4, Pattern = "nuevo", Template = "$1", Channel = null, Enabled = true,
        });
    }

    // ── Test ───────────────────────────────────────────────────────────────

    [Fact]
    public void Test_Match_ShowsTheMessageBuiltFromTheTemplate()
    {
        var model = new MessageRuleEditorModel { Pattern = @"^(\w+) te dice: (.+)$", Template = "$1: $2" };

        var result = model.Test("Gandalf te dice: corre, insensato");

        result.Matched.Should().BeTrue();
        result.Message.Should().Be("Gandalf: corre, insensato");
        result.Text.Should().Be(string.Format(Strings.MsgRule_TestMatch, "Gandalf: corre, insensato"));
    }

    [Fact]
    public void Test_NamedGroups_WholeMatch_AndChannel()
    {
        var model = new MessageRuleEditorModel { Pattern = @"^\[(?<canal>\w+)\] (?<quien>\w+): .+$", Template = "${quien} en ${canal} -> $0", Channel = "chat" };

        var result = model.Test("[novatos] Pepe: hola");

        result.Message.Should().Be("Pepe en novatos -> [novatos] Pepe: hola");
        result.Channel.Should().Be("chat");
        result.Text.Should().Contain("chat");
    }

    [Fact]
    public void Test_NoMatch_SaysSo()
    {
        var result = new MessageRuleEditorModel { Pattern = "^hola$" }.Test("adios");

        result.Matched.Should().BeFalse();
        result.Text.Should().Be(Strings.MsgRule_TestNoMatch);
    }

    [Fact]
    public void Test_HonoursCaseSensitivity()
    {
        new MessageRuleEditorModel { Pattern = "HOLA", CaseSensitive = false }.Test("hola").Matched.Should().BeTrue();
        new MessageRuleEditorModel { Pattern = "HOLA", CaseSensitive = true }.Test("hola").Matched.Should().BeFalse();
    }

    [Fact]
    public void Test_InvalidPattern_ShowsTheValidationError_InsteadOfThrowing()
    {
        var result = new MessageRuleEditorModel { Pattern = "(" }.Test("algo");

        result.Matched.Should().BeFalse();
        result.Text.Should().StartWith(Strings.MsgRule_PatternInvalid.Split('{')[0]);
    }

    [Fact]
    public void Test_EmptySample_AsksForText() =>
        new MessageRuleEditorModel { Pattern = "a" }.Test("  ").Text.Should().Be(Strings.MsgRule_TestEmpty);

    [Fact]
    public void Test_TriesADisabledRuleToo_BecauseItIsTheOneBeingEdited() =>
        new MessageRuleEditorModel { Pattern = "hola", Enabled = false }.Test("hola").Matched.Should().BeTrue();

    [Fact]
    public void Test_MultilineSample_IsTriedLineByLine()
    {
        var result = new MessageRuleEditorModel { Pattern = "^dos$" }.Test("uno\r\ndos\r\ntres");

        result.Matched.Should().BeTrue();
        result.Message.Should().Be("dos");
    }

    [Fact]
    public void Test_CatastrophicPattern_ReportsTheTimeout_AndDoesNotHang()
    {
        var model = new MessageRuleEditorModel { Pattern = @"^(a+)+$" };

        var result = model.Test(new string('a', 40) + "!");

        result.Matched.Should().BeFalse();
        result.Text.Should().BeOneOf(string.Format(Strings.MsgRule_TestTimeout, 1), Strings.MsgRule_TestNoMatch);
    }

    // ── A whole set ────────────────────────────────────────────────────────

    [Fact]
    public void Tester_FirstEnabledRuleThatMatchesWins_AndSaysWhichOne()
    {
        var rules = new[]
        {
            Rule("^nunca$"),
            Rule("hola", "desactivada", enabled: false),
            Rule("hola", "tercera: $0"),
            Rule("hola", "cuarta"),
        };

        var result = MessageRuleTester.Test(rules, "hola mundo");

        result.RuleNumber.Should().Be(3);
        result.Message.Should().Be("tercera: hola");
        result.Text.Should().Be(string.Format(Strings.MsgRule_TestMatchRule, 3, "tercera: hola"));
    }

    [Fact]
    public void Tester_SkipsBrokenRules_LikeASessionDoes()
    {
        var result = MessageRuleTester.Test([Rule("("), Rule("hola")], "hola");

        result.RuleNumber.Should().Be(2);
    }

    [Fact]
    public void Tester_NoRules_NoMatch() =>
        MessageRuleTester.Test([], "hola").Text.Should().Be(Strings.MsgRule_TestNoMatch);
}
