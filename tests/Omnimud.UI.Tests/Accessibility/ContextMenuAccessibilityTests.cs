using System.Reflection;
using System.Windows.Forms;
using FluentAssertions;
using Omnimud.UI.Services.Accessibility;
using Omnimud.UI.Tests.Services;

namespace Omnimud.UI.Tests.Accessibility;

/// <summary>
/// A context menu of WinForms opens in silence for a screen reader (no "menu opened" event the first time and no
/// item with the focus). <see cref="ContextMenuAccessibility"/> is the cure; these tests keep it in place.
/// </summary>
public class ContextMenuAccessibilityTests
{
    [Fact]
    public void Opening_the_menu_selects_the_first_item_so_the_reader_gets_a_focus_event()
    {
        WithOpenMenu(attach: menu => ContextMenuAccessibility.Attach(menu), build: menu =>
        {
            menu.Items.Add(new ToolStripMenuItem("Conectar"));
            menu.Items.Add(new ToolStripMenuItem("Editar"));
        }, check: menu =>
        {
            menu.Items[0].Selected.Should().BeTrue();
            menu.Items[1].Selected.Should().BeFalse();
        });
    }

    [Fact]
    public void Items_that_cannot_be_chosen_are_skipped()
    {
        WithOpenMenu(attach: menu => ContextMenuAccessibility.Attach(menu), build: menu =>
        {
            menu.Items.Add(new ToolStripMenuItem("Oculto") { Available = false });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Deshabilitado") { Enabled = false });
            menu.Items.Add(new ToolStripMenuItem("Editar"));
        }, check: menu => menu.Items.Cast<ToolStripItem>().Single(i => i.Selected).Text.Should().Be("Editar"));
    }

    [Fact]
    public void A_menu_with_nothing_to_choose_opens_without_selecting_anything()
    {
        WithOpenMenu(attach: menu => ContextMenuAccessibility.Attach(menu), build: menu =>
        {
            menu.Items.Add(new ToolStripMenuItem("Deshabilitado") { Enabled = false });
        }, check: menu => menu.Items[0].Selected.Should().BeFalse());
    }

    [Fact]
    public void A_menu_opened_with_the_mouse_keeps_the_usual_look_with_nothing_highlighted()
    {
        WithOpenMenu(attach: menu => ContextMenuAccessibility.Attach(menu, openedWithMouse: () => true), build: menu =>
        {
            menu.Items.Add(new ToolStripMenuItem("Conectar"));
        }, check: menu => menu.Items[0].Selected.Should().BeFalse());
    }

    [Fact]
    public void Items_added_while_opening_are_covered_when_the_fix_is_attached_after_the_handler_that_fills_the_menu()
    {
        WithOpenMenu(build: menu => menu.Opening += (_, e) =>
        {
            menu.Items.Clear();
            menu.Items.Add(new ToolStripMenuItem("Recién creado"));
            e.Cancel = false; // WinForms cancels by default the opening of a menu that starts empty (the launcher does this too)
        }, attach: menu => ContextMenuAccessibility.Attach(menu), check: menu =>
        {
            menu.Items[0].Selected.Should().BeTrue();
            AccessibleObjectExists(menu.Items[0]).Should().BeTrue();
        });
    }

    /// <summary>
    /// WinForms only raises "menu opened" and the focus of an item when their accessible objects already exist
    /// (internal IsAccessibilityObjectCreated). If this reflection stops finding the property, WinForms has changed:
    /// listen again from outside the process before deleting the test.
    /// </summary>
    [Fact]
    public void The_accessible_objects_exist_before_the_menu_is_open()
    {
        var existedOnOpened = false;
        WithOpenMenu(build: menu =>
        {
            menu.Items.Add(new ToolStripMenuItem("Conectar"));
            AccessibleObjectExists(menu).Should().BeFalse("nobody has asked for it yet: this is why WinForms stays silent");
        }, attach: menu =>
        {
            ContextMenuAccessibility.Attach(menu);
            menu.Opened += (_, _) => existedOnOpened = AccessibleObjectExists(menu) && AccessibleObjectExists(menu.Items[0]);
        }, check: _ => existedOnOpened.Should().BeTrue());
    }

    [Fact]
    public void A_cancelled_opening_does_nothing()
    {
        Sta.Run(() =>
        {
            using var form = new Form { ShowInTaskbar = false };
            using var menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripMenuItem("Conectar"));
            menu.Opening += (_, e) => e.Cancel = true;
            ContextMenuAccessibility.Attach(menu);
            form.Show();
            menu.Show(form, new Point(5, 5));
            Application.DoEvents();
            menu.Visible.Should().BeFalse();
            menu.Items[0].Selected.Should().BeFalse();
        });
    }

    /// <summary>Every context menu of the application has to carry the fix: a new one without it would be silent.</summary>
    [Fact]
    public void Every_file_that_creates_a_context_menu_attaches_the_fix()
    {
        var source = Path.Combine(HelpAndAppInfoTests.RepositoryRoot(), "src", "Omnimud.UI");
        var offenders = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(file => Path.GetFileName(file) != "ContextMenuAccessibility.cs")
            .Where(file =>
            {
                var text = File.ReadAllText(file);
                return text.Contains("new ContextMenuStrip") || text.Contains("ContextMenuStrip _") && text.Contains("= new()");
            })
            .Where(file => !File.ReadAllText(file).Contains("ContextMenuAccessibility.Attach"))
            .Select(Path.GetFileName)
            .ToList();

        offenders.Should().BeEmpty("a ContextMenuStrip without ContextMenuAccessibility.Attach opens in silence for NVDA and JAWS");
    }

    private static bool AccessibleObjectExists(object component)
    {
        var property = component.GetType().GetProperty("IsAccessibilityObjectCreated", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        property.Should().NotBeNull("WinForms decides with it whether to raise accessibility events");
        return (bool)property!.GetValue(component)!;
    }

    private static void WithOpenMenu(Action<ContextMenuStrip> build, Action<ContextMenuStrip> attach, Action<ContextMenuStrip> check)
    {
        Sta.Run(() =>
        {
            using var form = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(40, 40) };
            using var menu = new ContextMenuStrip();
            build(menu);
            attach(menu);
            form.Show();
            try
            {
                menu.Show(form, new Point(5, 5));
                Application.DoEvents();
                menu.Visible.Should().BeTrue();
                check(menu);
            }
            finally
            {
                menu.Close();
                form.Close();
            }
        });
    }
}
