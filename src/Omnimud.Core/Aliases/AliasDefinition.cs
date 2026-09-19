namespace Omnimud.Core.Aliases;

public sealed record AliasDefinition(string Command, string Action, bool Enabled = true);
