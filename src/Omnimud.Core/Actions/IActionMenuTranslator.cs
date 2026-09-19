namespace Omnimud.Core.Actions;

/// <summary>
/// Turns one kind of package of a MUD into one section of the actions menu. Pure: same payload,
/// same nodes; no state, no side effects. Supporting another MUD or another package is writing
/// one of these and adding it to <see cref="ActionMenuTranslators.Default"/> (or to the session
/// settings); limits, composition, the session and the menus need no change.
/// </summary>
public interface IActionMenuTranslator
{
    /// <summary>GMCP package it understands ("Char.Inventory"). Compared without case.</summary>
    string Package { get; }

    /// <summary>Module announced to the MUD in Core.Supports.Set ("Char.Inventory 1").</summary>
    string SupportedModule { get; }

    /// <summary>Visible, localized name of its section ("Inventory").</summary>
    string SectionLabel { get; }

    /// <summary>
    /// The nodes of the section; empty when the package says there is nothing. Null when the payload
    /// is not what was expected (the section then keeps what it had). Never throws.
    /// </summary>
    IReadOnlyList<ActionMenuNode>? Translate(string payload);
}

public static class ActionMenuTranslators
{
    /// <summary>The packages OMnimud understands, in the order their sections appear in the menu.</summary>
    public static IReadOnlyList<IActionMenuTranslator> Default { get; } = [new CharInventoryTranslator(), new RoomInfoTranslator()];
}
