using System.Globalization;
using Omnimud.UI.Resources;
using Omnimud.UI.Tests.Accessibility;

namespace Omnimud.UI.Tests.Forms;

/// <summary>Helpers shared by the tests of the alias, trigger and path windows (block T).</summary>
internal static class ListFormsTestSupport
{
    public static readonly string[] Cultures = ["es", "en"];

    public static void UseCulture(string culture) => Strings.Culture = CultureInfo.GetCultureInfo(culture);

    /// <summary>Builds the form in the given language, optionally puts it in a state, and audits it.</summary>
    public static void AssertAccessible<TForm>(string culture, Func<TForm> create, Action<TForm>? arrange = null) where TForm : Form
    {
        UseCulture(culture);
        using var form = create();
        arrange?.Invoke(form);
        AccessibilityAudit.Check(form, isDialog: true).Should().BeEmpty();
    }

    public static T Find<T>(this Form form, string name) where T : Control =>
        form.Controls.Find(name, searchAllChildren: true).OfType<T>().Single();

    /// <summary>Button.PerformClick does nothing on a window that is not shown; this raises Click the same way.</summary>
    public static void Press(this Button button)
    {
        button.Enabled.Should().BeTrue("a disabled button cannot be pressed");
        typeof(Button).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(button, [EventArgs.Empty]);
    }

    public static IEnumerable<string> SelectedTexts(this ListView list) =>
        list.SelectedItems.Cast<ListViewItem>().Select(i => i.Text);

    public static IEnumerable<string> RowTexts(this ListView list) => list.Items.Cast<ListViewItem>().Select(i => i.Text);
}
