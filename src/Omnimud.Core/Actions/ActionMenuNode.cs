namespace Omnimud.Core.Actions;

/// <summary>
/// One entry of the actions menu. Immutable. It is an <b>action</b> when it has a command and no
/// children, a <b>submenu</b> when it has children, and otherwise plain information that is shown
/// disabled ("… and 12 more").
///
/// The content comes from the MUD and is not trusted, so the only way to build a node is through
/// the factories, which clean label and command (<see cref="ActionMenuText"/>): whoever holds a node
/// knows that its command is one single line of text for the MUD and nothing else.
/// </summary>
public sealed class ActionMenuNode
{
    private ActionMenuNode(string label, string? command, IReadOnlyList<ActionMenuNode> children)
    {
        Label = label;
        Command = command;
        Children = children;
    }

    public string Label { get; }

    /// <summary>Text sent to the MUD as if the user had typed it. Null for submenus and information.</summary>
    public string? Command { get; }

    public IReadOnlyList<ActionMenuNode> Children { get; }

    public bool IsAction => Command is not null && Children.Count == 0;

    public bool IsSubmenu => Children.Count > 0;

    /// <summary>Null when the label or the command is empty once cleaned.</summary>
    public static ActionMenuNode? Action(string? label, string? command)
    {
        var cleanLabel = ActionMenuText.CleanLabel(label);
        var cleanCommand = ActionMenuText.CleanCommand(command);
        return cleanLabel.Length == 0 || cleanCommand.Length == 0 ? null : new ActionMenuNode(cleanLabel, cleanCommand, []);
    }

    /// <summary>Null when the label is empty once cleaned or there are no children.</summary>
    public static ActionMenuNode? Submenu(string? label, IEnumerable<ActionMenuNode?> children)
    {
        var cleanLabel = ActionMenuText.CleanLabel(label);
        var list = children.Where(c => c is not null).Select(c => c!).ToArray();
        return cleanLabel.Length == 0 || list.Length == 0 ? null : new ActionMenuNode(cleanLabel, null, list);
    }

    /// <summary>A line that can be read but not chosen. Null when the label is empty once cleaned.</summary>
    public static ActionMenuNode? Information(string? label)
    {
        var cleanLabel = ActionMenuText.CleanLabel(label);
        return cleanLabel.Length == 0 ? null : new ActionMenuNode(cleanLabel, null, []);
    }

    public override string ToString() => IsSubmenu ? $"{Label} ({Children.Count})" : IsAction ? $"{Label} → {Command}" : Label;
}
