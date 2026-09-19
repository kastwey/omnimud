namespace Omnimud.UI.Tests.Accessibility;

/// <summary>
/// The accessibility rules every Omnimud form must satisfy (docs/02_PLAN_PARIDAD_V2.md §4.1),
/// checked by walking the control tree of a live form instance.
/// </summary>
internal static class AccessibilityAudit
{
    public static IReadOnlyList<string> Check(Form form, bool isDialog)
    {
        var problems = new List<string>();
        var controls = Descendants(form).ToList();

        foreach (var control in controls)
        {
            if (NeedsLabel(control))
            {
                if (string.IsNullOrWhiteSpace(control.AccessibleName))
                    problems.Add($"{Describe(control)}: sin AccessibleName.");

                var label = PrecedingLabel(control);
                if (label is null)
                    problems.Add($"{Describe(control)}: no tiene una Label visible justo antes en el orden de tabulación.");
                else if (Mnemonic(label.Text) is null && label.UseMnemonic)
                    problems.Add($"{Describe(control)}: su etiqueta '{label.Text}' no tiene mnemónico (&).");
            }
            else if (IsTextNamed(control))
            {
                if (string.IsNullOrWhiteSpace(control.Text) && string.IsNullOrWhiteSpace(control.AccessibleName))
                    problems.Add($"{Describe(control)}: sin texto ni AccessibleName.");
            }
        }

        problems.AddRange(DuplicateMnemonics(form, controls));
        problems.AddRange(MenuProblems(form, controls));

        if (isDialog)
        {
            if (form.CancelButton is null)
                problems.Add($"{form.Name}: diálogo sin CancelButton (Escape no cierra).");
            if (form.AcceptButton is null)
                problems.Add($"{form.Name}: diálogo sin AcceptButton.");
        }

        if (string.IsNullOrWhiteSpace(form.Text))
            problems.Add($"{form.Name}: ventana sin título.");

        return problems;
    }

    private static bool NeedsLabel(Control c) =>
        c is TextBoxBase or ComboBox or ListBox or ListView or TreeView or NumericUpDown or DataGridView or DateTimePicker
        && c.Parent is not UpDownBase; // the inner edit of a NumericUpDown

    private static bool IsTextNamed(Control c) => c is ButtonBase;

    private static Label? PrecedingLabel(Control control)
    {
        if (control.Parent is null) return null;
        return control.Parent.Controls.OfType<Label>()
            .FirstOrDefault(l => l.TabIndex == control.TabIndex - 1 && !string.IsNullOrWhiteSpace(l.Text));
    }

    private static IEnumerable<string> DuplicateMnemonics(Form form, IEnumerable<Control> controls)
    {
        var owners = new List<(char Key, string Owner)>();
        foreach (var c in controls)
        {
            var usesMnemonic = c switch
            {
                Label l => l.UseMnemonic,
                ButtonBase b => b.UseMnemonic,
                GroupBox => true,
                _ => false,
            };
            if (usesMnemonic && Mnemonic(c.Text) is { } key)
                owners.Add((key, $"{c.GetType().Name} '{c.Text}'"));
        }

        if (form.MainMenuStrip is { } menu)
            foreach (ToolStripItem item in menu.Items)
                if (Mnemonic(item.Text) is { } key)
                    owners.Add((key, $"menú '{item.Text}'"));

        return owners.GroupBy(o => o.Key)
            .Where(g => g.Count() > 1)
            .Select(g => $"{form.Name}: mnemónico Alt+{g.Key} repetido en {string.Join(", ", g.Select(o => o.Owner))}.");
    }

    private static IEnumerable<string> MenuProblems(Form form, IEnumerable<Control> controls)
    {
        var problems = new List<string>();
        var shortcuts = new Dictionary<Keys, string>();

        foreach (var strip in controls.OfType<MenuStrip>())
            foreach (ToolStripItem top in strip.Items)
                Walk(top);

        return problems;

        void Walk(ToolStripItem item)
        {
            if (item is not ToolStripMenuItem menuItem) return;

            if (string.IsNullOrWhiteSpace(menuItem.Text))
                problems.Add($"{form.Name}: elemento de menú '{menuItem.Name}' sin texto.");

            if (menuItem.ShortcutKeys != Keys.None)
            {
                var key = menuItem.ShortcutKeys & Keys.KeyCode;
                if (key is Keys.Insert or Keys.CapsLock)
                    problems.Add($"{form.Name}: '{menuItem.Text}' usa {key}, la tecla modificadora de JAWS y NVDA.");
                if (!shortcuts.TryAdd(menuItem.ShortcutKeys, menuItem.Text ?? menuItem.Name ?? "?"))
                    problems.Add($"{form.Name}: atajo {menuItem.ShortcutKeys} repetido en '{menuItem.Text}' y '{shortcuts[menuItem.ShortcutKeys]}'.");
            }

            var siblings = new Dictionary<char, string>();
            foreach (ToolStripItem child in menuItem.DropDownItems)
            {
                if (child is ToolStripMenuItem && Mnemonic(child.Text) is { } m && !siblings.TryAdd(m, child.Text!))
                    problems.Add($"{form.Name}: mnemónico '{m}' repetido dentro del menú '{menuItem.Text}': '{child.Text}' y '{siblings[m]}'.");
                Walk(child);
            }
        }
    }

    /// <summary>The character after a single '&amp;' ("&amp;&amp;" is a literal ampersand).</summary>
    internal static char? Mnemonic(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        for (var i = 0; i < text.Length - 1; i++)
        {
            if (text[i] != '&') continue;
            if (text[i + 1] == '&') { i++; continue; }
            return char.ToUpperInvariant(text[i + 1]);
        }
        return null;
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var grandChild in Descendants(child))
                yield return grandChild;
        }
    }

    private static string Describe(Control c) => $"{c.FindForm()?.Name}.{(string.IsNullOrEmpty(c.Name) ? c.GetType().Name : c.Name)}";
}
