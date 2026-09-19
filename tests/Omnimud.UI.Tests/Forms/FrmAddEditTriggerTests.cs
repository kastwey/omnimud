using Omnimud.Core.Scripting;
using Omnimud.Core.Triggers;
using Omnimud.Data.Entities;
using Omnimud.UI.Forms;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Tests.Presenters;

namespace Omnimud.UI.Tests.Forms;

public sealed class FrmAddEditTriggerTests : IDisposable
{
    private readonly RecordingPrompts _prompts = new();
    private readonly RecordingAnnouncer _announcer = new();
    private readonly LuaScriptEngine _engine = new();

    public void Dispose() => _engine.Dispose();

    private static readonly TriggerEntity[] Existing =
        [new() { Id = "a", CharacterId = 1, Name = "Hambre", Pattern = "Tienes hambre", Action = "comer" }];

    private FrmAddEditTrigger Create(TriggerEntity? existing = null) =>
        new(new TriggerEditorModel(existing, existing is null, Existing), _prompts, _engine, _announcer);

    private static void Fill(FrmAddEditTrigger form, string name = "Nuevo", string pattern = "llega alguien", string action = "saludar")
    {
        form.Find<TextBox>("_txtName").Text = name;
        form.Find<TextBox>("_txtPattern").Text = pattern;
        form.Find<TextBox>("_txtAction").Text = action;
    }

    private static void Choose(FrmAddEditTrigger form, TriggerActionChoice choice) =>
        form.Find<ComboBox>("_cboActionType").SelectedIndex = (int)choice;

    // ── Accessibility in every relevant state ──

    public static TheoryData<string, int> CulturesAndChoices()
    {
        var data = new TheoryData<string, int>();
        foreach (var culture in ListFormsTestSupport.Cultures)
            for (var choice = 0; choice < 4; choice++)
                data.Add(culture, choice);
        return data;
    }

    [Theory]
    [MemberData(nameof(CulturesAndChoices))]
    public void PassesTheAccessibilityAudit_ForEveryKindOfAction(string culture, int choice) => Sta.Run(() =>
    {
        ListFormsTestSupport.AssertAccessible(culture, () => Create(), form => Choose(form, (TriggerActionChoice)choice));
    });

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void PassesTheAccessibilityAudit_AsCommandTrigger_Editing_AndWithTheCompatibleConstructor(string culture) => Sta.Run(() =>
    {
        ListFormsTestSupport.AssertAccessible(culture, () => Create(), form => form.Find<TextBox>("_txtPattern").Text = "@curar");
        ListFormsTestSupport.AssertAccessible(culture, () => Create(Existing[0]));
        ListFormsTestSupport.AssertAccessible(culture, () => new FrmAddEditTrigger());
        ListFormsTestSupport.AssertAccessible(culture, () => new FrmAddEditTrigger(Existing[0]));
    });

    [Fact]
    public void NoFocusableControl_CarriesAnAccessibleDescription() => Sta.Run(() =>
    {
        using var form = Create();
        Choose(form, TriggerActionChoice.LuaScript);
        foreach (Control control in form.Controls)
            control.AccessibleDescription.Should().BeNullOrEmpty(control.Name);
    });

    // ── What is enabled ──

    [Theory]
    [InlineData(TriggerActionChoice.SendCommand, true, false)]
    [InlineData(TriggerActionChoice.PlaySound, false, true)]
    [InlineData(TriggerActionChoice.Both, true, true)]
    [InlineData(TriggerActionChoice.LuaScript, true, false)]
    public void ActionKind_EnablesActionAndOrSound(TriggerActionChoice choice, bool action, bool sound) => Sta.Run(() =>
    {
        using var form = Create();
        Choose(form, choice);
        form.Find<TextBox>("_txtAction").Enabled.Should().Be(action);
        form.Find<TextBox>("_txtSound").Enabled.Should().Be(sound);
        form.Find<Button>("_btnBrowse").Enabled.Should().Be(sound);
    });

