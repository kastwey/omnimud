using System.Text.Json;
using Omnimud.Core.Resources;

namespace Omnimud.Core.Actions;

/// <summary>
/// GMCP <c>Room.Info { "id", "short", "long"?, "exits": [ "north"... ], "items": [...] }</c>:
/// one action per exit, whose command is the exit itself. Exits that are not text are dropped.
/// </summary>
public sealed class RoomInfoTranslator : IActionMenuTranslator
{
    public string Package => "Room.Info";

    public string SupportedModule => "Room.Info 1";

    public string SectionLabel => Strings.Actions_Exits;

    public IReadOnlyList<ActionMenuNode>? Translate(string payload)
    {
        using var document = ActionMenuJson.ParseObject(payload);
        if (document is null) return null;

        var exits = ActionMenuJson.Array(document.RootElement, "exits", out var valid);
        if (!valid) return null;

        return exits
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString())
            .Select(exit => ActionMenuNode.Action(exit, exit))
            .Where(n => n is not null)
            .Select(n => n!)
            .ToArray();
    }
}
