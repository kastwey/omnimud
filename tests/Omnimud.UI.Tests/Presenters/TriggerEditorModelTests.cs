using Omnimud.Core.Scripting;
using Omnimud.Core.Triggers;
using Omnimud.Data.Entities;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Tests.Presenters;

public sealed class TriggerEditorModelTests : IDisposable
{
    private readonly LuaScriptEngine _engine = new();

    public void Dispose() => _engine.Dispose();

    private static readonly TriggerEntity[] Existing =
    [
        new() { Id = "a", CharacterId = 3, Name = "Hambre", Pattern = "Tienes hambre", Action = "comer", SortOrder = 4 },
        new() { Id = "b", CharacterId = 3, Name = "Sed", Pattern = "Tienes sed", Action = "beber" },
    ];

    private static TriggerEditorModel Valid() => new(null, isNew: true, Existing)
    {
        Name = "Nuevo", Pattern = "llega alguien", Action = "saludar",
    };

    // ── What is available ──

    [Fact]
    public void NewTrigger_Defaults()
    {
        var model = new TriggerEditorModel(null, isNew: true);
        model.Enabled.Should().BeTrue();
        model.Priority.Should().Be(50);
        model.PatternType.Should().Be(PatternType.Literal);
        model.ActionChoice.Should().Be(TriggerActionChoice.SendCommand);
    }

    [Theory]
    [InlineData(TriggerActionChoice.SendCommand, true, false, false)]
    [InlineData(TriggerActionChoice.PlaySound, false, true, false)]
    [InlineData(TriggerActionChoice.Both, true, true, false)]
    [InlineData(TriggerActionChoice.LuaScript, true, false, true)]
    public void ActionChoice_DecidesWhichBoxesAreEnabled(TriggerActionChoice choice, bool action, bool sound, bool script)
    {
        var model = Valid();
        model.ActionChoice = choice;
        model.ActionEnabled.Should().Be(action);
        model.SoundEnabled.Should().Be(sound);
        model.IsScript.Should().Be(script);
    }

    [Fact]
    public void LuaScript_RenamesTheActionBox_LabelAndAccessibleName()
    {
        var model = Valid();
        model.ActionLabel.Should().Be(Strings.TrigEdit_ActionLabel);
        model.ActionAccessibleName.Should().Be(Strings.TrigEdit_ActionName);

        model.ActionChoice = TriggerActionChoice.LuaScript;
        model.ActionLabel.Should().Be(Strings.TrigEdit_ScriptLabel);
        model.ActionAccessibleName.Should().Be(Strings.TrigEdit_ScriptName);
    }

    [Fact]
    public void CommandTrigger_DisablesRegexMultilineAndHide_AndIsStoredAsPlainLiteral()
    {
        var model = Valid();
        model.PatternType = PatternType.Regex;
        model.Multiline = true;
        model.GagLine = true;
        model.Pattern = "@curar";

        model.IsCommandTrigger.Should().BeTrue();
        model.PatternTypeEnabled.Should().BeFalse();
        model.MultilineEnabled.Should().BeFalse();
        model.GagLineEnabled.Should().BeFalse();

        var entity = model.ToEntity();
        entity.PatternType.Should().Be((int)PatternType.Literal);
        entity.Multiline.Should().BeFalse();
        entity.GagLine.Should().BeFalse();
    }

    [Fact]
    public void Multiline_DisablesHideTheLine()
    {
        var model = Valid();
        model.GagLine = true;
        model.GagLineEnabled.Should().BeTrue();

        model.Multiline = true;
        model.GagLineEnabled.Should().BeFalse();
        model.ToEntity().GagLine.Should().BeFalse();
        model.ToEntity().Multiline.Should().BeTrue();
    }

    [Theory]
    [InlineData(TriggerActionType.SendCommand, TriggerActionChoice.SendCommand)]
    [InlineData(TriggerActionType.PlaySound, TriggerActionChoice.PlaySound)]
    [InlineData(TriggerActionType.SendCommandAndPlaySound, TriggerActionChoice.Both)]
    [InlineData(TriggerActionType.Script, TriggerActionChoice.LuaScript)]
    public void ActionChoice_MapsToTheStoredActionType_BothWays(TriggerActionType stored, TriggerActionChoice choice)
    {
        TriggerEditorModel.ToChoice(stored).Should().Be(choice);
        TriggerEditorModel.ToActionType(choice).Should().Be(stored);

        var loaded = new TriggerEditorModel(new TriggerEntity { Id = "x", Name = "n", Pattern = "p", Action = "a", ActionType = (int)stored }, isNew: false);
        loaded.ActionChoice.Should().Be(choice);
        loaded.ToEntity().ActionType.Should().Be((int)stored);
    }

