using System.Globalization;
using NSubstitute;
using Omnimud.Core.Session;
using Omnimud.Data.Exchange;
using Omnimud.UI.Forms;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Services.Accessibility;
using Omnimud.UI.Tests.Accessibility;

namespace Omnimud.UI.Tests.Forms;

public sealed class FrmImportConflictsTests
{
    private readonly IAnnouncer _announcer = Substitute.For<IAnnouncer>();

    private static readonly ImportItem[] Items =
    [
        new("mud:Reinos", ImportItemKind.Mud, "Reinos", ImportItemStatus.Conflict),
        new("character:Aldara", ImportItemKind.Character, "Aldara", ImportItemStatus.Conflict, ParentKey: "mud:Reinos"),
        new("character:Nuevo", ImportItemKind.Character, "Nuevo", ImportItemStatus.New, ParentKey: "mud:Reinos"),
        new("alias:k", ImportItemKind.Alias, "k", ImportItemStatus.Conflict),
    ];

    public FrmImportConflictsTests() => Strings.Culture = CultureInfo.GetCultureInfo("es");

    private FrmImportConflicts Create() => new(new ImportConflictsPresenter(Items), _announcer);

    private static T Get<T>(Form form, string name) where T : Control => (T)form.Controls.Find(name, true).Single();

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void Dialog_PassesTheAccessibilityAudit_InEveryLanguage(string culture) => Sta.Run(() =>
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);
        using var form = Create();

        AccessibilityAudit.Check(form, isDialog: true).Should().BeEmpty();
    });

    [Fact]
    public void ListsOnlyTheConflicts_Unchecked_WithTheFirstSelected() => Sta.Run(() =>
    {
        using var form = Create();
        var list = Get<CheckedListBox>(form, "_list");

        list.Items.Cast<object>().Select(i => i.ToString()).Should().Equal("MUD: Reinos", "Personaje: Aldara (MUD Reinos)", "Alias: k");
        list.CheckedIndices.Count.Should().Be(0, "nothing is overwritten unless the user says so");
        list.SelectedIndex.Should().Be(0);
        list.AccessibleName.Should().Contain("marcado = sobrescribir");
        Get<Label>(form, "_lblStatus").Text.Should().Be(string.Format(Strings.ImportConflicts_Status, 3, 0, 3));
    });

    [Fact]
    public void CheckingARow_MeansOverwrite_ForThatRowOnly() => Sta.Run(() =>
    {
        using var form = Create();

        Get<CheckedListBox>(form, "_list").SetItemChecked(1, true);

        form.Decisions.Should().BeEquivalentTo(new Dictionary<string, ImportDecision>
        {
            ["mud:Reinos"] = ImportDecision.Skip,
            ["character:Aldara"] = ImportDecision.Overwrite,
            ["alias:k"] = ImportDecision.Skip,
        });
        Get<Label>(form, "_lblStatus").Text.Should().Be(string.Format(Strings.ImportConflicts_Status, 3, 1, 2));
    });

    [Fact]
    public void OverwriteAll_And_SkipAll_UpdateTheList_AndAreAnnounced() => Sta.Run(() =>
    {
        using var form = Create();
        var list = Get<CheckedListBox>(form, "_list");

        form.SetAll(true);
        list.CheckedIndices.Count.Should().Be(3);
        form.Decisions.Values.Should().OnlyContain(d => d == ImportDecision.Overwrite);
        _announcer.Received(1).Announce(string.Format(Strings.ImportConflicts_Status, 3, 3, 0), AnnouncePriority.MostRecent);

        form.SetAll(false);
        list.CheckedIndices.Count.Should().Be(0);
        form.Decisions.Values.Should().OnlyContain(d => d == ImportDecision.Skip);
        _announcer.Received(1).Announce(string.Format(Strings.ImportConflicts_Status, 3, 0, 3), AnnouncePriority.MostRecent);
    });

    [Fact]
    public void ImportAccepts_AndEscapeCancels() => Sta.Run(() =>
    {
        using var form = Create();

        ((Button)form.AcceptButton!).DialogResult.Should().Be(DialogResult.OK);
        ((Button)form.CancelButton!).DialogResult.Should().Be(DialogResult.Cancel);
    });

    [Fact]
    public void Resolver_WithoutConflicts_AsksNothing() => Sta.Run(() =>
    {
        var resolver = new ImportConflictsDialog(() => null);

        resolver.Resolve([Items[2]]).Should().BeEmpty();
    });
}
