using System.Globalization;
using Omnimud.Core.Session;
using Omnimud.UI.Forms;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Tests.Accessibility;

namespace Omnimud.UI.Tests.Forms;

/// <summary>The rules of the movement keys dialog, without a window.</summary>
public sealed class MovementKeysEditorTests
{
    private static readonly IReadOnlyDictionary<int, string> None = new Dictionary<int, string>();
    private static readonly IReadOnlyDictionary<int, string> Es = MovementKeys.DefaultCommands(CultureInfo.GetCultureInfo("es"));
    private static readonly IReadOnlyDictionary<int, string> En = MovementKeys.DefaultCommands(CultureInfo.GetCultureInfo("en"));

    private static Dictionary<int, string> Texts(MovementKeysEditor editor) =>
        MovementKeys.AllCodes.ToDictionary(key => key, editor.Effective);

    [Fact]
    public void NothingConfigured_ShowsTheDefaultsOfTheLanguage()
    {
        var es = new MovementKeysEditor(None, None, Es);
        var en = new MovementKeysEditor(None, None, En);

        es.Effective(10).Should().Be("norte");
        es.Effective(15).Should().Be("abajo");
        es.Effective(1).Should().Be("sudoeste");
        es.Effective(16).Should().BeEmpty();
        es.Effective(5).Should().BeEmpty();
        en.Effective(10).Should().Be("north");
        en.Effective(14).Should().Be("up");
        en.Effective(9).Should().Be("northeast");
    }

    [Fact]
    public void Effective_IsConfiguredElseDefault()
    {
        var editor = new MovementKeysEditor(new Dictionary<int, string> { [8] = "n", [17] = "salir", [10] = "" }, None, Es);

        editor.Effective(8).Should().Be("n");
        editor.Effective(17).Should().Be("salir");
        editor.Effective(10).Should().BeEmpty("an empty configured command switches the key off");
        editor.Effective(11).Should().Be("sur", "not configured: default");
    }

    [Fact]
    public void Character_WithoutOwnKeys_ShowsTheMuds_CompletedWithDefaults()
    {
        var editor = new MovementKeysEditor(None, new Dictionary<int, string> { [8] = "n", [10] = "" }, Es);

        editor.Effective(8).Should().Be("n");
        editor.Effective(10).Should().BeEmpty();
        editor.Effective(2).Should().Be("sur");
    }

    [Fact]
    public void Character_WithOwnKeys_IgnoresTheMuds_NotAMix()
    {
        var editor = new MovementKeysEditor(new Dictionary<int, string> { [5] = "mirar" }, new Dictionary<int, string> { [8] = "n" }, Es);

        editor.Effective(5).Should().Be("mirar");
        editor.Effective(8).Should().Be("norte", "the MUD's keys are not mixed in: default");
        editor.Baseline(8).Should().Be("n", "restoring goes back to the MUD's");
        editor.Baseline(5).Should().BeEmpty();
    }

    [Fact]
    public void Save_NothingChanged_StoresNothing_SoInheritanceAndDefaultsStayAlive()
    {
        var mud = new MovementKeysEditor(None, None, Es);
        var character = new MovementKeysEditor(None, new Dictionary<int, string> { [8] = "n" }, Es);

        mud.ToSave(Texts(mud)).Should().BeEmpty();
        character.ToSave(Texts(character)).Should().BeEmpty();
    }

    [Fact]
    public void Save_AnyChange_StoresTheCompleteSet()
    {
        var editor = new MovementKeysEditor(None, None, Es);
        var texts = Texts(editor);
        texts[10] = "  n  ";
        texts[16] = "entrar";

        var saved = editor.ToSave(texts);

        saved[10].Should().Be("n", "texts are trimmed");
        saved[16].Should().Be("entrar");
        saved[11].Should().Be("sur", "unchanged defaults are stored too: what was seen is what the keys do from now on");
        saved.Should().NotContainKey(17).And.NotContainKey(5).And.NotContainKey(0);
        saved.Should().HaveCount(15);
    }

    [Fact]
    public void Save_AnEmptiedKeyWithDefault_IsStoredEmpty_OrTheDefaultWouldComeBack()
    {
        var editor = new MovementKeysEditor(None, None, Es);
        var texts = Texts(editor);
        texts[10] = "";

        var saved = editor.ToSave(texts);

        saved[10].Should().BeEmpty();
        MovementKeys.Effective(saved, Es).Should().NotContainKey(10);
        MovementKeys.Effective(saved, En).Should().NotContainKey(10, "also after changing the language of the interface");
    }

