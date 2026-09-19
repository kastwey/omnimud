namespace Omnimud.Data.Seed;

public sealed record BuiltInMessageRule(string Pattern, string Template = "$0", bool CaseSensitive = true, string? Channel = null);

/// <param name="Rules">The per-line regex translation (schema version 4). Kept for reference; not evaluated while the set has a script.</param>
/// <param name="Script">The Lua port of the original rule (schema version 6): what the set really runs.</param>
public sealed record BuiltInMessageRuleSet(string Name, IReadOnlyList<BuiltInMessageRule> Rules, string Script);

/// <summary>
/// The four message rules shipped with the original client (trunk\ProcessRules\*.cs).
///
/// Since schema version 6 each built-in set is a LUA SCRIPT (Seed\MessageRuleScripts\*.lua, embedded)
/// that sees the whole received block like the original DLLs did, so wrapped messages, Simauria's
/// "previous line is empty" condition and Balzhur's accumulation are back. See each script's header
/// and docs\API_LUA.md ("Reglas de mensajes") for the contract.
///
/// The per-line regex rules below are the translation that schema version 4 seeded. They are kept in
/// the built-in sets for reference (a set with a script does not evaluate them): a user who duplicates
/// a set and switches the copy to "patterns" gets them back. What follows documents THEIR limits:
///
/// General differences that a per-line rule cannot reproduce (they apply to every set):
///  * The originals returned ONE string per received block. Here every matching line becomes its
///    own message, so a block with three chat lines yields three messages instead of one blob
///    (Balzhur glued them without separator; Callandor, Simauria and Cyberlife dropped all but the first).
///  * Callandor and Simauria extended the message over the FOLLOWING lines of the block (word-wrapped
///    speech) until a closing apostrophe or the end of the block. A per-line rule only sees the first
///    line: wrapped messages are captured up to the end of their first line.
///  * Simauria channel lines ("[Canal] ...") were only accepted when the PREVIOUS line of the block was
///    empty and it was not the first line. That context is not visible to a per-line regex, so any
///    line starting with '[' that contains ']' is taken.
/// Word tests were done on String.Split(' '): Balzhur with RemoveEmptyEntries (runs of spaces and
/// leading spaces are irrelevant, hence " *" and " +"), Callandor and Simauria with None (exactly one
/// space; the first word may even be empty, hence "[^ ]*"). All comparisons were ordinal, so those
/// three sets are case sensitive; Cyberlife used RegexOptions.IgnoreCase.
/// </summary>
public static class BuiltInMessageRuleSets
{
    public const string Balzhur = "Balzhur";
    public const string Callandor = "Callandor";
    public const string Simauria = "Simauria";
    public const string Cyberlife = "Cyberlife";

    private static readonly string[] BalzhurChannels =
        ["Clan", "Faccion", "Novatos", "Orden", "Concilio", "Gremio", "Raza", "Avatar", "Reino", "Trivial", "GOSOCIAL"];

    public static IReadOnlyList<BuiltInMessageRuleSet> All { get; } =
    [
        new(Balzhur, BuildBalzhur(), LoadScript(Balzhur)),
        new(Callandor, BuildCallandor(), LoadScript(Callandor)),
        new(Simauria, BuildSimauria(), LoadScript(Simauria)),
        new(Cyberlife, BuildCyberlife(), LoadScript(Cyberlife))
    ];

    /// <summary>The Lua script of a built-in set, or null if there is no built-in set with that name.</summary>
    public static string? ScriptOf(string name) =>
        All.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Script;

    private static string LoadScript(string name)
    {
        var resource = $"Omnimud.Data.Seed.MessageRuleScripts.{name}.lua";
        using var stream = typeof(BuiltInMessageRuleSets).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded script '{resource}' not found.");
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    }

