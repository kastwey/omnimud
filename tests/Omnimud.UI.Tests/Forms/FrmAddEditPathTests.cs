using Omnimud.Core.Paths;
using Omnimud.Data.Entities;
using Omnimud.UI.Forms;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Tests.Presenters;

namespace Omnimud.UI.Tests.Forms;

public class FrmAddEditPathTests
{
    private readonly RecordingPrompts _prompts = new();
    private readonly RecordingAnnouncer _announcer = new();

    private static readonly PathEntity[] Existing = [new() { Id = 1, CharacterId = 1, Name = "plaza", Path = "3n2e" }];

    private FrmAddEditPath Create(PathEntity? existing = null, IReadOnlyList<DirectionEntry>? directions = null, bool noDictionary = false) =>
        new(new PathEditorModel(existing, existing is null, Existing, noDictionary ? null : directions ?? PathEditorModelTests.Directions), _prompts, _announcer);

    private static void Fill(FrmAddEditPath form, string name, string path)
    {
        form.Find<TextBox>("_txtName").Text = name;
        form.Find<TextBox>("_txtPath").Text = path;
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void PassesTheAccessibilityAudit(string culture) => Sta.Run(() =>
    {
        ListFormsTestSupport.AssertAccessible(culture, () => Create());
        ListFormsTestSupport.AssertAccessible(culture, () => Create(Existing[0]));
        ListFormsTestSupport.AssertAccessible(culture, () => new FrmAddEditPath());
        ListFormsTestSupport.AssertAccessible(culture, () => new FrmAddEditPath(new PathEntity { Name = "", Path = "nnn" }));
    });

    [Fact]
    public void Title_FollowsTheMode() => Sta.Run(() =>
    {
        ListFormsTestSupport.UseCulture("es");
        using var add = Create();
        using var edit = Create(Existing[0]);
        using var recorded = new FrmAddEditPath(new PathEntity { CharacterId = 1, Name = "", Path = "nnn" });

        add.Text.Should().Be("Agregar nuevo path");
        edit.Text.Should().Be("Editando el path plaza");
        recorded.Text.Should().Be("Agregar nuevo path", "a recorded path arrives with Id 0");
        recorded.PathValue.Should().Be("nnn");
    });

    [Fact]
    public void ValidPath_IsAccepted_AndStoredCollapsed() => Sta.Run(() =>
    {
        using var form = Create();
        Fill(form, " mercado ", "nnnss");

        form.Find<Button>("_btnOk").Press();

        form.DialogResult.Should().Be(DialogResult.OK);
        form.PathName.Should().Be("mercado");
        form.PathValue.Should().Be("3n2s");
        _prompts.Messages.Should().BeEmpty();
        _prompts.Confirms.Should().BeEmpty();
    });

    [Fact]
    public void UnknownAbbreviation_SaysWhich_AndFocusesThePath() => Sta.Run(() =>
    {
        using var form = Create();
        Fill(form, "x", "3n2k");

        form.TryAccept().Should().BeFalse();

        _prompts.Warnings.Should().Equal(string.Format(Strings.PathEdit_ErrUnknownAbbreviation, 'k'));
        form.ActiveControl.Should().BeSameAs(form.Find<TextBox>("_txtPath"));
    });

    [Fact]
    public void DuplicateName_FocusesTheName() => Sta.Run(() =>
    {
        using var form = Create();
        Fill(form, "plaza", "n");

        form.TryAccept().Should().BeFalse();

        _prompts.Warnings.Should().Equal(string.Format(Strings.PathEdit_ErrDuplicateName, "plaza"));
        form.ActiveControl.Should().BeSameAs(form.Find<TextBox>("_txtName"));
    });

    [Fact]
    public void MudWithoutDirections_PointsToTheDirectionsDialog() => Sta.Run(() =>
    {
        using var form = Create(directions: []);
        Fill(form, "x", "3n");
        form.TryAccept().Should().BeFalse();
        _prompts.Warnings.Should().Equal(Strings.PathEdit_ErrNoDirections);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NotReversible_IsAQuestion(bool answer) => Sta.Run(() =>
    {
        _prompts.DefaultConfirm = answer;
        using var form = Create();
        Fill(form, "sotano", "nt");

        form.TryAccept().Should().Be(answer);
        _prompts.Confirms.Should().Equal(Strings.PathEdit_WarnNotReversible);
    });

    [Fact]
    public void SameRouteAsAnotherPath_IsAQuestion() => Sta.Run(() =>
    {
        using var form = Create();
        Fill(form, "otra", "nnnee");
        form.TryAccept().Should().BeTrue();
        _prompts.Confirms.Should().Equal(string.Format(Strings.PathEdit_WarnSamePath, "plaza"));
    });

    [Fact]
    public void ShowExpansion_FillsTheReadOnlyBox_AndAnnouncesIt() => Sta.Run(() =>
    {
        using var form = Create();
        Fill(form, "x", "2ne");

        form.Find<Button>("_btnExpand").Press();

        var expected = string.Format(Strings.PathEdit_Expansion, 3, "norte, norte, este");
        form.ExpansionText.Should().Be(expected);
        form.Find<TextBox>("_txtExpansion").ReadOnly.Should().BeTrue();
        _announcer.Spoken.Should().Equal(expected);
    });

    [Fact]
    public void Editing_ShowsTheExpansionFromTheStart_WithoutAnnouncing() => Sta.Run(() =>
    {
        using var form = Create(Existing[0]);
        form.ExpansionText.Should().Contain("norte, norte, norte, este, este");
        _announcer.Spoken.Should().BeEmpty();
    });

    [Fact]
    public void WithoutDictionary_AcceptsThePathAsTyped() => Sta.Run(() =>
    {
        using var form = Create(noDictionary: true);
        Fill(form, "x", "3n2k");
        form.TryAccept().Should().BeTrue();
        form.PathValue.Should().Be("3n2k");
    });
}