    [Fact]
    public void Save_WhatIsStored_GivesExactlyWhatWasShown()
    {
        var editor = new MovementKeysEditor(new Dictionary<int, string> { [8] = "n" }, new Dictionary<int, string> { [2] = "s" }, Es);
        var texts = Texts(editor);
        texts[13] = "";
        texts[0] = "mirar";

        var effective = MovementKeys.Effective(editor.ToSave(texts), Es);

        foreach (var key in MovementKeys.AllCodes)
            (effective.TryGetValue(key, out var command) ? command : "").Should().Be(texts[key].Trim());
    }

    [Fact]
    public void Save_BackToTheBaseline_DeletesTheOwnKeys()
    {
        var editor = new MovementKeysEditor(new Dictionary<int, string> { [8] = "n", [10] = "" }, new Dictionary<int, string> { [2] = "s" }, Es);
        var restored = MovementKeys.AllCodes.ToDictionary(key => key, editor.Baseline);

        editor.ToSave(restored).Should().BeEmpty("the character follows its MUD again");
    }

    [Fact]
    public void Save_CharacterThatGoesBackToPlainDefaults_WhileItsMudDiffers_StoresThem()
    {
        var editor = new MovementKeysEditor(None, new Dictionary<int, string> { [8] = "n" }, Es);
        var texts = Texts(editor);
        texts[8] = "norte";

        editor.ToSave(texts)[8].Should().Be("norte", "otherwise the MUD's 'n' would come back");
    }
}

public sealed class FrmMovementsTests
{
    private static readonly IReadOnlyDictionary<int, string> None = new Dictionary<int, string>();

    public FrmMovementsTests() => Strings.Culture = CultureInfo.GetCultureInfo("es");

    private static MovementKeysEditor Editor(IReadOnlyDictionary<int, string>? own = null, IReadOnlyDictionary<int, string>? inherited = null) =>
        new(own ?? None, inherited ?? None, MovementKeys.DefaultCommands(Strings.Culture));

    private static FrmMovements Create(IReadOnlyDictionary<int, string>? own = null, IReadOnlyDictionary<int, string>? inherited = null) =>
        new("Aldara", ownerIsCharacter: true, Editor(own, inherited));

    private static T Get<T>(Form form, string name) where T : Control =>
        (T)form.Controls.Find(name, searchAllChildren: true).Single();

    private static TextBox Box(Form form, int key) => Get<TextBox>(form, $"_txtKey{key}");

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void Dialog_PassesTheAccessibilityAudit_InEveryLanguage(string culture) => Sta.Run(() =>
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);
        using var form = Create();

