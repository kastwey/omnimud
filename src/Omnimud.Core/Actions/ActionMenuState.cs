namespace Omnimud.Core.Actions;

/// <summary>
/// Keeps the last version of every section and composes the menu: one submenu per translator, in
/// the order the translators were given, leaving out the sections that have nothing.
///
/// Updates come from one thread at a time (the session's queue); <see cref="Current"/> is an
/// immutable snapshot that can be read from any thread.
/// </summary>
public sealed class ActionMenuState
{
    private readonly IReadOnlyList<IActionMenuTranslator> _translators;
    private readonly IReadOnlyList<ActionMenuNode>[] _sections;
    private volatile ActionMenu _current = ActionMenu.Empty;

    public ActionMenuState(IReadOnlyList<IActionMenuTranslator>? translators = null)
    {
        _translators = translators ?? ActionMenuTranslators.Default;
        _sections = new IReadOnlyList<ActionMenuNode>[_translators.Count];
        Array.Fill(_sections, []);
    }

    public ActionMenu Current => _current;

    /// <summary>What to announce in Core.Supports.Set so that the MUD sends the packages.</summary>
    public IEnumerable<string> SupportedModules => _translators.Select(t => t.SupportedModule);

    /// <summary>
    /// A package arrived. Returns true when it belonged to a section and the menu was replaced.
    /// Unknown packages and payloads that make no sense change nothing.
    /// </summary>
    public bool Update(string? package, string? payload)
    {
        if (string.IsNullOrEmpty(package) || payload is null) return false;

        var changed = false;
        for (var i = 0; i < _translators.Count; i++)
        {
            if (!_translators[i].Package.Equals(package, StringComparison.OrdinalIgnoreCase)) continue;

            IReadOnlyList<ActionMenuNode>? nodes;
            try
            {
                nodes = _translators[i].Translate(payload);
            }
            catch (Exception ex)
            {
                // The contract says translators do not throw; one that does must not take the session down.
                System.Diagnostics.Debug.WriteLine(ex);
                nodes = null;
            }
            if (nodes is null) continue;

            _sections[i] = ActionMenuLimits.Apply(nodes);
            changed = true;
        }

        if (changed) Compose();
        return changed;
    }

    /// <summary>Forgets everything (disconnection). Returns true when there was something to forget.</summary>
    public bool Clear()
    {
        var had = !_current.IsEmpty;
        Array.Fill(_sections, []);
        _current = ActionMenu.Empty;
        return had;
    }

    private void Compose()
    {
        var sections = new List<ActionMenuNode>(_sections.Length);
        for (var i = 0; i < _sections.Length; i++)
        {
            if (ActionMenuNode.Submenu(_translators[i].SectionLabel, _sections[i]) is { } section)
                sections.Add(section);
        }

        _current = sections.Count == 0 ? ActionMenu.Empty : new ActionMenu(sections);
    }
}
