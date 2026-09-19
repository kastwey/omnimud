using System.Globalization;
using Omnimud.Data.Exchange;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Tests.Presenters;

public sealed class ImportConflictsPresenterTests
{
    public ImportConflictsPresenterTests() => Strings.Culture = CultureInfo.GetCultureInfo("es");

    private static readonly ImportItem[] Items =
    [
        new("mud:Reinos", ImportItemKind.Mud, "Reinos", ImportItemStatus.Conflict),
        new("set:Balzhur", ImportItemKind.MessageRuleSet, "Balzhur", ImportItemStatus.New),
        new("character:Aldara", ImportItemKind.Character, "Aldara", ImportItemStatus.Conflict, ParentKey: "mud:Reinos"),
        new("character:Nuevo", ImportItemKind.Character, "Nuevo", ImportItemStatus.New, ParentKey: "mud:Reinos"),
        new("alias:x", ImportItemKind.Alias, "x", ImportItemStatus.Invalid, Error: "bad"),
        new("movements", ImportItemKind.Movements, "", ImportItemStatus.Conflict),
    ];

    [Fact]
    public void OnlyConflicts_AreListed_InFileOrder()
    {
        var presenter = new ImportConflictsPresenter(Items);

        presenter.HasConflicts.Should().BeTrue();
        presenter.Rows.Select(r => r.Key).Should().Equal("mud:Reinos", "character:Aldara", "movements");
    }

    [Fact]
    public void Rows_SayWhatTheyAre_AndWhereTheyBelong()
    {
        var presenter = new ImportConflictsPresenter(Items);

        presenter.Rows.Select(r => r.Text).Should().Equal("MUD: Reinos", "Personaje: Aldara (MUD Reinos)", "Teclas de movimiento");
    }

    [Fact]
    public void NothingIsOverwritten_UnlessTheUserSaysSo()
    {
        var presenter = new ImportConflictsPresenter(Items);

        presenter.Decisions.Values.Should().OnlyContain(d => d == ImportDecision.Skip);
        presenter.Decisions.Keys.Should().BeEquivalentTo("mud:Reinos", "character:Aldara", "movements");
    }

    [Fact]
    public void EachRow_IsDecidedOnItsOwn()
    {
        var presenter = new ImportConflictsPresenter(Items);

        presenter.SetOverwrite(1, true);
        presenter.SetOverwrite(99, true);

        presenter.Decisions.Should().BeEquivalentTo(new Dictionary<string, ImportDecision>
        {
            ["mud:Reinos"] = ImportDecision.Skip,
            ["character:Aldara"] = ImportDecision.Overwrite,
            ["movements"] = ImportDecision.Skip,
        });
        presenter.Status.Should().Be(string.Format(Strings.ImportConflicts_Status, 3, 1, 2));
    }

    [Fact]
    public void OverwriteAll_And_SkipAll()
    {
        var presenter = new ImportConflictsPresenter(Items);

        presenter.SetAll(true);
        presenter.Decisions.Values.Should().OnlyContain(d => d == ImportDecision.Overwrite);
        (presenter.OverwriteCount, presenter.SkipCount).Should().Be((3, 0));

        presenter.SetAll(false);
        presenter.Decisions.Values.Should().OnlyContain(d => d == ImportDecision.Skip);
    }

    [Fact]
    public void WithoutConflicts_ThereIsNothingToAsk()
    {
        var presenter = new ImportConflictsPresenter(Items.Where(i => i.Status != ImportItemStatus.Conflict));

        presenter.HasConflicts.Should().BeFalse();
        presenter.Decisions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void EveryKind_HasAName_InEveryLanguage(string culture)
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);

        var names = Enum.GetValues<ImportItemKind>().Select(ImportConflictsPresenter.KindName).ToList();

        names.Should().OnlyContain(n => !string.IsNullOrWhiteSpace(n)).And.OnlyHaveUniqueItems();
    }
}