        AccessibilityAudit.Check(form, isDialog: true).Should().BeEmpty();
    });

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void EighteenKeys_EachWithLabelMnemonicAndAccessibleName_AllMnemonicsDifferent(string culture) => Sta.Run(() =>
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);
        using var form = Create();
        var mnemonics = new List<char>();

        foreach (var key in MovementKeys.AllCodes)
        {
            var box = Box(form, key);
            var label = Get<Label>(form, $"_lblKey{key}");
            label.Parent.Should().BeSameAs(box.Parent);
            label.TabIndex.Should().Be(box.TabIndex - 1);
            label.UseMnemonic.Should().BeTrue();
            AccessibilityAudit.Mnemonic(label.Text).Should().NotBeNull($"'{label.Text}' needs a mnemonic");
            mnemonics.Add(AccessibilityAudit.Mnemonic(label.Text)!.Value);
            box.AccessibleName.Should().Be(MovementKeys.DisplayName(key, Strings.Culture));
            box.AccessibleDescription.Should().BeNullOrEmpty("a description is read aloud on every focus");
        }

        foreach (var button in new[] { "_btnRestore", "_btnOk", "_btnCancel" })
            if (AccessibilityAudit.Mnemonic(Get<Button>(form, button).Text) is { } m) mnemonics.Add(m);

        mnemonics.Should().OnlyHaveUniqueItems();
        mnemonics.Should().Contain(['0', '1', '2', '3', '4', '5', '6', '7', '8', '9'], "Alt+digit reaches each keypad key");
    });

    [Fact]
    public void Labels_InSpanish() => Sta.Run(() =>
    {
        using var form = Create();

        Get<Label>(form, "_lblKey10").Text.Should().Be("Flecha &arriba:");
        Get<Label>(form, "_lblKey14").Text.Should().Be("&Re Pág:");
        Get<Label>(form, "_lblKey15").Text.Should().Be("A&v Pág:");
        Get<Label>(form, "_lblKey8").Text.Should().Be("Teclado numérico &8:");
        Get<GroupBox>(form, "_grpNavigation").Text.Should().Be("Flechas y navegación");
        Get<GroupBox>(form, "_grpNumPad").Text.Should().Be("Teclado numérico");
        Get<Button>(form, "_btnRestore").Text.Should().Be("Re&staurar valores por defecto");
        form.Text.Should().Be("Teclas de movimiento - Aldara");
    });

    [Fact]
    public void Groups_HoldTheirKeys_NavigationFirst_InTabOrder() => Sta.Run(() =>
    {
        using var form = Create();
        var navigation = Get<GroupBox>(form, "_grpNavigation");
        var numPad = Get<GroupBox>(form, "_grpNumPad");

        foreach (var key in MovementKeys.NavigationCodes) navigation.Contains(Box(form, key)).Should().BeTrue();
        foreach (var key in MovementKeys.NumPadCodes) numPad.Contains(Box(form, key)).Should().BeTrue();

        navigation.TabIndex.Should().BeLessThan(numPad.TabIndex);
        numPad.TabIndex.Should().BeLessThan(Get<Control>(form, "_buttons").TabIndex);
        MovementKeys.NavigationCodes.Select(k => Box(form, k).TabIndex).Should().BeInAscendingOrder();
        MovementKeys.NumPadCodes.Select(k => Box(form, k).TabIndex).Should().BeInAscendingOrder();
    });

    [Fact]
    public void Dialog_HasAcceptAndCancelButtons() => Sta.Run(() =>
    {
        using var form = Create();

        form.AcceptButton.Should().BeSameAs(Get<Button>(form, "_btnOk"));
        form.CancelButton.Should().BeSameAs(Get<Button>(form, "_btnCancel"));
        Get<Button>(form, "_btnOk").DialogResult.Should().Be(DialogResult.OK);
        Get<Button>(form, "_btnRestore").DialogResult.Should().Be(DialogResult.None, "restoring does not close the dialog");
    });

    [Theory]
    [InlineData("es", "norte", "arriba", "sudeste")]
    [InlineData("en", "north", "up", "southeast")]
    public void Boxes_ShowTheEffectiveValue_DefaultsWhenNothingIsConfigured(string culture, string up, string pageUp, string three) => Sta.Run(() =>
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);
        using var form = Create();

        Box(form, 10).Text.Should().Be(up);
        Box(form, 14).Text.Should().Be(pageUp);
        Box(form, 3).Text.Should().Be(three);
        Box(form, 16).Text.Should().BeEmpty();
        Box(form, 17).Text.Should().BeEmpty();
        Box(form, 5).Text.Should().BeEmpty();
        Box(form, 0).Text.Should().BeEmpty();
    });

    [Fact]
    public void Boxes_ShowConfiguredOverDefault_AndInheritedForACharacterWithoutKeys() => Sta.Run(() =>
    {
        using (var own = Create(own: new Dictionary<int, string> { [10] = "n", [11] = "" }))
        {
            Box(own, 10).Text.Should().Be("n");
            Box(own, 11).Text.Should().BeEmpty("switched off");
            Box(own, 12).Text.Should().Be("oeste");
        }

        using var inherited = Create(inherited: new Dictionary<int, string> { [8] = "n" });
        Box(inherited, 8).Text.Should().Be("n");
        Box(inherited, 2).Text.Should().Be("sur");
    });

    [Fact]
    public void Commands_Untouched_AreEmpty_NothingToStore() => Sta.Run(() =>
    {
        using var form = Create(inherited: new Dictionary<int, string> { [8] = "n" });

        form.Commands.Should().BeEmpty();
    });

    [Fact]
    public void Commands_AfterEditing_AreTheCompleteSet_WithEmptiedDefaultsStoredEmpty() => Sta.Run(() =>
    {
        using var form = Create();
        Box(form, 10).Text = " n ";
        Box(form, 13).Text = "";
        Box(form, 16).Text = "entrar";

        var commands = form.Commands;

        commands[10].Should().Be("n");
        commands[13].Should().BeEmpty();
        commands[16].Should().Be("entrar");
        commands[11].Should().Be("sur");
        commands.Should().NotContainKey(17).And.NotContainKey(5);
        commands.Keys.Should().OnlyContain(key => MovementKeys.IsValid(key));
    });

    [Fact]
    public void RestoreDefaults_PutsTheBaselineBack_AndThenThereIsNothingToStore() => Sta.Run(() =>
    {
        using var form = Create(own: new Dictionary<int, string> { [10] = "n", [16] = "entrar", [2] = "" }, inherited: new Dictionary<int, string> { [8] = "n" });
        Box(form, 12).Text = "w";

        form.RestoreDefaults(); // what the button does (PerformClick needs a visible window)

        Box(form, 10).Text.Should().Be("norte");
        Box(form, 16).Text.Should().BeEmpty();
        Box(form, 2).Text.Should().Be("sur");
        Box(form, 12).Text.Should().Be("oeste");
        Box(form, 8).Text.Should().Be("n", "for a character the defaults are its MUD's keys");
        Box(form, 10).SelectionLength.Should().Be(Box(form, 10).TextLength, "the restored text is selected so the screen reader reads it");
        form.Commands.Should().BeEmpty();
    });

    [Fact]
    public void LegacyConstructor_StillWorks_TreatingTheKeysAsOwn() => Sta.Run(() =>
    {
        using var form = new FrmMovements("Reinos", ownerIsCharacter: false, new Dictionary<int, string> { [8] = "n" });

        Box(form, 8).Text.Should().Be("n");
        Box(form, 10).Text.Should().Be("norte");
        form.Commands[8].Should().Be("n");
    });

    [Theory]
    [InlineData(9.75F)]  // 100 %
    [InlineData(19.5F)]  // 200 %: the same font takes twice the pixels
    public void Dialog_FitsAFullHdScreen_WithoutScrolling(float fontPoints) => Sta.Run(() =>
    {
        using var form = new FrmMovements("Aldara", true, Editor(), fontPoints);
        var area = new Rectangle(0, 0, 1920, 1040);

        form.FitToScreen(area);

        form.Width.Should().BeLessThanOrEqualTo(area.Width);
        form.Height.Should().BeLessThanOrEqualTo(area.Height);
        var content = Get<TableLayoutPanel>(form, "_layout");
        form.ClientSize.Width.Should().BeGreaterThanOrEqualTo(content.Width, "everything is visible without scrolling");
        form.ClientSize.Height.Should().BeGreaterThanOrEqualTo(content.Height);
    });

    [Fact]
    public void Dialog_OnASmallScreenAtBigScale_IsCappedToTheScreen_AndScrolls() => Sta.Run(() =>
    {
        using var form = new FrmMovements("Aldara", true, Editor(), 19.5F);
        var area = new Rectangle(0, 0, 1366, 728);

        form.FitToScreen(area);

        form.AutoScroll.Should().BeTrue();
        form.Width.Should().BeLessThanOrEqualTo(area.Width);
        form.Height.Should().BeLessThanOrEqualTo(area.Height);
        Get<TableLayoutPanel>(form, "_layout").Height.Should().BeGreaterThan(form.ClientSize.Height, "the content is taller: the dialog scrolls");
    });

    [Theory]
    [InlineData(600, 400, 1000, 800, 600, 400)]   // fits
    [InlineData(600, 900, 1000, 800, 617, 800)]   // too tall: room for the vertical bar
    [InlineData(1200, 400, 1000, 800, 1000, 417)] // too wide: room for the horizontal bar
    [InlineData(1200, 900, 1000, 800, 1000, 800)] // both
    [InlineData(990, 790, 1000, 800, 990, 790)]
    [InlineData(1200, 790, 1000, 800, 1000, 800)] // the horizontal bar makes it too tall as well
    public void FitClientSize_NeverExceedsTheScreen_AndLeavesRoomForScrollBars(int w, int h, int availableW, int availableH, int expectedW, int expectedH)
    {
        FrmMovements.FitClientSize(new Size(w, h), new Size(availableW, availableH), 17, 17)
            .Should().Be(new Size(expectedW, expectedH));
    }
}
