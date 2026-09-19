using System.Text.Json;
using Omnimud.Core.Resources;

namespace Omnimud.Core.Actions;

/// <summary>
/// GMCP <c>Char.Inventory { "items": [ item... ] }</c>; every package replaces the previous one.
/// An item is a leaf <c>{ "short", "actions": [ { "action", "label", "cmd" } ] }</c> or a group of
/// identical objects <c>{ "short": "potion (3)", "children": [ item... ] }</c>.
///
/// Each item becomes a submenu named after it with one entry per action (<c>label</c> is what is
/// shown, <c>cmd</c> what is sent); a group becomes a submenu of submenus. An item without any
/// usable action is kept as a disabled line: the menu doubles as a list of what is being carried,
/// and a missing object would be more confusing than one that can only be read. Entries without
/// text (no <c>short</c>, no <c>label</c> nor <c>action</c>, no <c>cmd</c>) are dropped.
/// </summary>
public sealed class CharInventoryTranslator : IActionMenuTranslator
{
    public string Package => "Char.Inventory";

    public string SupportedModule => "Char.Inventory 1";

    public string SectionLabel => Strings.Actions_Inventory;

    public IReadOnlyList<ActionMenuNode>? Translate(string payload)
    {
        using var document = ActionMenuJson.ParseObject(payload);
        if (document is null) return null;

        var items = ActionMenuJson.Array(document.RootElement, "items", out var valid);
        return valid ? Nodes(items.Select(i => Item(i, 1))) : null;
    }

    private static ActionMenuNode? Item(JsonElement item, int level)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;
        var name = ActionMenuJson.String(item, "short");

        // Deeper than the menu will ever show: not worth translating.
        var children = level > ActionMenuLimits.MaxDepth
            ? []
            : ActionMenuJson.Array(item, "children", out _).Select(c => Item(c, level + 1));
        var actions = ActionMenuJson.Array(item, "actions", out _).Select(Action);

        return ActionMenuNode.Submenu(name, children.Concat(actions)) ?? ActionMenuNode.Information(name);
    }

    private static ActionMenuNode? Action(JsonElement action)
    {
        var label = ActionMenuJson.String(action, "label");
        if (string.IsNullOrWhiteSpace(label)) label = ActionMenuJson.String(action, "action");
        return ActionMenuNode.Action(label, ActionMenuJson.String(action, "cmd"));
    }

    private static ActionMenuNode[] Nodes(IEnumerable<ActionMenuNode?> nodes)
        => nodes.Where(n => n is not null).Select(n => n!).ToArray();
}
