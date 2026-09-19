using Omnimud.Core.Messages;
using Omnimud.Core.Scripting;
using Omnimud.Data.Seed;

namespace Omnimud.Data.Tests.Seed;

/// <summary>
/// Fidelity of the four built-in Lua scripts. Every block is run through the script (exactly as a
/// session does, <see cref="MessageRuleScript"/>) and through the ORACLE, the original C# rule
/// ported verbatim (<see cref="OriginalProcessRules"/>). The blocks are deduced from the conditions
/// of the original code and cover each of its branches; a seeded fuzz mixes them into random blocks.
///
/// How the original text maps to a v2 block: the original got the raw chunk, where every complete
/// line ends in '\n' and the prompt usually follows; v2 hands the script the complete lines only.
/// Deliberate differences, each with its own test below:
///  * Callandor: the original returned Substring(0, index + 3), i.e. the line break and ONE MORE
///    character after the closing quote (and threw, losing the message, when nothing followed).
///    The script stops at the closing quote. It also goes on looking for more messages after it;
///    the original returned only the first one of the block.
///  * Balzhur: the original glued chat lines without any separator and channel lines with "\r\n";
///    the script joins all of them with '\n'. "[Canal: Nombre]:" without a third word made the
///    original throw and lose the whole block; the script just does not take that line.
///  * Simauria: "Dijiste a  x" (double space) made the original throw; the script skips the line.
///  * Cyberlife: the original returned the first match of the first regex that matched anywhere in
///    the block; the script returns every matching line, in block order.
/// </summary>
public sealed class BuiltInMessageScriptsTests(Xunit.Abstractions.ITestOutputHelper output) : IDisposable
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> Warmed = new();
    private readonly LuaScriptEngine _engine = new();

    public void Dispose() => _engine.Dispose();

    private async Task<IReadOnlyList<string>> LuaAsync(string set, string block)
    {
        var script = BuiltInMessageRuleSets.ScriptOf(set);
        script.Should().NotBeNullOrWhiteSpace();
        // The very first run in a process loads the interpreter, which does not fit in the 100 ms of a live rule.
        if (Warmed.TryAdd(set, true)) await MessageRuleScript.WarmUpAsync(_engine, script!);
        var result = await MessageRuleScript.RunAsync(_engine, script!, MessageRuleScript.SplitLines(block));
        result.Error.Should().BeNull($"the {set} script must run cleanly over:\n{block}");
        return result.Messages;
    }

    private const string NL = "\n";

    private static string Strip(string? text) => (text ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);

    // ── General ────────────────────────────────────────────────────────────

    [Fact]
    public void EveryBuiltInSet_HasAScript_ThatCompiles()
    {
        BuiltInMessageRuleSets.All.Should().HaveCount(4);
        foreach (var set in BuiltInMessageRuleSets.All)
        {
            set.Script.Should().NotBeNullOrWhiteSpace();
            set.Script.Should().NotContain("\r");
            _engine.Validate(set.Script).Should().BeNull($"the {set.Name} script must compile");
            BuiltInMessageRuleSets.ScriptOf(set.Name.ToUpperInvariant()).Should().Be(set.Script);
        }
        BuiltInMessageRuleSets.ScriptOf("Nada").Should().BeNull();
        BuiltInMessageRuleSets.ScriptOf(BuiltInMessageRuleSets.Callandor).Should().Contain("Gruñes").And.Contain("cariñosamente");
        BuiltInMessageRuleSets.ScriptOf(BuiltInMessageRuleSets.Cyberlife).Should().Contain("teléfono").And.Contain("aquí");
    }

    [Theory]
    [InlineData("Balzhur")]
    [InlineData("Callandor")]
    [InlineData("Simauria")]
    [InlineData("Cyberlife")]
    public async Task EmptyAndIrrelevantBlocks_ProduceNothing(string set)
    {
        (await LuaAsync(set, "")).Should().BeEmpty();
        (await LuaAsync(set, "\n\n\n")).Should().BeEmpty();
        (await LuaAsync(set, "Estas en una plaza.\nHay tres salidas: norte, sur y este.\nUn orco llega del norte.")).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Balzhur")]
    [InlineData("Callandor")]
    [InlineData("Simauria")]
    [InlineData("Cyberlife")]
    public async Task ABigBlock_StaysWellInsideTheLimits(string set)
    {
        // 8192 bytes is the most a block can hold: some 250 lines of ordinary text.
        var lines = Enumerable.Range(0, 250).Select(i => i % 7 == 0 ? "" : $"Linea {i} de una descripcion larga con 'comillas' y [corchetes] y \"dobles\".");
        var messages = await LuaAsync(set, string.Join('\n', lines));
        messages.Should().BeEmpty();
    }

    /// <summary>
    /// The engine builds a fresh Lua VM and parses the script for every block (executions are isolated
    /// and MoonSharp offers no way to share a compiled chunk between VMs). Measured on the development
    /// machine, debug build: about 6 ms per block (2 ms the VM, 1.5 ms parsing, the rest the om table,
    /// the guard and two thread-pool hops), i.e. some 1.2 s for 200 blocks. Best of three, because the
    /// other test assemblies run at the same time.
    /// </summary>
    [Theory]
    [InlineData("Balzhur")]
    [InlineData("Callandor")]
    [InlineData("Simauria")]
    [InlineData("Cyberlife")]
    public async Task TwoHundredBlocksOfTenLines_TakeLessThanTwoSeconds(string set)
    {
        var block = string.Join(NL, Enumerable.Range(0, 10).Select(i => i == 4 ? "Ana dice 'hola" : i == 5 ? "que tal'" : $"Linea de relleno {i} con 'comillas' y [corchetes]."));
        await LuaAsync(set, block);

        var best = TimeSpan.MaxValue;
        for (var attempt = 0; attempt < 3 && best >= TimeSpan.FromSeconds(2); attempt++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < 200; i++)
                await LuaAsync(set, block);
            watch.Stop();
            if (watch.Elapsed < best) best = watch.Elapsed;
        }

        output.WriteLine($"{set}: 200 blocks of 10 lines in {best.TotalMilliseconds:F0} ms");
        best.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    // ── Callandor ──────────────────────────────────────────────────────────

    /// <summary>What the original gave for this block, seen as it saw it (lines ended in '\n', then the prompt), minus its index+3 slip.</summary>
    private static string? CallandorExpected(string block)
    {
        var original = OriginalProcessRules.Run(OriginalProcessRules.Callandor, block + "\n> ");
        if (original is null) return null;
        var close = original.IndexOf("'\n", StringComparison.Ordinal);
        if (close >= 0) return original[..(close + 1)];
        return original[..^3].TrimEnd('\n'); // never closed: the rest of the block, without the prompt added above
    }

    /// <summary>The original applied again and again to what follows each message.</summary>
    private static List<string> CallandorExpectedAll(string block)
    {
        var lines = block.Split('\n');
        var expected = new List<string>();
        var position = 0;
        while (position < lines.Length)
        {
            if (lines[position].Length == 0 || CallandorExpected(lines[position]) is null)
            {
                position++;
                continue;
            }
            var message = CallandorExpected(string.Join('\n', lines[position..]))!;
            expected.Add(message);
            position += message.Split('\n').Length;
        }
        return expected;
    }

    public static TheoryData<string> CallandorSingleLines => new()
    {
        // what the player says (prefixes)
        "Solicitas 'ayuda'", "Instruyes a Pepe en el arte de la espada", "Dices 'hola'", "Charlas 'buenas'",
        "Transmites a Ana 'hola'", "<Comunicas> 'aqui'", "Susurras 'psst'", "Gritas 'eh'", "Conspiras 'plan'",
        "Gruñes 'grr'", "Transmites cariñosamente a Ana 'hola'",
        // what others say (word conditions), in the order of the original
        "Ana te transmite 'hola'", "Ana dice al grupo 'vamos'", "Urk gruñe al grupo 'grr'",
        "Ana dice 'hola'", "Ana charla 'x'", "Ana conspira 'x'", "Ana comunica 'x'", "Urk gruñe 'x'",
        "Ana susurra 'x'", "Ana grita cerca de aqui 'x'", "Ana grita 'x'", "Ana dice al equipo 'x'",
        "<Ana> conversa 'x'", "Ana te transmite cariñosamente 'x'", "Ana solicita 'x'", "Trivial: Ana 'x'",
        " dice 'primera palabra vacia'",
    };

    [Theory]
    [MemberData(nameof(CallandorSingleLines))]
    public async Task Callandor_EveryVerb_OneLine(string line)
    {
        CallandorExpected(line).Should().Be(line, "the oracle must agree that this is a message");
        (await LuaAsync("Callandor", line)).Should().Equal(line);
        // and in the middle of other text ("Instruyes ..." has no closing quote: it runs to the end, as in the original)
        var block = $"Un orco llega.{NL}{NL}{line}{NL}El orco se va.";
        var lua = await LuaAsync("Callandor", block);
        lua.Should().Equal(CallandorExpectedAll(block));
        lua.Should().ContainSingle().Which.Should().StartWith(line);
    }

    public static TheoryData<string> CallandorNegatives => new()
    {
        "Ana dice algo sin comillas", "Ana  dice 'doble espacio'", "Ana te dice 'x'", "dice 'x'", "Ana grita cerca de aquí 'con acento'",
        "'Ana dice", "Ana dice al grupo", "Ana dice al grupo sin 'comilla", "Ana te transmite", "Ana te transmite  'x'",
        "<Ana conversa 'x'", "Ana> conversa 'x'", "Trivial: Ana", "Trivial Ana 'x'", "Ana solicita ayuda", "solicitas 'minuscula'",
        " Dices 'con espacio delante'", "Ana gruñe al equipo 'x'", "Ana susurra", "Ana grita cerca de aqui", "Ana dice ''",
    };

    [Theory]
    [MemberData(nameof(CallandorNegatives))]
    public async Task Callandor_LinesThatLookLikeIt_ButAreNot(string line)
    {
        var expected = CallandorExpected(line);
        var lua = await LuaAsync("Callandor", line);
        if (expected is null) lua.Should().BeEmpty();
        else lua.Should().Equal(expected); // "Ana dice ''" is a message for the original too
    }

    [Fact]
    public async Task Callandor_MessageWrappedOverSeveralLines_IsCapturedUpToTheClosingQuote()
    {
        const string block = "Un orco llega.\nAna dice 'hola que tal estas, hace mucho\nque no te veia por aqui, a ver si\nquedamos un dia'\nEl orco te mira.\nAna sonrie.";
        const string message = "Ana dice 'hola que tal estas, hace mucho\nque no te veia por aqui, a ver si\nquedamos un dia'";

        CallandorExpected(block).Should().Be(message);
        (await LuaAsync("Callandor", block)).Should().Equal(message);
    }

    [Fact]
    public async Task Callandor_QuoteInTheMiddleOfALine_DoesNotClose_OnlyAtEndOfLine()
    {
        const string block = "Ana dice 'el 'jefe' ha dicho\nque no' y punto\nse acabo'\nFin.";
        const string message = "Ana dice 'el 'jefe' ha dicho\nque no' y punto\nse acabo'";
        CallandorExpected(block).Should().Be(message);
        (await LuaAsync("Callandor", block)).Should().Equal(message);
    }

    [Fact]
    public async Task Callandor_QuoteNeverClosed_TakesTheRestOfTheBlock()
    {
        const string block = "Dices 'esto no se cierra\nsigue aqui\ny aqui\n\n";
        const string message = "Dices 'esto no se cierra\nsigue aqui\ny aqui";
        CallandorExpected(block).Should().Be(message);
        (await LuaAsync("Callandor", block)).Should().Equal(message);
    }

    [Fact]
    public async Task Callandor_OwnSpeechWithoutQuotes_InstruyesAndTransmitesA_RunToTheNextClosingQuoteOrTheEnd()
    {
        const string block = "Instruyes a Pepe.\nAlgo mas.";
        CallandorExpected(block).Should().Be(block);
        (await LuaAsync("Callandor", block)).Should().Equal(block);
    }

    [Fact]
    public async Task Callandor_Difference_TheOriginalKeptOneCharacterTooMany_AndLostTheMessageAtTheEndOfTheChunk()
    {
        // index + 3: the closing quote, the line break and the first character of whatever follows.
        OriginalProcessRules.Run(OriginalProcessRules.Callandor, "Ana dice 'hola'\n> ").Should().Be("Ana dice 'hola'\n>");
        // Nothing after the line break: Substring threw, the client beeped and there was no message.
        OriginalProcessRules.Run(OriginalProcessRules.Callandor, "Ana dice 'hola'\n").Should().BeNull();

        (await LuaAsync("Callandor", "Ana dice 'hola'")).Should().Equal("Ana dice 'hola'");
    }

    [Fact]
    public async Task Callandor_Difference_SeveralMessagesInOneBlock_AreAllReturned()
    {
        const string block = "Ana dice 'uno'\nUn orco llega.\nBeto grita 'dos\ny medio'\nTransmites a Ana 'tres'";
        CallandorExpected(block).Should().Be("Ana dice 'uno'", "the original stopped at the first");
        (await LuaAsync("Callandor", block)).Should().Equal("Ana dice 'uno'", "Beto grita 'dos\ny medio'", "Transmites a Ana 'tres'");
    }

    // ── Simauria ───────────────────────────────────────────────────────────

    private static string? SimauriaExpected(string block)
        => OriginalProcessRules.Run(OriginalProcessRules.Simauria, block + "\n")?.TrimEnd('\n');

    public static TheoryData<string, string?> SimauriaBlocks => new()
    {
        // channels: previous line empty, not the first line, a ']' from there on
        { "\n[Charla] Ana: hola", "[Charla] Ana: hola" },
        { "Llega Ana.\n\n[Charla] Ana: hola a todos", "[Charla] Ana: hola a todos" },
        { "[Charla] Ana: en la primera linea no", null },
        { "Texto pegado\n[Charla] Ana: la anterior no esta vacia", null },
        { " \n[Charla] Ana: un espacio no es una linea vacia", null },
        { "\n[sin cierre", null },
        { "\n[abre aqui\ny cierra] despues", "[abre aqui\ny cierra] despues" },
        { "\n[Charla] Ana: mensaje largo que\nsigue en otra linea\n\nUn orco llega.", "[Charla] Ana: mensaje largo que\nsigue en otra linea\n\nUn orco llega." },
        { "\n [Charla] con espacio delante", null },
        // X dice: '...'   → cut at the last apostrophe of what is left of the block
        { "Ana dice: 'hola'", "Ana dice: 'hola'" },
        { "Ana dice: 'hola' y sonrie.", "Ana dice: 'hola'" },
        { "Ana dice: 'hola que\ntal estas' y sonrie.\nUn orco llega.", "Ana dice: 'hola que\ntal estas'" },
        { "Ana dice: 'hola'\nVes el cartel 'Posada' aqui.", "Ana dice: 'hola'\nVes el cartel 'Posada'" },
        { "Ana dice: 'sin cerrar\nmas texto", "Ana dice: '" },
        { "Dices: 'hola'", "Dices: 'hola'" },
        { "Dices: 'hola que\ntal' mas cosas", "Dices: 'hola que\ntal'" },
        // X te dice: and Dijiste a X: → everything to the end of the block
        { "Ana te dice: 'hola'\nUn orco llega.", "Ana te dice: 'hola'\nUn orco llega." },
        { "Ana te dice: 'hola que\ntal'\n\n", "Ana te dice: 'hola que\ntal'" },
        { "Dijiste a Ana: hola que tal\nUn orco llega.", "Dijiste a Ana: hola que tal\nUn orco llega." },
        // the first message wins
        { "Un orco llega.\nAna dice: 'uno'\n\n[Charla] Beto: dos", "Ana dice: 'uno'" },
        { "\n[Charla] Beto: dos\nAna dice: 'uno'", "[Charla] Beto: dos\nAna dice: 'uno'" },
        // not messages
        { "Ana dice: hola", null },
        { "Ana dice 'hola'", null },
        { "Dices: hola", null },
        { "Dices:", null },
        { "Dijiste a Ana:", null },
        { "Dijiste a Ana hola", null },
        { "Ana  dice: 'doble espacio'", null },
        { "Ana te dice: hola", null },
        { "dices: 'minuscula'", null },
        // the original threw here (empty third word) and lost the block; the script only skips the line
        { "Dijiste a  x y", null },
    };

    [Theory]
    [MemberData(nameof(SimauriaBlocks))]
    public async Task Simauria_EveryBranch(string block, string? message)
    {
        SimauriaExpected(block).Should().Be(message, "the oracle must agree with the expectation written in the test");
        var lua = await LuaAsync("Simauria", block);
        if (message is null) lua.Should().BeEmpty();
        else lua.Should().Equal(message);
    }

    [Fact]
    public async Task Simauria_Difference_DoubleSpaceAfterDijisteA_DoesNotLoseALaterMessage()
    {
        const string block = "Dijiste a  x y\nAna dice: 'hola'";
        SimauriaExpected(block).Should().BeNull("the original threw at the first line");
        (await LuaAsync("Simauria", block)).Should().Equal("Ana dice: 'hola'");
    }

    // ── Balzhur ────────────────────────────────────────────────────────────

    private static string? BalzhurExpected(string block)
        => OriginalProcessRules.Run(OriginalProcessRules.Balzhur, block + "\n");

    public static TheoryData<string> BalzhurChannels => new()
    {
        "Clan", "Faccion", "Novatos", "Orden", "Concilio", "Gremio", "Raza", "Avatar", "Reino", "Trivial", "GOSOCIAL"
    };

    [Theory]
    [MemberData(nameof(BalzhurChannels))]
    public async Task Balzhur_EveryChannel_InItsThreeForms(string channel)
    {
        foreach (var line in new[] { $"[{channel}: Pepe]: 'hola a todos'", $"[{channel}]: Pepe saluda", $"[{channel}] Pepe: hola", $"   [{channel}]   con   espacios" })
        {
            Strip(BalzhurExpected(line)).Should().Be(line);
            (await LuaAsync("Balzhur", line)).Should().Equal(line);
        }
    }

    public static TheoryData<string> BalzhurTalkLines => new()
    {
        "Charlas 'hola'", "Cuentas a Ana 'hola'", "Dices 'hola'", "Respondes a Ana 'hola'", "Susurras a Ana 'hola'",
        "Ana charla 'hola'", "Ana te responde 'hola'", "Ana te cuenta 'hola'", "Ana te susurra 'hola'",
        "  Ana   charla   'con espacios de mas'", "Ana te    cuenta 'x'", "Cuentas a nadie sin comillas",
    };

    [Theory]
    [MemberData(nameof(BalzhurTalkLines))]
    public async Task Balzhur_EveryVerb(string line)
    {
        BalzhurExpected(line).Should().Be(line);
        (await LuaAsync("Balzhur", line)).Should().Equal(line);
    }

    public static TheoryData<string> BalzhurNegatives => new()
    {
        "[Clan]", "[Otro] Pepe: hola", "[Clan: Pepe] 'sin dos puntos'", "[Clan: Pepe]: sin comilla", "[clan] minuscula", "[Clan]:",
        "Clan] Pepe", " Dices 'con espacio delante'", "Ana dice 'hola'", "Ana charla hola", "Ana te responde", "Ana te dice 'x'",
        "charlas 'minuscula'", "Ana te susurra algo", "Un orco llega.", "   ",
    };

    [Theory]
    [MemberData(nameof(BalzhurNegatives))]
    public async Task Balzhur_LinesThatAreNotMessages(string line)
    {
        BalzhurExpected(line).Should().BeNull();
        (await LuaAsync("Balzhur", line)).Should().BeEmpty();
    }

    [Fact]
    public async Task Balzhur_AccumulatesEveryMessageOfTheBlock_InOneMessage()
    {
        const string block = "Un orco llega.\r\n[Clan: Pepe]: 'a las armas'\r\n\r\nAna charla 'hola'\r\nEl orco te golpea.\r\nDices 'ay'\r\n[Novatos] Beto: ayuda\r\nAna te susurra 'corre'";
        var lua = await LuaAsync("Balzhur", block);

        lua.Should().Equal("[Clan: Pepe]: 'a las armas'\nAna charla 'hola'\nDices 'ay'\n[Novatos] Beto: ayuda\nAna te susurra 'corre'");
        // Same lines, same order; the original only differed in the separators (see the next test).
        Strip(lua[0]).Should().Be(Strip(BalzhurExpected(block)));
    }

    [Fact]
    public async Task Balzhur_Difference_TheOriginalGluedChatLinesWithoutSeparator()
    {
        const string block = "Dices 'uno'\nAna charla 'dos'\n[Clan] Pepe: tres\nDices 'cuatro'";
        BalzhurExpected(block).Should().Be("Dices 'uno'Ana charla 'dos'[Clan] Pepe: tres\r\nDices 'cuatro'");
        (await LuaAsync("Balzhur", block)).Should().Equal("Dices 'uno'\nAna charla 'dos'\n[Clan] Pepe: tres\nDices 'cuatro'");
    }

    [Fact]
    public async Task Balzhur_Difference_ChannelWithNameAndNoThirdWord_DoesNotLoseTheBlock()
    {
        const string block = "Dices 'uno'\n[Clan: Pepe]:\nAna charla 'dos'";
        BalzhurExpected(block).Should().BeNull("the original indexed a third word that is not there and threw");
        (await LuaAsync("Balzhur", block)).Should().Equal("Dices 'uno'\nAna charla 'dos'");
    }

    // ── Cyberlife ──────────────────────────────────────────────────────────

    public static TheoryData<string> CyberlifeLines => new()
    {
        "Dices con acento gallego, \"hola\"", "Murmuras con acento ruso, \"hola\"",
        "Dices: \"hola\"", "Murmuras: \"hola\"",
        "Gritas: \"eh\"",
        "Ana grita: \"eh\"",
        "Ana grita cerca de aquí: \"eh\"",
        "[Chat] Ana: \"hola\"",
        "[Chat:] \"hola\"",
        "Ana dice con acento ruso, \"hola\"", "Ana murmura con acento ruso, \"hola\"",
        "Ana dice: \"hola\"", "Ana murmura: \"hola\"", "Una mujer alta dice: \"hola, \"amigo\"\"",
        "\"Ana chatea: \"hola\"",
        "Transmites a Ana, \"hola\"",
        "Ana te transmite, \"hola\"",
        "** Ana Ha solicitado asistencia con el siguiente motivo: un bug **",
        "*** Ana ha solicitado asistencia con el siguiente motivo: x***",
        "Ana te dice por teléfono, \"hola\"", "Ana dice por teléfono, \"hola\"",
        "Dices por teléfono, \"hola\"",
        "ANA DICE: \"MAYUSCULAS\"", "gritas: \"minusculas\"",
    };

    [Theory]
    [MemberData(nameof(CyberlifeLines))]
    public async Task Cyberlife_EveryExpression(string line)
    {
        OriginalProcessRules.Cyberlife(line).Should().Be(line);
        (await LuaAsync("Cyberlife", line)).Should().Equal(line);
        (await LuaAsync("Cyberlife", $"Entras en el bar.\n{line}\nSuena musica.")).Should().Equal(line);
    }

    public static TheoryData<string> CyberlifeNegatives => new()
    {
        "Ana dice: hola", "Ana dice \"hola\"", "Ana dice: \"hola\" y se va", "* Ana Ha solicitado asistencia con el siguiente motivo: x *",
        "Dices: \"\"", "Ana dice por telefono, \"sin acento\"", "[Chat] \"sin nombre\"", "Transmites a Ana \"sin coma\"", "\"hola\"",
    };

    [Theory]
    [MemberData(nameof(CyberlifeNegatives))]
    public async Task Cyberlife_LinesThatAreNotMessages(string line)
    {
        OriginalProcessRules.Cyberlife(line).Should().BeNull();
        (await LuaAsync("Cyberlife", line)).Should().BeEmpty();
    }

    [Fact]
    public async Task Cyberlife_Difference_EveryMatchingLine_NotOnlyTheFirstExpressionThatMatches()
    {
        const string block = "Ana dice: \"uno\"\nRuido.\nDices: \"dos\"\n[Chat] Beto: \"tres\"";
        // Regex 2 (Dices:) is tried before regex 9 (X dice:), so the original returned the SECOND line and nothing else.
        OriginalProcessRules.Cyberlife(block).Should().Be("Dices: \"dos\"");
        (await LuaAsync("Cyberlife", block)).Should().Equal("Ana dice: \"uno\"", "Dices: \"dos\"", "[Chat] Beto: \"tres\"");
    }

    // ── Fuzz: random blocks made of the lines above ────────────────────────

    private static List<string> RandomBlocks(IEnumerable<string> pool, int count, int seed)
    {
        var lines = pool.ToList();
        var random = new Random(seed);
        var blocks = new List<string>(count);
        for (var b = 0; b < count; b++)
        {
            var size = random.Next(1, 9);
            blocks.Add(string.Join('\n', Enumerable.Range(0, size).Select(_ => lines[random.Next(lines.Count)])));
        }
        return blocks;
    }

    private static readonly string[] Noise =
        ["", "", "Un orco llega del norte.", "sigue el texto aqui", "y termina con comilla'", "[corchete suelto", "cierra] aqui", "Ves el cartel 'Posada'.", "> "];

    private static IEnumerable<string> Rows(IEnumerable<object[]> data) => data.Select(row => (string)row[0]);

    [Fact]
    public async Task Callandor_Fuzz_AgreesWithTheOriginal()
    {
        var pool = Rows(CallandorSingleLines).Concat(Rows(CallandorNegatives)).Concat(Noise)
            .Append("Ana dice 'empieza y no acaba").Append("Dices 'otro que sigue");
        foreach (var block in RandomBlocks(pool, 300, seed: 1))
            (await LuaAsync("Callandor", block)).Should().Equal(CallandorExpectedAll(block), $"block:\n{block}");
    }

    [Fact]
    public async Task Simauria_Fuzz_AgreesWithTheOriginal()
    {
        var pool = Rows(SimauriaBlocks).Where(b => b != "Dijiste a  x y").SelectMany(b => b.Split('\n')).Concat(Noise);
        foreach (var block in RandomBlocks(pool, 300, seed: 2))
        {
            var expected = SimauriaExpected(block);
            var lua = await LuaAsync("Simauria", block);
            if (string.IsNullOrEmpty(expected)) lua.Should().BeEmpty($"block:\n{block}");
            else lua.Should().Equal([expected], $"block:\n{block}");
        }
    }

    [Fact]
    public async Task Balzhur_Fuzz_AgreesWithTheOriginal()
    {
        var channels = Rows(BalzhurChannels).SelectMany(c => new[] { $"[{c}: Pepe]: 'hola'", $"[{c}]: Pepe saluda", $"[{c}] Pepe: hola" });
        var pool = channels.Concat(Rows(BalzhurTalkLines)).Concat(Rows(BalzhurNegatives)).Concat(Noise);
        foreach (var block in RandomBlocks(pool, 300, seed: 3))
        {
            var expected = BalzhurExpected(block);
            var lua = await LuaAsync("Balzhur", block);
            if (expected is null) lua.Should().BeEmpty($"block:\n{block}");
            else
            {
                lua.Should().ContainSingle($"block:\n{block}");
                Strip(lua[0]).Should().Be(Strip(expected), $"block:\n{block}");
            }
        }
    }

    [Fact]
    public async Task Cyberlife_Fuzz_AgreesWithTheOriginal()
    {
        var pool = Rows(CyberlifeLines).Concat(Rows(CyberlifeNegatives)).Concat(Noise);
        foreach (var block in RandomBlocks(pool, 300, seed: 4))
        {
            var lua = await LuaAsync("Cyberlife", block);
            lua.Should().Equal(OriginalProcessRules.CyberlifeAllLines(block), $"block:\n{block}");
            var first = OriginalProcessRules.Cyberlife(block);
            if (first is null) lua.Should().BeEmpty();
            else lua.Should().Contain(first);
        }
    }
}