    private static List<BuiltInMessageRule> BuildBalzhur()
    {
        var rules = new List<BuiltInMessageRule>();

        // words[0]=="[Canal:" && words[1].EndsWith("]:") && words[2].StartsWith("'")
        //   || words[0]=="[Canal]:" || words[0]=="[Canal]"   (always with at least a second word).
        // The original threw IndexOutOfRange (no message) for "[Canal: x]:" without a third word; the regex
        // simply does not match, which is the same visible result.
        foreach (var channel in BalzhurChannels)
        {
            rules.Add(new BuiltInMessageRule(
                $@"^ *\[{channel}(?:: +[^ ]*\]: +'|\]:? +[^ ]).*$", Channel: channel));
        }

        // Own speech: line.StartsWith(...) on the raw line.
        rules.Add(new BuiltInMessageRule(@"^(?:Charlas '|Cuentas a |Dices '|Respondes a |Susurras a ).*$"));
        // X charla '...
        rules.Add(new BuiltInMessageRule(@"^ *[^ ]+ +charla +'.*$"));
        // X te responde|cuenta|susurra '...
        rules.Add(new BuiltInMessageRule(@"^ *[^ ]+ +te +(?:responde|cuenta|susurra) +'.*$"));
        return rules;
    }

    private static List<BuiltInMessageRule> BuildCallandor() =>
    [
        // Own speech (prefixes). "Instruyes" has no trailing space or apostrophe in the original.
        new(@"^(?:Solicitas '|Instruyes|Dices '|Charlas '|Transmites a |<Comunicas> '|Susurras '|Gritas '|Conspiras '|Gruñes '|Transmites cariñosamente a ).*$"),
        // X te transmite '...   ·   X te transmite cariñosamente '...
        new(@"^[^ ]* te transmite (?:cariñosamente )?'.*$"),
        // X dice al grupo '... · X gruñe al grupo '... · X dice al equipo '...
        new(@"^[^ ]* (?:(?:dice|gruñe) al grupo|dice al equipo) '.*$"),
        // X dice|charla|conspira|comunica|gruñe '...
        new(@"^[^ ]* (?:dice|charla|conspira|comunica|gruñe) '.*$"),
        // X susurra '...
        new(@"^[^ ]* susurra '.*$"),
        // X grita cerca de aqui '... ("aqui" without accent, as in the original) · X grita '...
        new(@"^[^ ]* grita (?:cerca de aqui )?'.*$"),
        // <Canal> conversa '...
        new(@"^<[^ ]*> conversa '.*$"),
        // X solicita '...
        new(@"^[^ ]* solicita '.*$"),
        // Trivial: X '...
        new(@"^Trivial: [^ ]* '.*$", Channel: "Trivial")
    ];

    private static List<BuiltInMessageRule> BuildSimauria() =>
    [
        // Channel line. See the class remarks: the "previous line is empty" condition is lost.
        new(@"^\[.*\].*$"),
        // X te dice: '...  → the original returned everything to the end of the block.
        new(@"^[^ ]* te dice: '.*$"),
        // Dijiste a X: ...  → idem.
        new(@"^Dijiste a [^ ]+: .*$"),
        // X dice: '...  and  Dices: '...  → cut after the LAST apostrophe; without a closing apostrophe
        // on the line (wrapped speech) the whole line is kept instead of the original's bare "X dice: '".
        new(@"^[^ ]* dice: '(?:.*'|.*$)"),
        new(@"^Dices: '(?:.*'|.*$)")
    ];

    private static List<BuiltInMessageRule> BuildCyberlife()
    {
        // The 15 original expressions, verbatim and in the same order. They were compiled with
        // Multiline | IgnoreCase and matched against the block; per line, ^ and $ mean the same.
        // The original tried each regex against the whole block and returned the first regex (not the
        // first line) that matched; per line, every line is tested against the rules in order.
        string[] patterns =
        [
            "^(Murmuras|Dices) con acento .+?, \".+?\"$",
            "^(Murmuras|Dices): \".+?\"$",
            "^gritas: \".+?\"$",
            "^.+? grita: \".+?\"$",
            "^.+? grita cerca de aquí: \".+?\"$",
            "^\\[.+?\\] .+?: \".+?\"$",
            "^\\[.+?:\\] \".+?\"$",
            "^.+? (Murmura|Dice) con acento .+?, \".+?\"$",
            "^.+? (Murmura|Dice): \".+?\"$",
            "^\".+? chatea: \".+?\"$", // sic: the leading quote is in the original
            "^Transmites a .+?, \".+?\"$",
            "^.+? te transmite, \".+?\"$",
            "^\\*{2,} .+? Ha solicitado asistencia con el siguiente motivo: .+?\\*{2,}$",
            "^.+?(te)? dice por teléfono, \".+?\"$",
            "^dices por teléfono, \".+?\"$"
        ];
        return patterns.Select(p => new BuiltInMessageRule(p, CaseSensitive: false)).ToList();
    }
}
