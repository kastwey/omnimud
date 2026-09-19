using Omnimud.UI.Forms;
using Omnimud.UI.Presenters;

namespace Omnimud.UI.Tests.Forms;

public class FrmPickCharacterTests
{
    private static readonly CharacterChoice[] Choices = [new(3, "Aragorn", "Otro"), new(2, "Frodo", "Reinos")];

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void PassesTheAccessibilityAudit(string culture) => Sta.Run(() =>
    {
        ListFormsTestSupport.AssertAccessible(culture, () => new FrmPickCharacter(Choices));
        ListFormsTestSupport.AssertAccessible(culture, () => new FrmPickCharacter([]));
    });

    [Fact]
    public void ListsNameAndMud_WithTheFirstSelected() => Sta.Run(() =>
    {
        ListFormsTestSupport.UseCulture("es");
        using var form = new FrmPickCharacter(Choices);

        form.Text.Should().Be("Importar desde otro personaje");
        form.List.AccessibleName.Should().Be("Personaje");
        form.List.Items.Cast<object>().Select(form.List.GetItemText).Should().Equal("Aragorn (Otro)", "Frodo (Reinos)");
        form.Selected.Should().Be(Choices[0]);
    });

    [Fact]
    public void Ok_ReturnsTheSelectedCharacter() => Sta.Run(() =>
    {
        using var form = new FrmPickCharacter(Choices);
        form.List.SelectedIndex = 1;

        form.Find<Button>("_btnOk").Press();

        form.DialogResult.Should().Be(DialogResult.OK);
        form.Selected!.Id.Should().Be(2);
    });

    [Fact]
    public void Empty_CannotBeAccepted() => Sta.Run(() =>
    {
        using var form = new FrmPickCharacter([]);
        form.Find<Button>("_btnOk").Enabled.Should().BeFalse();
        form.Selected.Should().BeNull();
    });
}
