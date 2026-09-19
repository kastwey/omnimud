using System.Text.RegularExpressions;
using Omnimud.Core.Session;
using Omnimud.Data.Entities;
using Omnimud.Data.Repositories;
using Omnimud.Data.Seed;
using Omnimud.Data.Session;

namespace Omnimud.Data.Tests.Seed;

/// <summary>
/// The four seeded rule sets, read back from a migrated database through SessionStore and evaluated
/// the way the contract describes: per line, first rule that matches, template expanded on the match.
/// The examples come from the conditions of trunk\ProcessRules\*.cs.
/// </summary>
public sealed class BuiltInMessageRulesTests : IAsyncLifetime
{
    private readonly InMemoryDatabaseFixture _db = new();
    private readonly Dictionary<string, IReadOnlyList<MessageRule>> _rules = [];

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        var muds = new SqliteMudRepository(_db);
        var ruleRepo = new SqliteMessageRuleRepository(_db);
        var store = new SessionStore(muds, new SqliteCharacterRepository(_db), new SqliteAliasRepository(_db),
            new SqliteTriggerRepository(_db), new SqlitePathRepository(_db), new SqliteDirectionRepository(_db),
            new SqliteMovementRepository(_db), ruleRepo);

        foreach (var set in await ruleRepo.GetRuleSetsAsync())
        {
            var mudId = await muds.AddAsync(new MudEntity { Name = set.Name, Host = "h", Port = 1, MessageRuleSetId = set.Id });
            _rules[set.Name] = await store.GetMessageRulesAsync(mudId);
        }
    }

    public Task DisposeAsync() => _db.DisposeAsync();

    private (string Text, string? Channel)? Extract(string ruleSet, string line)
    {
        foreach (var rule in _rules[ruleSet])
        {
            var match = Regex.Match(line, rule.Pattern, rule.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            if (match.Success) return (match.Result(rule.Template), rule.Channel);
        }
        return null;
    }

    [Fact]
    public void Seed_HasTheFourSetsOfTheOriginal_AndEveryPatternCompiles()
    {
        _rules.Keys.Should().BeEquivalentTo("Balzhur", "Callandor", "Simauria", "Cyberlife");
        BuiltInMessageRuleSets.All.Select(s => s.Name).Should().BeEquivalentTo(_rules.Keys);
        foreach (var set in BuiltInMessageRuleSets.All)
        {
            _rules[set.Name].Select(r => r.Pattern).Should().Equal(set.Rules.Select(r => r.Pattern), "the order of the seed is the evaluation order");
            foreach (var rule in set.Rules)
                FluentActions.Invoking(() => new Regex(rule.Pattern)).Should().NotThrow();
        }
    }

    // ── Balzhur ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("[Clan: Pepe]: 'hola a todos'", "Clan")]
    [InlineData("[Faccion: Ana]: 'reunión en la plaza'", "Faccion")]
    [InlineData("[Novatos]: Pepe pregunta dónde está el banco", "Novatos")]
    [InlineData("[Gremio] Ana ha entrado en el juego", "Gremio")]
    [InlineData("[GOSOCIAL] Pepe sonríe", "GOSOCIAL")]
    [InlineData("[Trivial]: ¿Capital de Francia?", "Trivial")]
    [InlineData("  [Reino]:   Juan   avisa   de   algo", "Reino")]
    [InlineData("Charlas 'hola'", null)]
    [InlineData("Cuentas a Pepe 'hola'", null)]
    [InlineData("Dices 'hola'", null)]
    [InlineData("Respondes a Pepe 'vale'", null)]
    [InlineData("Susurras a Pepe 'psst'", null)]
    [InlineData("Pepe charla 'hola'", null)]
    [InlineData("Pepe te cuenta 'hola'", null)]
    [InlineData("Pepe te responde 'sí'", null)]
    [InlineData("Pepe  te   susurra  'eh'", null)]
    public void Balzhur_Messages(string line, string? channel)
    {
        Extract("Balzhur", line).Should().Be((line, channel));
    }

    [Theory]
    [InlineData("[Clan]")]                        // a second word is required
    [InlineData("[Clan: Pepe]:")]                 // the original threw here; no message either way
    [InlineData("[Clan: Pepe] 'hola'")]           // second word must end in "]:"
    [InlineData("[Clan: Pepe]: hola")]            // third word must start with an apostrophe
    [InlineData("[Desconocido]: hola")]
    [InlineData("[clan]: hola")]                  // ordinal comparison
    [InlineData("[Clanes]: hola")]
    [InlineData("Pepe dice 'hola'")]              // "dice" is not a Balzhur verb
    [InlineData("Pepe te mira fijamente")]
    [InlineData("Pepe charla con Ana")]
    [InlineData("Un orco te cuenta 'x'")]         // the verb must be the second/third word
    [InlineData("charlas 'hola'")]
    [InlineData(" Dices 'hola'")]                 // StartsWith on the raw line
    [InlineData("Dices hola")]
    [InlineData("")]
    public void Balzhur_NotMessages(string line)
    {
        Extract("Balzhur", line).Should().BeNull();
    }

    // ── Callandor ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Solicitas 'ayuda'")]
    [InlineData("Instruyes a Pepe en el arte de la espada")]
    [InlineData("Dices 'hola'")]
    [InlineData("Charlas 'hola'")]
    [InlineData("Transmites a Pepe 'hola'")]
    [InlineData("<Comunicas> 'hola'")]
    [InlineData("Susurras 'hola'")]
    [InlineData("Gritas 'hola'")]
    [InlineData("Conspiras 'hola'")]
    [InlineData("Gruñes 'grr'")]
    [InlineData("Transmites cariñosamente a Ana 'hola'")]
    [InlineData("Pepe te transmite 'hola'")]
    [InlineData("Pepe te transmite cariñosamente 'hola'")]
    [InlineData("Pepe dice al grupo 'vamos'")]
    [InlineData("Pepe gruñe al grupo 'grr'")]
    [InlineData("Pepe dice al equipo 'ya'")]
    [InlineData("Pepe dice 'hola'")]
    [InlineData("Pepe charla 'hola'")]
    [InlineData("Pepe conspira 'hola'")]
    [InlineData("Pepe comunica 'hola'")]
    [InlineData("Pepe gruñe 'hola'")]
    [InlineData("Pepe susurra 'hola'")]
    [InlineData("Pepe grita cerca de aqui 'socorro'")]
    [InlineData("Pepe grita 'eh'")]
    [InlineData("<Novato> conversa 'hola'")]
    [InlineData("Pepe solicita 'ayuda'")]
    [InlineData("Trivial: Pepe 'París'")]
    [InlineData("Pepe dice 'un mensaje largo que sigue en la")] // wrapped: first line only (see seed remarks)
    public void Callandor_Messages(string line)
    {
        Extract("Callandor", line)!.Value.Text.Should().Be(line);
    }

    [Theory]
    [InlineData("Pepe grita cerca de aquí 'socorro'")] // the original compares "aqui" without accent
    [InlineData("Pepe dice  'hola'")]                  // Split(' ') without removing empties: two spaces break it
    [InlineData("Un orco dice 'hola'")]
    [InlineData("Pepe dice hola")]
    [InlineData("Pepe dice al grupo hola")]
    [InlineData("Pepe te mira")]
    [InlineData("dices 'hola'")]
    [InlineData("Novato conversa 'hola'")]
    [InlineData("<Novato conversa 'hola'")]
    [InlineData("Trivial: 'París'")]
    [InlineData("Comunicas 'hola'")]
    [InlineData("Pepe llega desde el norte.")]
    public void Callandor_NotMessages(string line)
    {
        Extract("Callandor", line).Should().BeNull();
    }

    // ── Simauria ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("[Charla] Pepe: hola", "[Charla] Pepe: hola")]
    [InlineData("[Gremio:Pepe] hola a todos", "[Gremio:Pepe] hola a todos")]
    [InlineData("Pepe dice: 'hola'", "Pepe dice: 'hola'")]
    [InlineData("Pepe dice: 'hola' y se marcha.", "Pepe dice: 'hola'")]          // cut after the last apostrophe
    [InlineData("Pepe dice: 'no digas 'eso' aquí'.", "Pepe dice: 'no digas 'eso' aquí'")]
    [InlineData("Pepe dice: 'un mensaje largo que sigue", "Pepe dice: 'un mensaje largo que sigue")]
    [InlineData("Dices: 'hola'.", "Dices: 'hola'")]
    [InlineData("Pepe te dice: 'hola' en voz baja", "Pepe te dice: 'hola' en voz baja")] // tells are kept whole
    [InlineData("Dijiste a Pepe: hola", "Dijiste a Pepe: hola")]
    public void Simauria_Messages(string line, string expected)
    {
        Extract("Simauria", line)!.Value.Text.Should().Be(expected);
    }

    [Theory]
    [InlineData("Pepe dice 'hola'")]
    [InlineData("Pepe dice: hola")]
    [InlineData("Un orco dice: 'hola'")]
    [InlineData("dices: 'hola'")]
    [InlineData("Dices: hola")]
    [InlineData("Dijiste a Pepe hola")]
    [InlineData("Dijiste a Pepe:")]
    [InlineData("Salidas: [norte, sur]")]
    [InlineData("[sin cerrar")]
    [InlineData("Pepe llega.")]
    public void Simauria_NotMessages(string line)
    {
        Extract("Simauria", line).Should().BeNull();
    }

    // ── Cyberlife ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Dices con acento gallego, \"hola\"")]
    [InlineData("Murmuras con acento ruso, \"hola\"")]
    [InlineData("Murmuras: \"hola\"")]
    [InlineData("Dices: \"hola\"")]
    [InlineData("DICES: \"HOLA\"")]                       // IgnoreCase, as in the original
    [InlineData("gritas: \"eh\"")]
    [InlineData("Pepe grita: \"eh\"")]
    [InlineData("Pepe grita cerca de aquí: \"eh\"")]
    [InlineData("[Novato] Pepe: \"hola\"")]
    [InlineData("[Info:] \"reinicio en cinco minutos\"")]
    [InlineData("Pepe Dice con acento raro, \"hola\"")]
    [InlineData("El guardia de la puerta dice: \"alto\"")]
    [InlineData("\"Pepe chatea: \"hola\"")]
    [InlineData("Transmites a Pepe, \"hola\"")]
    [InlineData("Pepe te transmite, \"hola\"")]
    [InlineData("*** Pepe Ha solicitado asistencia con el siguiente motivo: me he quedado atascado ***")]
    [InlineData("Pepe te dice por teléfono, \"hola\"")]
    [InlineData("Pepe dice por teléfono, \"hola\"")]
    [InlineData("dices por teléfono, \"hola\"")]
    public void Cyberlife_Messages(string line)
    {
        Extract("Cyberlife", line)!.Value.Text.Should().Be(line);
    }

    [Theory]
    [InlineData("Pepe dice: hola")]
    [InlineData("Dices: \"hola\" y te vas")]
    [InlineData("Dices \"hola\"")]
    [InlineData("[Novato] Pepe: hola")]
    [InlineData("* Pepe Ha solicitado asistencia con el siguiente motivo: bug *")]
    [InlineData("Pepe te mira.")]
    [InlineData("Pepe grita con fuerza")]
    [InlineData("Transmites a Pepe \"hola\"")]
    public void Cyberlife_NotMessages(string line)
    {
        Extract("Cyberlife", line).Should().BeNull();
    }
}
