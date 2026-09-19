using Omnimud.Data.Entities;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Tests.Presenters;

public class AliasEditorModelTests
{
    private static readonly AliasEntity[] Existing =
    [
        new() { Id = 1, CharacterId = 7, Command = "k", Action = "matar orco" },
        new() { Id = 2, CharacterId = 7, Command = "b", Action = "beber agua" },
    ];

    private static AliasEditorModel New(string command, string action) =>
        new(null, isNew: true, Existing) { Command = command, Action = action };

    [Fact]
    public void Validate_ValidAlias_HasNoIssue()
    {
        New("c", "comer pan").Validate().Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyCommand_PointsAtTheCommand(string command)
    {
        var issue = New(command, "algo").Validate();
        issue!.Field.Should().Be(AliasEditorModel.FieldCommand);
        issue.Message.Should().Be(Strings.AliasEdit_ErrCommandEmpty);
    }

    [Theory]
    [InlineData("dos palabras")]
    [InlineData("con\ttab")]
    public void Validate_CommandWithSpaces_IsRejected(string command)
    {
        var issue = New(command, "algo").Validate();
        issue!.Field.Should().Be(AliasEditorModel.FieldCommand);
        issue.Message.Should().Be(Strings.AliasEdit_ErrCommandSpaces);
    }

    [Fact]
    public void Validate_EmptyAction_PointsAtTheAction()
    {
        var issue = New("c", " ").Validate();
        issue!.Field.Should().Be(AliasEditorModel.FieldAction);
        issue.Message.Should().Be(Strings.AliasEdit_ErrActionEmpty);
    }

    [Fact]
    public void Validate_CommandEqualToAction_IsPointless()
    {
        New("mirar", "mirar").Validate()!.Message.Should().Be(Strings.AliasEdit_ErrSameAsAction);
    }

    [Fact]
    public void Validate_DuplicateCommand_IsAMessageOnTheCommand_NotAnException()
    {
        var issue = New("k", "otra cosa").Validate();
        issue!.Field.Should().Be(AliasEditorModel.FieldCommand);
        issue.Message.Should().Be(string.Format(Strings.AliasEdit_ErrDuplicate, "k"));
    }

    [Fact]
    public void Validate_DuplicatesAreCaseSensitive_LikeAliasMatching()
    {
        New("K", "otra cosa").Validate().Should().BeNull();
    }

    [Fact]
    public void Validate_EditingAnAlias_DoesNotCollideWithItself()
    {
        var model = new AliasEditorModel(Existing[0], isNew: false, Existing) { Action = "matar troll" };
        model.Validate().Should().BeNull();
    }

    [Fact]
    public void Warnings_SameActionAsAnotherAlias_AsksNamingTheOther()
    {
        New("x", "beber agua").Warnings().Should().ContainSingle()
            .Which.Should().Be(string.Format(Strings.AliasEdit_WarnSameAction, "b"));
        New("x", "otra").Warnings().Should().BeEmpty();
    }

    [Fact]
    public void ToEntity_TrimsAndKeepsIdentity()
    {
        var model = new AliasEditorModel(Existing[1], isNew: false, Existing) { Command = " bb ", Action = " beber vino ", Enabled = false };
        var entity = model.ToEntity();
        entity.Should().BeEquivalentTo(new AliasEntity { Id = 2, CharacterId = 7, Command = "bb", Action = "beber vino", Enabled = false });
    }

    [Fact]
    public void ToEntity_NewAlias_HasNoId_EvenWhenReopenedWithTypedValues()
    {
        var typed = new AliasEntity { Id = 0, Command = "k", Action = "x" };
        var model = new AliasEditorModel(typed, isNew: true, Existing);
        model.Command.Should().Be("k");
        model.Validate()!.Message.Should().Be(string.Format(Strings.AliasEdit_ErrDuplicate, "k"));
        model.ToEntity().Id.Should().Be(0);
    }

    [Fact]
    public void NewAlias_IsEnabledByDefault()
    {
        new AliasEditorModel(null, isNew: true).Enabled.Should().BeTrue();
    }
}
