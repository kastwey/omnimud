using Omnimud.Core.Session;
using Omnimud.Data.Entities;
using Omnimud.Data.Repositories;
using Omnimud.Data.Seed;
using Omnimud.Data.Session;

namespace Omnimud.Data.Tests.Repositories;

/// <summary>MessageRuleSets.Script through the repository and through the session store.</summary>
public sealed class MessageRuleScriptTests : IAsyncLifetime
{
    private const string Script = "-- reglas\nfor _, line in ipairs(om.lines) do\n  om.message('ñ: ' .. line)\nend\n";

    private readonly InMemoryDatabaseFixture _db = new();
    private SqliteMessageRuleRepository _rules = null!;
    private SqliteMudRepository _muds = null!;
    private ISessionStore _store = null!;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        _rules = new SqliteMessageRuleRepository(_db);
        _muds = new SqliteMudRepository(_db);
        _store = new SessionStore(_muds, new SqliteCharacterRepository(_db), new SqliteAliasRepository(_db),
            new SqliteTriggerRepository(_db), new SqlitePathRepository(_db), new SqliteDirectionRepository(_db),
            new SqliteMovementRepository(_db), _rules);
    }

    public Task DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task Script_RoundTrips_ByIdByNameAndInTheList()
    {
        var id = await _rules.AddRuleSetAsync(new MessageRuleSetEntity { Name = "Con script", Script = Script });

        (await _rules.GetRuleSetByIdAsync(id))!.Script.Should().Be(Script);
        (await _rules.GetRuleSetByNameAsync("con SCRIPT"))!.Script.Should().Be(Script);
        var listed = (await _rules.GetRuleSetsAsync()).Single(s => s.Id == id);
        listed.Script.Should().Be(Script);
        listed.IsScript.Should().BeTrue();
        listed.IsBuiltIn.Should().BeFalse();
    }

    [Fact]
    public async Task ASetWithoutScript_IsOfTypePatterns_AndABlankScriptIsStoredAsNull()
    {
        var plain = await _rules.AddRuleSetAsync(new MessageRuleSetEntity { Name = "Patrones" });
        var blank = await _rules.AddRuleSetAsync(new MessageRuleSetEntity { Name = "En blanco", Script = "  \r\n\t" });

        (await _rules.GetRuleSetByIdAsync(plain))!.Should().BeEquivalentTo(new { Script = (string?)null, IsScript = false });
        (await _rules.GetRuleSetByIdAsync(blank))!.Should().BeEquivalentTo(new { Script = (string?)null, IsScript = false });
    }

    [Fact]
    public async Task Update_SetsChangesAndClearsTheScript_WithoutTouchingTheRules()
    {
        var id = await _rules.AddRuleSetAsync(new MessageRuleSetEntity { Name = "Mio" });
        await _rules.AddRuleAsync(new MessageRuleEntity { RuleSetId = id, Pattern = "te dice" });

        await _rules.UpdateRuleSetAsync(new MessageRuleSetEntity { Id = id, Name = "Mio", Script = Script });
        (await _rules.GetRuleSetByIdAsync(id))!.Script.Should().Be(Script);

        await _rules.UpdateRuleSetAsync(new MessageRuleSetEntity { Id = id, Name = "Mio 2", Script = "om.message('x')" });
        (await _rules.GetRuleSetByIdAsync(id))!.Should().BeEquivalentTo(new { Name = "Mio 2", Script = "om.message('x')" });

        await _rules.UpdateRuleSetAsync(new MessageRuleSetEntity { Id = id, Name = "Mio 2", Script = null });
        (await _rules.GetRuleSetByIdAsync(id))!.IsScript.Should().BeFalse();
        (await _rules.GetRulesAsync(id)).Should().ContainSingle().Which.Pattern.Should().Be("te dice");
    }

    [Fact]
    public async Task Duplicate_CopiesTheScriptAndTheRules_AndIsNeverBuiltIn()
    {
        var callandor = (await _rules.GetRuleSetByNameAsync(BuiltInMessageRuleSets.Callandor))!;
        callandor.IsBuiltIn.Should().BeTrue();
        callandor.Script.Should().Be(BuiltInMessageRuleSets.ScriptOf(BuiltInMessageRuleSets.Callandor));

        var copyId = await _rules.DuplicateRuleSetAsync(callandor.Id, "Mi Callandor");

        var copy = (await _rules.GetRuleSetByIdAsync(copyId))!;
        copy.Should().BeEquivalentTo(new { Name = "Mi Callandor", IsBuiltIn = false, callandor.Script });
        (await _rules.GetRulesAsync(copyId)).Select(r => r.Pattern).Should().Equal((await _rules.GetRulesAsync(callandor.Id)).Select(r => r.Pattern));
    }

    [Fact]
    public async Task EveryBuiltInSet_IsAScript_InAFreshDatabase()
    {
        var builtIn = (await _rules.GetRuleSetsAsync()).Where(s => s.IsBuiltIn).ToList();
        builtIn.Select(s => s.Name).Should().BeEquivalentTo("Balzhur", "Callandor", "Simauria", "Cyberlife");
        builtIn.Should().OnlyContain(s => s.IsScript && s.Script == BuiltInMessageRuleSets.ScriptOf(s.Name));
    }

    [Fact]
    public async Task SessionStore_GivesTheScriptOfTheMudsSet_OrNull()
    {
        var scriptSet = await _rules.AddRuleSetAsync(new MessageRuleSetEntity { Name = "Con script", Script = Script });
        var patternSet = await _rules.AddRuleSetAsync(new MessageRuleSetEntity { Name = "Patrones" });
        await _rules.AddRuleAsync(new MessageRuleEntity { RuleSetId = patternSet, Pattern = "te dice" });

        var withScript = await _muds.AddAsync(new MudEntity { Name = "A", Host = "h", Port = 1, MessageRuleSetId = scriptSet });
        var withPatterns = await _muds.AddAsync(new MudEntity { Name = "B", Host = "h", Port = 1, MessageRuleSetId = patternSet });
        var withNothing = await _muds.AddAsync(new MudEntity { Name = "C", Host = "h", Port = 1 });

        (await _store.GetMessageRuleScriptAsync(withScript)).Should().Be(Script);
        (await _store.GetMessageRuleScriptAsync(withPatterns)).Should().BeNull();
        (await _store.GetMessageRuleScriptAsync(withNothing)).Should().BeNull();
        (await _store.GetMessageRuleScriptAsync(9999)).Should().BeNull();

        (await _store.GetMessageRulesAsync(withPatterns)).Should().ContainSingle();
    }

    [Fact]
    public async Task SessionStore_ABuiltInMud_GetsItsLuaScript()
    {
        var simauria = (await _rules.GetRuleSetByNameAsync("simauria"))!;
        var mudId = await _muds.AddAsync(new MudEntity { Name = "Simauria", Host = "h", Port = 1, MessageRuleSetId = simauria.Id });

        (await _store.GetMessageRuleScriptAsync(mudId)).Should().Be(BuiltInMessageRuleSets.ScriptOf("Simauria"));
    }
}