    [Fact]
    public void LuaScript_RenamesLabelAndAccessibleName_AndTurnsTheBoxIntoACodeEditor_ThenBack() => Sta.Run(() =>
    {
        ListFormsTestSupport.UseCulture("es");
        using var form = Create();
        var box = form.Find<TextBox>("_txtAction");
        var label = form.Find<Label>("_lblAction");
        var hint = form.Find<Label>("_lblTabHint");

        label.Text.Should().Be("&Acción:");
        box.AccessibleName.Should().Be("Acción");
        box.AcceptsTab.Should().BeFalse();
        box.AcceptsReturn.Should().BeFalse();

        Choose(form, TriggerActionChoice.LuaScript);
        label.Text.Should().Be("&Script Lua:");
        box.AccessibleName.Should().Be("Script Lua");
        box.Multiline.Should().BeTrue();
        box.AcceptsTab.Should().BeTrue();
        box.AcceptsReturn.Should().BeTrue();
        box.Font.FontFamily.Name.Should().Be("Consolas");
        box.Height.Should().BeGreaterThan(100);
        hint.Text.Should().Contain("Ctrl+Tab");
        box.AccessibleDescription.Should().BeNullOrEmpty();

        Choose(form, TriggerActionChoice.SendCommand);
        label.Text.Should().Be("&Acción:");
        box.AccessibleName.Should().Be("Acción");
        box.AcceptsTab.Should().BeFalse();
    });

    [Fact]
    public void CommandTrigger_DisablesRegexMultilineAndHide_Explains_AndAnnouncesOnce_ThenRestores() => Sta.Run(() =>
    {
        using var form = Create();
        var type = form.Find<ComboBox>("_cboPatternType");
        var multiline = form.Find<CheckBox>("_chkMultiline");
        var gag = form.Find<CheckBox>("_chkGag");
        var info = form.Find<Label>("_lblCommandInfo");
        type.SelectedIndex = (int)PatternType.Regex;
        gag.Checked = true;

        form.Find<TextBox>("_txtPattern").Text = "@c";
        form.Find<TextBox>("_txtPattern").Text = "@curar";

        type.Enabled.Should().BeFalse();
        type.SelectedIndex.Should().Be((int)PatternType.Literal);
        multiline.Enabled.Should().BeFalse();
        gag.Enabled.Should().BeFalse();
        gag.Checked.Should().BeFalse();
        info.Text.Should().Be(Strings.TrigEdit_CommandInfo);
        _announcer.Spoken.Should().Equal(Strings.TrigEdit_CommandInfo);

        form.Find<TextBox>("_txtPattern").Text = "curar";
        type.Enabled.Should().BeTrue();
        type.SelectedIndex.Should().Be((int)PatternType.Regex, "the user's choice comes back");
        gag.Enabled.Should().BeTrue();
        gag.Checked.Should().BeTrue();
        info.Text.Should().BeEmpty();
    });

    [Fact]
    public void Multiline_DisablesHideTheLine() => Sta.Run(() =>
    {
        using var form = Create();
        var gag = form.Find<CheckBox>("_chkGag");
        gag.Checked = true;

        form.Find<CheckBox>("_chkMultiline").Checked = true;
        gag.Enabled.Should().BeFalse();
        gag.Checked.Should().BeFalse();

        form.Find<CheckBox>("_chkMultiline").Checked = false;
        gag.Enabled.Should().BeTrue();
    });

    [Fact]
    public void PlaySoundButton_OnlyExistsWithAPlayer() => Sta.Run(() =>
    {
        using var form = Create();
        form.Find<Button>("_btnPlaySound").Visible.Should().BeFalse();
    });

    [Fact]
    public void Browse_PutsTheChosenFileInTheSoundBox() => Sta.Run(() =>
    {
        _prompts.OpenFile = @"C:\sonidos\ding.wav";
        using var form = Create();
        Choose(form, TriggerActionChoice.PlaySound);
        form.Find<Button>("_btnBrowse").Press();
        form.Find<TextBox>("_txtSound").Text.Should().Be(@"C:\sonidos\ding.wav");
    });

    // ── Accepting ──

