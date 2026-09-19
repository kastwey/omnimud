using Omnimud.Data.Entities;
using Omnimud.UI.Forms;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Tests.Presenters;

namespace Omnimud.UI.Tests.Forms;

public class FrmAddEditAliasTests
{
    private readonly RecordingPrompts _prompts = new();

    private static readonly AliasEntity[] Existing = [new() { Id = 1, CharacterId = 1, Command = "k", Action = "matar orco" }];

    private FrmAddEditAlias Create(AliasEntity? existing = null) =>
        new(new AliasEditorModel(existing, existing is null, Existing), _prompts);

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void PassesTheAccessibilityAudit(string culture) => Sta.Run(() =>
    {
        ListFormsTestSupport.AssertAccessible(culture, () => Create());
        ListFormsTestSupport.AssertAccessible(culture, () => Create(Existing[0]));
        ListFormsTestSupport.AssertAccessible(culture, () => new FrmAddEditAlias());
    });

    [Fact]
    public void Title_FollowsTheMode_AndFieldsAreLoaded() => Sta.Run(() =>
    {
        ListFormsTestSupport.UseCulture("es");
        using var add = Create();
        using var edit = Create(Existing[0]);

        add.Text.Should().Be("Crear nuevo alias");
        add.AliasEnabled.Should().BeTrue();
        edit.Text.Should().Be("Editar alias");
        edit.AliasCommand.Should().Be("k");
        edit.AliasAction.Should().Be("matar orco");
    });

    [Fact]
    public void CommandWithSpaces_MessageAndFocusOnTheCommand() => Sta.Run(() =>
    {
        using var form = Create();
        form.Find<TextBox>("_txtCommand").Text = "dos palabras";
        form.Find<TextBox>("_txtAction").Text = "algo";

        form.TryAccept().Should().BeFalse();

        _prompts.Warnings.Should().Equal(Strings.AliasEdit_ErrCommandSpaces);
        form.ActiveControl.Should().BeSameAs(form.Find<TextBox>("_txtCommand"));
    });

    [Fact]
    public void EmptyAction_FocusOnTheAction() => Sta.Run(() =>
    {
        using var form = Create();
        form.Find<TextBox>("_txtCommand").Text = "c";

        form.TryAccept().Should().BeFalse();

        _prompts.Warnings.Should().Equal(Strings.AliasEdit_ErrActionEmpty);
        form.ActiveControl.Should().BeSameAs(form.Find<TextBox>("_txtAction"));
    });

    [Fact]
    public void Duplicate_MessageAndFocusOnTheCommand() => Sta.Run(() =>
    {
        using var form = Create();
        form.Find<TextBox>("_txtCommand").Text = "k";
        form.Find<TextBox>("_txtAction").Text = "otra cosa";

        form.TryAccept().Should().BeFalse();

        _prompts.Warnings.Should().Equal(string.Format(Strings.AliasEdit_ErrDuplicate, "k"));
        form.ActiveControl.Should().BeSameAs(form.Find<TextBox>("_txtCommand"));
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SameActionAsAnotherAlias_IsAQuestion(bool answer) => Sta.Run(() =>
    {
        _prompts.DefaultConfirm = answer;
        using var form = Create();
        form.Find<TextBox>("_txtCommand").Text = "m";
        form.Find<TextBox>("_txtAction").Text = "matar orco";

        form.TryAccept().Should().Be(answer);
        _prompts.Confirms.Should().Equal(string.Format(Strings.AliasEdit_WarnSameAction, "k"));
    });

    [Fact]
    public void ValidAlias_IsAccepted_AndOkClosesTheDialog() => Sta.Run(() =>
    {
        using var form = Create();
        form.Find<TextBox>("_txtCommand").Text = " c ";
        form.Find<TextBox>("_txtAction").Text = "comer pan";
        form.Find<CheckBox>("_chkEnabled").Checked = false;

        form.Find<Button>("_btnOk").Press();

        form.DialogResult.Should().Be(DialogResult.OK);
        form.AliasCommand.Should().Be("c");
        form.AliasEnabled.Should().BeFalse();
        _prompts.Messages.Should().BeEmpty();
    });

    [Fact]
    public void InvalidAlias_OkDoesNotClose() => Sta.Run(() =>
    {
        using var form = Create();
        form.Find<Button>("_btnOk").Press();
        form.DialogResult.Should().Be(DialogResult.None);
    });
}