    // ── Validation ──

    [Fact]
    public void Validate_ValidTrigger_HasNoIssue() => Valid().Validate(_engine).Should().BeNull();

    [Fact]
    public void Validate_EmptyName()
    {
        var model = Valid();
        model.Name = "  ";
        var issue = model.Validate(_engine);
        issue!.Field.Should().Be(TriggerEditorModel.FieldName);
        issue.Message.Should().Be(Strings.TrigEdit_ErrNameEmpty);
    }

    [Fact]
    public void Validate_DuplicateName_IgnoringCase_IsAMessageOnTheName()
    {
        var model = Valid();
        model.Name = "hambre";
        var issue = model.Validate(_engine);
        issue!.Field.Should().Be(TriggerEditorModel.FieldName);
        issue.Message.Should().Be(string.Format(Strings.TrigEdit_ErrDuplicateName, "hambre"));
    }

    [Fact]
    public void Validate_EditingATrigger_DoesNotCollideWithItself()
    {
        new TriggerEditorModel(Existing[0], isNew: false, Existing).Validate(_engine).Should().BeNull();
    }

    [Fact]
    public void Validate_EmptyPattern()
    {
        var model = Valid();
        model.Pattern = "";
        model.Validate(_engine)!.Should().BeEquivalentTo(new EditorIssue(TriggerEditorModel.FieldPattern, Strings.TrigEdit_ErrPatternEmpty));
    }

    [Theory]
    [InlineData("@", nameof(Strings.TrigEdit_ErrCommandEmpty))]
    [InlineData("@dos palabras", nameof(Strings.TrigEdit_ErrCommandSpaces))]
    public void Validate_CommandTrigger_NeedsOneWord(string pattern, string expectedKey)
    {
        var model = Valid();
        model.Pattern = pattern;
        var expected = expectedKey == nameof(Strings.TrigEdit_ErrCommandEmpty) ? Strings.TrigEdit_ErrCommandEmpty : Strings.TrigEdit_ErrCommandSpaces;
        model.Validate(_engine)!.Should().BeEquivalentTo(new EditorIssue(TriggerEditorModel.FieldPattern, expected));
    }

    [Fact]
    public void Validate_BrokenRegex_SaysWhy_OnThePattern()
    {
        var model = Valid();
        model.PatternType = PatternType.Regex;
        model.Pattern = "(sin cerrar";
        var issue = model.Validate(_engine);
        issue!.Field.Should().Be(TriggerEditorModel.FieldPattern);
        issue.Message.Should().StartWith(Strings.TrigEdit_ErrRegex.Split("{0}")[0]);
    }

    [Fact]
    public void Validate_BrokenRegex_IsIgnoredForLiteralsAndCommandTriggers()
    {
        var model = Valid();
        model.Pattern = "(sin cerrar";
        model.Validate(_engine).Should().BeNull();

        model.PatternType = PatternType.Regex;
        model.Pattern = "@(x";
        model.Validate(_engine).Should().BeNull();
    }

