using Omnimud.Core.Options;
using Omnimud.Data.Entities;
using Omnimud.Data.Options;
using Omnimud.Data.Repositories;

namespace Omnimud.Data.Tests.Options;

public sealed class OptionsServiceTests : IAsyncLifetime
{
    private readonly InMemoryDatabaseFixture _db = new();
    private SqliteOptionRepository _repo = null!;
    private OptionsService _sut = null!;
    private int _mudId;
    private int _characterId;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        _repo = new SqliteOptionRepository(_db);
        _sut = new OptionsService(_repo);
        _mudId = await new SqliteMudRepository(_db).AddAsync(new MudEntity { Name = "Mud", Host = "h", Port = 1 });
        _characterId = await new SqliteCharacterRepository(_db).AddAsync(new CharacterEntity { MudId = _mudId, Name = "Pj" });
    }

    public Task DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task Resolve_NothingStored_ReturnsDefaults()
    {
        (await _sut.ResolveAsync(_mudId, _characterId)).Should().Be(new OmnimudOptions());
        (await _sut.ResolveAsync(null, null)).Should().Be(OmnimudOptions.Default);
        (await _sut.HasOwnOptionsAsync(OptionScope.Global, null)).Should().BeFalse();
    }

    [Fact]
    public async Task Resolve_InheritsByBlock_ThroughTheThreeLevels()
    {
        var global = new OmnimudOptions { Volume = 10, HistorySize = 11, FontFamily = "Global" };
        var mud = new OmnimudOptions { Volume = 20 };
        var character = new OmnimudOptions { Volume = 30, UseConcatChar = true };

        await _sut.SaveAsync(OptionScope.Global, null, global);
        (await _sut.ResolveAsync(_mudId, _characterId)).Should().Be(global, "nobody below has own options");

        await _sut.SaveAsync(OptionScope.Mud, _mudId, mud);
        (await _sut.ResolveAsync(_mudId, _characterId)).Should().Be(mud, "the character inherits the MUD's whole block");
        (await _sut.ResolveAsync(_mudId, _characterId)).HistorySize.Should().Be(50, "by block: the MUD's set does not fall through to global per option");
        (await _sut.ResolveAsync(_mudId, null)).Should().Be(mud);
        (await _sut.ResolveAsync(null, null)).Should().Be(global);

        await _sut.SaveAsync(OptionScope.Character, _characterId, character);
        (await _sut.ResolveAsync(_mudId, _characterId)).Should().Be(character);
        (await _sut.ResolveAsync(_mudId, null)).Should().Be(mud);

        (await _sut.HasOwnOptionsAsync(OptionScope.Global, null)).Should().BeTrue();
        (await _sut.HasOwnOptionsAsync(OptionScope.Mud, _mudId)).Should().BeTrue();
        (await _sut.HasOwnOptionsAsync(OptionScope.Character, _characterId)).Should().BeTrue();
        (await _sut.HasOwnOptionsAsync(OptionScope.Mud, _mudId + 100)).Should().BeFalse();
    }

    [Fact]
    public async Task Resolve_OtherMudsAndCharacters_AreNotAffected()
    {
        var otherMud = await new SqliteMudRepository(_db).AddAsync(new MudEntity { Name = "Otro", Host = "h", Port = 1 });
        await _sut.SaveAsync(OptionScope.Mud, _mudId, new OmnimudOptions { Volume = 20 });

        (await _sut.ResolveAsync(otherMud, null)).Volume.Should().Be(100);
    }

    [Fact]
    public async Task Save_StoresACompleteBlock_WithEveryKey()
    {
        await _sut.SaveAsync(OptionScope.Character, _characterId, new OmnimudOptions { ConcatChar = '|' });

        var rows = await _repo.GetByScope((int)OptionScope.Character, _characterId);
        rows.Select(r => r.Key).Should().BeEquivalentTo(OptionsSerializer.Keys);
        rows.Single(r => r.Key == nameof(OmnimudOptions.ConcatChar)).Value.Should().Be("|");
    }

    [Fact]
    public async Task ResetToInherited_RemovesTheBlock_SoTheScopeInheritsAgain()
    {
        await _sut.SaveAsync(OptionScope.Mud, _mudId, new OmnimudOptions { Volume = 20 });
        await _sut.SaveAsync(OptionScope.Character, _characterId, new OmnimudOptions { Volume = 30 });

        await _sut.ResetToInheritedAsync(OptionScope.Character, _characterId);

        (await _sut.HasOwnOptionsAsync(OptionScope.Character, _characterId)).Should().BeFalse();
        (await _sut.ResolveAsync(_mudId, _characterId)).Volume.Should().Be(20);
        (await _sut.GetOwnAsync(OptionScope.Character, _characterId)).Should().BeNull();
        (await _sut.GetOwnAsync(OptionScope.Mud, _mudId))!.Volume.Should().Be(20);
    }

    [Fact]
    public async Task ResetToInherited_Global_GoesBackToDefaults()
    {
        await _sut.SaveAsync(OptionScope.Global, null, new OmnimudOptions { Volume = 5 });

        await _sut.ResetToInheritedAsync(OptionScope.Global, null);

        (await _sut.ResolveAsync(null, null)).Should().Be(new OmnimudOptions());
    }

    [Fact]
    public async Task Changed_IsRaisedAfterSaveAndReset_WithTheScope()
    {
        var events = new List<(OptionScope, int?)>();
        _sut.Changed += (sender, e) =>
        {
            sender.Should().BeSameAs(_sut);
            events.Add((e.Scope, e.ScopeId));
        };

        await _sut.SaveAsync(OptionScope.Global, 123, new OmnimudOptions());
        await _sut.SaveAsync(OptionScope.Mud, _mudId, new OmnimudOptions());
        await _sut.ResetToInheritedAsync(OptionScope.Mud, _mudId);
        await _sut.SaveAsync(OptionScope.Character, _characterId, new OmnimudOptions());

        events.Should().Equal(
            (OptionScope.Global, null),
            (OptionScope.Mud, _mudId),
            (OptionScope.Mud, _mudId),
            (OptionScope.Character, _characterId));
    }

    [Fact]
    public async Task Changed_IsRaisedAfterTheDataIsStored()
    {
        OmnimudOptions? seenByHandler = null;
        _sut.Changed += (_, _) => seenByHandler = _sut.ResolveAsync(null, null).GetAwaiter().GetResult();

        await _sut.SaveAsync(OptionScope.Global, null, new OmnimudOptions { Volume = 42 });

        seenByHandler!.Volume.Should().Be(42);
    }

    [Fact]
    public async Task MudAndCharacterScopes_NeedAnId()
    {
        await _sut.Invoking(s => s.SaveAsync(OptionScope.Mud, null, new OmnimudOptions())).Should().ThrowAsync<ArgumentException>();
        await _sut.Invoking(s => s.HasOwnOptionsAsync(OptionScope.Character, null)).Should().ThrowAsync<ArgumentException>();
        await _sut.Invoking(s => s.ResetToInheritedAsync(OptionScope.Character, null)).Should().ThrowAsync<ArgumentException>();
        await _sut.Invoking(s => s.SaveAsync(OptionScope.Global, null, null!)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task LegacyGlobalKeys_WrittenByTheOldOptionsDialog_AreRead()
    {
        // Exactly what FrmOptions.BtnOk_Click writes today.
        await _repo.SetValueAsync(0, null, "Language", "es");
        await _repo.SetValueAsync(0, null, "ScreenReader", "NVDA");
        await _repo.SetValueAsync(0, null, "MaxLines", "5000");
        await _repo.SetValueAsync(0, null, "FontFamily", "Courier New");
        await _repo.SetValueAsync(0, null, "FontSize", "14");
        await _repo.SetValueAsync(0, null, "SoundEnabled", "false");
        await _repo.SetValueAsync(0, null, "Volume", "35");
        await _repo.SetValueAsync(0, null, "AnnounceMessages", "false");
        await _repo.SetValueAsync(0, null, "AnnounceMudText", "false");
        await _repo.SetValueAsync(0, null, "FlashOnMessage", "false");

        var options = await _sut.ResolveAsync(_mudId, _characterId);

        options.Should().Be(new OmnimudOptions
        {
            Language = "es",
            ScreenReader = ScreenReaderMode.Nvda,
            MaxLines = 5000,
            FontFamily = "Courier New",
            FontSize = 14f,
            EnableSounds = false,
            Volume = 35,
            AnnounceMessages = false,
            AnnounceMudText = false,
            FlashWindow = false
        });
        (await _sut.HasOwnOptionsAsync(OptionScope.Global, null)).Should().BeTrue();
    }

    [Fact]
    public async Task LegacyGlobalKeys_AreFoldedIntoTheNewOnesOnSave()
    {
        await _repo.SetValueAsync(0, null, "SoundEnabled", "false");
        await _repo.SetValueAsync(0, null, "FlashOnMessage", "false");

        var loaded = await _sut.ResolveAsync(null, null);
        await _sut.SaveAsync(OptionScope.Global, null, loaded with { Volume = 1 });

        var keys = (await _repo.GetByScope(0, null)).Select(r => r.Key).ToList();
        keys.Should().NotContain(["SoundEnabled", "FlashOnMessage"]);
        var reloaded = await _sut.ResolveAsync(null, null);
        reloaded.EnableSounds.Should().BeFalse();
        reloaded.FlashWindow.Should().BeFalse();
        reloaded.Volume.Should().Be(1);
    }

    [Fact]
    public async Task CorruptRows_DoNotBreakResolution()
    {
        await _repo.SetValueAsync(0, null, "Volume", "muy alto");
        await _repo.SetValueAsync(0, null, "LogType", "Hourly");
        await _repo.SetValueAsync(0, null, "NoExisteEstaOpcion", "x");
        await _repo.SetValueAsync(0, null, "HistorySize", "25");

        var options = await _sut.ResolveAsync(null, null);

        options.Should().Be(new OmnimudOptions { HistorySize = 25 });
    }
}
