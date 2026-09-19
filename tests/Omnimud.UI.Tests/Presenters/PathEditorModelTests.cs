using Omnimud.Core.Paths;
using Omnimud.Data.Entities;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Tests.Presenters;

public class PathEditorModelTests
{
    internal static readonly DirectionEntry[] Directions =
    [
        new("norte", 'n', "sur"), new("sur", 's', "norte"), new("este", 'e', "oeste"), new("oeste", 'o', "este"),
        new("trampilla", 't', null), // no way back
    ];

    private static readonly PathEntity[] Existing =
    [
        new() { Id = 1, CharacterId = 5, Name = "plaza", Path = "3n2e" },
        new() { Id = 2, CharacterId = 5, Name = "puerto", Path = "4s" },
    ];

    private static PathEditorModel New(string name, string path, IReadOnlyList<DirectionEntry>? directions) =>
        new(null, isNew: true, Existing, directions) { Name = name, Path = path };

    [Fact]
    public void Validate_ValidPath_HasNoIssue_AndNoWarnings()
    {
        var model = New("mercado", "2n3e", Directions);
        model.Validate().Should().BeNull();
        model.Warnings().Should().BeEmpty();
        model.IsReversible.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyName_AndEmptyPath()
    {
        New(" ", "n", Directions).Validate()!.Should().BeEquivalentTo(new EditorIssue(PathEditorModel.FieldName, Strings.PathEdit_ErrNameEmpty));
        New("x", "  ", Directions).Validate()!.Should().BeEquivalentTo(new EditorIssue(PathEditorModel.FieldPath, Strings.PathEdit_ErrPathEmpty));
    }

    [Fact]
    public void Validate_DuplicateName_IsAMessageOnTheName()
    {
        New("PLAZA", "n", Directions).Validate()!
            .Should().BeEquivalentTo(new EditorIssue(PathEditorModel.FieldName, string.Format(Strings.PathEdit_ErrDuplicateName, "PLAZA")));
    }

    [Fact]
    public void Validate_EditingAPath_DoesNotCollideWithItself()
    {
        new PathEditorModel(Existing[0], isNew: false, Existing, Directions).Validate().Should().BeNull();
    }

    [Fact]
    public void Validate_UnknownAbbreviation_SaysWhichOne()
    {
        var issue = New("x", "3n2x1e", Directions).Validate();
        issue!.Field.Should().Be(PathEditorModel.FieldPath);
        issue.Message.Should().Be(string.Format(Strings.PathEdit_ErrUnknownAbbreviation, 'x'));
    }

    [Fact]
    public void Validate_PathEndingInANumber_IsASyntaxError()
    {
        New("x", "3n2", Directions).Validate()!.Message.Should().Be(Strings.PathEdit_ErrEndsWithNumber);
    }

    [Fact]
    public void Validate_HugeRepetition_IsRejected_InsteadOfExhaustingMemory()
    {
        var model = New("x", "999999999n", Directions);
        model.Validate()!.Message.Should().Be(Strings.PathEdit_ErrCountTooBig);
        model.DescribeExpansion().Should().Be(Strings.PathEdit_ErrCountTooBig);
        model.CollapsedPath.Should().Be("999999999n");
    }

    [Fact]
    public void Validate_MudWithoutDirections_SendsTheUserToTheDirectionsDialog()
    {
        var issue = New("x", "3n", []).Validate();
        issue!.Field.Should().Be(PathEditorModel.FieldPath);
        issue.Message.Should().Be(Strings.PathEdit_ErrNoDirections);
    }

    [Fact]
    public void WithoutDictionary_AnythingGoes_AndIsStoredAsTyped()
    {
        var model = New("x", "3n2x", null);
        model.ValidatesAgainstDictionary.Should().BeFalse();
        model.Validate().Should().BeNull();
        model.Warnings().Should().BeEmpty();
        model.CollapsedPath.Should().Be("3n2x");
        model.DescribeExpansion().Should().Be(Strings.PathEdit_ExpansionUnavailable);
    }

    [Theory]
    [InlineData("nnnee", "3n2e")]
    [InlineData("n2nes", "3nes")]
    [InlineData(" 3n 2e ", "3n2e")]
    [InlineData("1n", "n")]
    public void CollapsedPath_IsWhatGetsStored(string typed, string stored)
    {
        var model = New("x", typed, Directions);
        model.Validate().Should().BeNull();
        model.CollapsedPath.Should().Be(stored);
        model.ToEntity().Path.Should().Be(stored);
    }

    [Fact]
    public void Warnings_NotReversible_WhenADirectionHasNoOpposite()
    {
        var model = New("sotano", "2nt", Directions);
        model.Validate().Should().BeNull();
        model.IsReversible.Should().BeFalse();
        model.Warnings().Should().Equal(Strings.PathEdit_WarnNotReversible);
    }

    [Fact]
    public void Warnings_SameRouteAsAnotherPath_ComparingTheCollapsedForm()
    {
        New("otra plaza", "nnnee", Directions).Warnings().Should().Equal(string.Format(Strings.PathEdit_WarnSamePath, "plaza"));
    }

    [Fact]
    public void Warnings_CanBeBoth()
    {
        var all = Existing.Append(new PathEntity { Id = 3, CharacterId = 5, Name = "pozo", Path = "t" }).ToList();
        var model = new PathEditorModel(null, isNew: true, all, Directions) { Name = "otro pozo", Path = "t" };
        model.Warnings().Should().HaveCount(2);
    }

    [Fact]
    public void DescribeExpansion_ListsEveryStep()
    {
        New("x", "2ne", Directions).DescribeExpansion().Should().Be(string.Format(Strings.PathEdit_Expansion, 3, "norte, norte, este"));
        New("x", "", Directions).DescribeExpansion().Should().BeEmpty();
        New("x", "2z", Directions).DescribeExpansion().Should().Be(string.Format(Strings.PathEdit_ErrUnknownAbbreviation, 'z'));
    }

    [Fact]
    public void ToEntity_KeepsIdentity_WhenEditing_AndHasNoIdWhenNew()
    {
        var edited = new PathEditorModel(Existing[1], isNew: false, Existing, Directions) { Name = " muelle ", Path = "ssss" }.ToEntity();
        edited.Should().BeEquivalentTo(new PathEntity { Id = 2, CharacterId = 5, Name = "muelle", Path = "4s" });

        New("x", "n", Directions).ToEntity().Id.Should().Be(0);
    }

    [Fact]
    public async Task LoadDirectionsAsync_ReadsTheDictionaryOfTheMud_FromSqlite()
    {
        using var db = new ListsDatabase();
        await db.Directions.AddAsync(new DirectionEntity { MudId = db.MudId, Direction = "norte", Abbreviation = "n", OppositeDirection = "sur" });
        await db.Directions.AddAsync(new DirectionEntity { MudId = db.MudId, Direction = "sur", Abbreviation = "s", OppositeDirection = "norte" });
        await db.Directions.AddAsync(new DirectionEntity { MudId = db.OtherMudId, Direction = "up", Abbreviation = "u" });

        var directions = await PathEditorModel.LoadDirectionsAsync(db.Directions, db.MudId);

        directions.Select(d => d.Abbreviation).Should().BeEquivalentTo(['n', 's']);
        new PathEditorModel(null, true, [], directions) { Name = "x", Path = "2ns" }.Validate().Should().BeNull();
        new PathEditorModel(null, true, [], directions) { Name = "x", Path = "u" }.Validate()!.Message
            .Should().Be(string.Format(Strings.PathEdit_ErrUnknownAbbreviation, 'u'));
    }
}