    [Fact]
    public void Editing_LoadsEveryField() => Sta.Run(() =>
    {
        var entity = new TriggerEntity
        {
            Id = "z", Name = "Bloque", Pattern = "^Salidas", PatternType = 1, Action = "om.send('x')\nom.send('y')", ActionType = 2,
            Sound = "s.wav", CaseSensitive = true, Multiline = true, Priority = 70, Enabled = false,
        };
        using var form = Create(entity);

        form.Text.Should().Be(Strings.TrigEdit_EditTitle);
        form.Find<TextBox>("_txtAction").Lines.Should().Equal("om.send('x')", "om.send('y')");
        var stored = form.Entity;
        stored.Should().BeEquivalentTo(entity, o => o.Excluding(t => t.UpdatedAt).Excluding(t => t.CreatedAt).Excluding(t => t.Action));
        stored.Action.ReplaceLineEndings("\n").Should().Be(entity.Action);
    });

    [Fact]
    public void ValidTrigger_IsAccepted_WithTheNewFields() => Sta.Run(() =>
    {
        using var form = Create();
        Fill(form);
        form.Find<CheckBox>("_chkGag").Checked = true;
        form.Find<NumericUpDown>("_nudPriority").Value = 80;

        form.Find<Button>("_btnOk").Press();

        form.DialogResult.Should().Be(DialogResult.OK);
        form.Entity.GagLine.Should().BeTrue();
        form.GagLine.Should().BeTrue();
        form.Priority.Should().Be(80);
        form.TriggerName.Should().Be("Nuevo");
        _prompts.Messages.Should().BeEmpty();
    });

    [Fact]
    public void DuplicateName_MessageAndFocusOnTheName() => Sta.Run(() =>
    {
        using var form = Create();
        Fill(form, name: "hambre");

        form.TryAccept().Should().BeFalse();

        _prompts.Warnings.Should().Equal(string.Format(Strings.TrigEdit_ErrDuplicateName, "hambre"));
        form.ActiveControl.Should().BeSameAs(form.Find<TextBox>("_txtName"));
    });

    [Fact]
    public void BrokenRegex_MessageAndFocusOnThePattern() => Sta.Run(() =>
    {
        using var form = Create();
        Fill(form, pattern: "(sin cerrar");
        form.Find<ComboBox>("_cboPatternType").SelectedIndex = (int)PatternType.Regex;

        form.TryAccept().Should().BeFalse();

        _prompts.Warnings.Should().ContainSingle();
        form.ActiveControl.Should().BeSameAs(form.Find<TextBox>("_txtPattern"));
        form.DialogResult.Should().Be(DialogResult.None);
    });

    [Fact]
    public void SoundKind_WithoutSound_FocusOnTheSound() => Sta.Run(() =>
    {
        using var form = Create();
        Fill(form, action: "");
        Choose(form, TriggerActionChoice.PlaySound);

        form.TryAccept().Should().BeFalse();

        _prompts.Warnings.Should().Equal(Strings.TrigEdit_ErrSoundEmpty);
        form.ActiveControl.Should().BeSameAs(form.Find<TextBox>("_txtSound"));
    });

    [Fact]
    public void LuaError_ShowsTheLine_AndPutsTheCursorOnIt() => Sta.Run(() =>
    {
        using var form = Create();
        Choose(form, TriggerActionChoice.LuaScript);
        Fill(form, action: "om.send('uno')\r\nom.send('dos')\r\nlocal = 3\r\nom.send('cuatro')");
        var box = form.Find<TextBox>("_txtAction");

        form.TryAccept().Should().BeFalse();

        _prompts.Warnings.Should().ContainSingle().Which.Should().StartWith(string.Format(Strings.TrigEdit_ErrScriptAtLine, 3, "").TrimEnd());
        form.ActiveControl.Should().BeSameAs(box);
        box.SelectionStart.Should().Be(box.Text.IndexOf("local", StringComparison.Ordinal));
        box.SelectedText.Should().Be("local = 3");
    });

    [Fact]
    public void SamePatternAsAnotherTrigger_IsAQuestion() => Sta.Run(() =>
    {
        _prompts.DefaultConfirm = false;
        using var form = Create();
        Fill(form, pattern: "Tienes hambre");

        form.TryAccept().Should().BeFalse();
        _prompts.Confirms.Should().Equal(string.Format(Strings.TrigEdit_WarnSamePattern, "Hambre"));
    });

    // ── Test button ──

