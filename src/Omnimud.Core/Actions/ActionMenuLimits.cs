using Omnimud.Core.Resources;

namespace Omnimud.Core.Actions;

/// <summary>
/// Hard limits of the actions menu. Its content comes from the MUD, so nothing the server sends
/// may turn into a menu that is endless, bottomless or slow to build. Every translator gets them
/// for free: <see cref="ActionMenuState"/> applies them to whatever a translator returns.
/// </summary>
public static class ActionMenuLimits
{
    /// <summary>Levels below a section (item, child of a group, its actions...). Deeper nodes are dropped.</summary>
    public const int MaxDepth = 4;

    /// <summary>Content nodes of one section, all levels together.</summary>
    public const int MaxNodesPerSection = 200;

    /// <summary>Content nodes of one menu or submenu.</summary>
    public const int MaxChildrenPerNode = 40;

    public const int MaxLabelLength = 80;

    public const int MaxCommandLength = 255;

    /// <summary>A bigger package is ignored without looking at it (UTF-16 characters, so at least as strict as bytes).</summary>
    public const int MaxPayloadLength = 256 * 1024;

    /// <summary>
    /// Cuts the nodes of one section down to the limits. Where siblings are left out, the menu ends
    /// with a disabled "… and N more" (it does not count as content). A submenu left without
    /// content disappears.
    /// </summary>
    public static IReadOnlyList<ActionMenuNode> Apply(IReadOnlyList<ActionMenuNode> nodes)
    {
        var budget = MaxNodesPerSection;
        return Limit(nodes, 1, ref budget);
    }

    private static IReadOnlyList<ActionMenuNode> Limit(IReadOnlyList<ActionMenuNode> nodes, int level, ref int budget)
    {
        var result = new List<ActionMenuNode>(Math.Min(nodes.Count, MaxChildrenPerNode) + 1);
        var content = 0;

        for (var i = 0; i < nodes.Count; i++)
        {
            if (content == MaxChildrenPerNode || budget == 0)
            {
                if (ActionMenuNode.Information(string.Format(Strings.Actions_More, nodes.Count - i)) is { } more)
                    result.Add(more);
                break;
            }

            var node = nodes[i];
            if (!node.IsSubmenu)
            {
                result.Add(node);
                content++;
                budget--;
                continue;
            }

            // A submenu needs room for itself and, one level down, for at least one child.
            if (level >= MaxDepth || budget < 2) continue;

            budget--;
            var children = Limit(node.Children, level + 1, ref budget);
            if (children.Count == 0)
            {
                // Everything inside was too deep or did not fit.
                budget++;
                continue;
            }

            result.Add(ActionMenuNode.Submenu(node.Label, children)!);
            content++;
        }

        return result;
    }
}