    [Fact]
    public void RegexError_CatastrophicPattern_DoesNotHang()
    {
        var model = Valid();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        _ = model.RegexError("^(a+)+$");
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Validate_CommandAction_IsRequired_AndSingleLine()
    {
        var model = Valid();
        model.Action = " ";
        model.Validate(_engine)!.Should().BeEquivalentTo(new EditorIssue(TriggerEditorModel.FieldAction, Strings.TrigEdit_ErrActionEmpty));

        model.Action = "uno\r\ndos";
        model.Validate(_engine)!.Message.Should().Be(Strings.TrigEdit_ErrActionLines);
    }

    [Fact]
    public void Validate_SoundOnly_NeedsTheSound_ButNotTheAction()
    {
        var model = Valid();
        model.ActionChoice = TriggerActionChoice.PlaySound;
        model.Action = "";
        model.Validate(_engine)!.Should().BeEquivalentTo(new EditorIssue(TriggerEditorModel.FieldSound, Strings.TrigEdit_ErrSoundEmpty));

        model.Sound = "alarma.wav";
        model.Validate(_engine).Should().BeNull();
    }

    [Fact]
    public void Validate_Both_NeedsActionAndSound()
    {
        var model = Valid();
        model.ActionChoice = TriggerActionChoice.Both;
        model.Validate(_engine)!.Field.Should().Be(TriggerEditorModel.FieldSound);
        model.Action = "";
        model.Validate(_engine)!.Field.Should().Be(TriggerEditorModel.FieldAction);
    }

    [Fact]
    public void Validate_EmptyScript()
    {
        var model = Valid();
        model.ActionChoice = TriggerActionChoice.LuaScript;
        model.Action = "";
        model.Validate(_engine)!.Message.Should().Be(Strings.TrigEdit_ErrScriptEmpty);
    }

    [Fact]
    public void Validate_LuaSyntaxError_ReportsTheLine()
    {
        var model = Valid();
        model.ActionChoice = TriggerActionChoice.LuaScript;
        model.Action = "om.send('uno')\r\nom.send('dos')\r\nif x then\r\nom.send(";

        var issue = model.Validate(_engine);

        issue!.Field.Should().Be(TriggerEditorModel.FieldAction);
        issue.Line.Should().Be(4);
        issue.Message.Should().Contain("4");
    }

    [Fact]
    public void Validate_ValidLua_Passes_AndMultilineScriptsAreAllowed()
    {
        var model = Valid();
        model.ActionChoice = TriggerActionChoice.LuaScript;
        model.Action = "local n = 1\r\nom.send('hola ' .. n)";
        model.Validate(_engine).Should().BeNull();
    }

    [Fact]
    public void Validate_WithoutEngine_DoesNotCheckTheScript()
    {
        var model = Valid();
        model.ActionChoice = TriggerActionChoice.LuaScript;
        model.Action = "this is not lua (";
        model.Validate(null).Should().BeNull();
    }

    [Theory]
    [InlineData("Script syntax error: line 12: unexpected symbol", 12)]
    [InlineData("Error de sintaxis del script: line 3: x", 3)]
    [InlineData("no position here", null)]
    public void ErrorLine_IsReadFromTheEngineMessage(string error, int? expected)
    {
        TriggerEditorModel.ErrorLine(error).Should().Be(expected);
    }

    [Theory]
    [InlineData("uno\r\ndos\r\ntres", 1, 0, 3)]
    [InlineData("uno\r\ndos\r\ntres", 2, 5, 3)]
    [InlineData("uno\r\ndos\r\ntres", 3, 10, 4)]
    [InlineData("uno\r\ndos", 9, 5, 3)]
    [InlineData("uno\ndos", 2, 4, 3)]
    public void LineSpan_FindsWhereTheCursorMustGo(string text, int line, int start, int length)
    {
        TriggerEditorModel.LineSpan(text, line).Should().Be((start, length));
    }

    [Fact]
    public void Warnings_SamePatternAsAnotherTrigger_AsksNamingIt()
    {
        var model = Valid();
        model.Pattern = "Tienes sed";
        model.Warnings().Should().ContainSingle().Which.Should().Be(string.Format(Strings.TrigEdit_WarnSamePattern, "Sed"));
        Valid().Warnings().Should().BeEmpty();
    }

    // ── Output ──

    [Fact]
    public void ToEntity_Editing_KeepsIdentityAndOrder()
    {
        var model = new TriggerEditorModel(Existing[0], isNew: false, Existing) { Name = " Hambre 2 ", Priority = 80, CaseSensitive = true };
        var entity = model.ToEntity();
        entity.Id.Should().Be("a");
        entity.CharacterId.Should().Be(3);
        entity.SortOrder.Should().Be(4);
        entity.Name.Should().Be("Hambre 2");
        entity.Priority.Should().Be(80);
        entity.CaseSensitive.Should().BeTrue();
    }

    [Fact]
    public void ToEntity_New_GetsAnId_AndAppends()
    {
        var entity = Valid().ToEntity();
        Guid.TryParse(entity.Id, out _).Should().BeTrue();
        entity.SortOrder.Should().Be(0);
    }

    [Fact]
    public void ToEntity_KeepsTheScriptExactlyAsTyped_AndEmptySoundIsNull()
    {
        var model = Valid();
        model.ActionChoice = TriggerActionChoice.LuaScript;
        model.Action = "  if true then\r\n\tom.send('x')\r\nend\r\n";
        model.Sound = "  ";
        var entity = model.ToEntity();
        entity.Action.Should().Be(model.Action);
        entity.Sound.Should().BeNull();
    }

    [Fact]
    public void Priority_OutOfRangeInTheDatabase_IsClamped()
    {
        var model = new TriggerEditorModel(new TriggerEntity { Id = "x", Name = "n", Pattern = "p", Action = "a", Priority = 99999 }, isNew: false);
        model.Priority.Should().Be(TriggerEditorModel.MaxPriority);
    }
}