    [Fact]
    public void Test_Regex_ShowsCapturesAndTheResultingCommand_AndAnnouncesIt() => Sta.Run(() =>
    {
        using var form = Create();
        Fill(form, pattern: @"^(\w+) llega", action: "saludar %1");
        form.Find<ComboBox>("_cboPatternType").SelectedIndex = (int)PatternType.Regex;
        form.Find<TextBox>("_txtSample").Text = "Gimli llega desde el norte.";

        StaPump.Wait(form.TestAsync());

        var result = form.Find<TextBox>("_txtResult");
        result.ReadOnly.Should().BeTrue();
        result.Lines.Should().Equal(Strings.TrigTest_Matches, string.Format(Strings.TrigTest_Capture, 1, "Gimli"), string.Format(Strings.TrigTest_Command, "saludar Gimli"));
        _announcer.Spoken.Should().ContainSingle().Which.Should().Contain("saludar Gimli");
        form.Find<Button>("_btnTest").Enabled.Should().BeTrue();
        _prompts.Messages.Should().BeEmpty();
    });

    [Fact]
    public void Test_NoMatch_AndNoSample() => Sta.Run(() =>
    {
        using var form = Create();
        Fill(form);
        StaPump.Wait(form.TestAsync());
        form.ResultText.Should().Be(Strings.TrigTest_ErrNoSample);

        form.Find<TextBox>("_txtSample").Text = "otra cosa";
        StaPump.Wait(form.TestAsync());
        form.ResultText.Should().Be(Strings.TrigTest_NoMatch);
    });

    [Fact]
    public void Test_Lua_ListsWhatTheScriptWouldDo() => Sta.Run(() =>
    {
        using var form = Create();
        Choose(form, TriggerActionChoice.LuaScript);
        Fill(form, pattern: "@eco", action: "om.send('hola ' .. om.args[1])\r\nom.say('dicho')");
        form.Find<TextBox>("_txtSample").Text = "eco mundo";

        StaPump.Wait(form.TestAsync());

        form.Find<TextBox>("_txtResult").Lines.Should().Contain(string.Format(Strings.TrigTest_FxSend, "hola mundo"))
            .And.Contain(string.Format(Strings.TrigTest_FxSay, "dicho"));
    });

    [Fact]
    public void Test_LuaInfiniteLoop_Ends_AndTheDialogSurvives() => Sta.Run(() =>
    {
        using var form = Create();
        Choose(form, TriggerActionChoice.LuaScript);
        Fill(form, action: "while true do end");
        form.Find<TextBox>("_txtSample").Text = "llega alguien";

        StaPump.Wait(form.TestAsync());

        form.ResultText.Should().Contain(Strings.TrigTest_Matches);
        form.Find<TextBox>("_txtResult").Lines.Length.Should().BeGreaterThan(1);
        form.Find<Button>("_btnTest").Enabled.Should().BeTrue();
    });

    [Fact]
    public void Test_LuaSyntaxError_GoesToTheResultBox_WithTheCursorOnTheLine_AndNoMessageBox() => Sta.Run(() =>
    {
        using var form = Create();
        Choose(form, TriggerActionChoice.LuaScript);
        Fill(form, action: "om.send('uno')\r\nlocal = 3");
        form.Find<TextBox>("_txtSample").Text = "llega alguien";

        StaPump.Wait(form.TestAsync());

        form.ResultText.Should().StartWith(string.Format(Strings.TrigEdit_ErrScriptAtLine, 2, "").TrimEnd());
        form.Find<TextBox>("_txtAction").SelectedText.Should().Be("local = 3");
        _prompts.Messages.Should().BeEmpty();
    });

    [Fact]
    public void Test_BrokenRegex_GoesToTheResultBox() => Sta.Run(() =>
    {
        using var form = Create();
        Fill(form, pattern: "(x");
        form.Find<ComboBox>("_cboPatternType").SelectedIndex = (int)PatternType.Regex;
        form.Find<TextBox>("_txtSample").Text = "x";

        StaPump.Wait(form.TestAsync());

        form.ResultText.Should().StartWith(Strings.TrigEdit_ErrRegex.Split("{0}")[0]);
    });
}
