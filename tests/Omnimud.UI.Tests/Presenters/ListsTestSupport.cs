using Microsoft.Data.Sqlite;
using Omnimud.Core.Options;
using Omnimud.Core.Session;
using Omnimud.Data;
using Omnimud.Data.Entities;
using Omnimud.Data.Exchange;
using Omnimud.Data.Migrations;
using Omnimud.Data.Repositories;
using Omnimud.UI.Presenters;
using Omnimud.UI.Services;
using Omnimud.UI.Services.Accessibility;

namespace Omnimud.UI.Tests.Presenters;

/// <summary>Records every system dialog and answers from a script (block T: aliases, triggers, paths).</summary>
internal sealed class RecordingPrompts : IUserPrompts
{
    public List<string> Confirms { get; } = [];
    public List<string> Infos { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> Errors { get; } = [];
    public Queue<bool> ConfirmAnswers { get; } = new();
    public bool DefaultConfirm { get; set; } = true;
    public string? OpenFile { get; set; }
    public string? SaveFile { get; set; }

    public IEnumerable<string> Messages => Infos.Concat(Warnings).Concat(Errors);

    public bool Confirm(string message, string? title = null)
    {
        Confirms.Add(message);
        return ConfirmAnswers.Count > 0 ? ConfirmAnswers.Dequeue() : DefaultConfirm;
    }

    public void Info(string message, string? title = null) => Infos.Add(message);
    public void Warn(string message, string? title = null) => Warnings.Add(message);
    public void Error(string message, string? title = null) => Errors.Add(message);
    public string? PickOpenFile(string title, string filter) => OpenFile;
    public string? PickSaveFile(string title, string filter, string? defaultFileName = null) => SaveFile;
    public string? PickFolder(string title, string? initialPath = null) => null;
}

internal sealed class RecordingAnnouncer : IAnnouncer
{
    public List<string> Spoken { get; } = [];
    public ScreenReaderMode Mode { get; set; }
    public bool Muted { get; set; }

    public bool Announce(string text, AnnouncePriority priority)
    {
        Spoken.Add(text);
        return true;
    }

    public void StopSpeech() { }
}

/// <summary>Conflict dialog stand-in: one answer for every conflict, or cancel.</summary>
internal sealed class FixedConflictResolver(ImportDecision? decision) : IImportConflictResolver
{
    public int Calls { get; private set; }
    public IReadOnlyList<ImportItem> LastConflicts { get; private set; } = [];

    public IReadOnlyDictionary<string, ImportDecision>? Resolve(IReadOnlyList<ImportItem> items)
    {
        Calls++;
        LastConflicts = items.Where(i => i.Status == ImportItemStatus.Conflict).ToList();
        return decision is { } d ? LastConflicts.ToDictionary(i => i.Key, _ => d) : null;
    }
}

internal sealed class MemoryAliasRepository : IAliasRepository
{
    private readonly List<AliasEntity> _items = [];
    private int _nextId = 1;

    public MemoryAliasRepository(params (string Command, string Action)[] seed)
    {
        foreach (var (command, action) in seed)
            _items.Add(new AliasEntity { Id = _nextId++, CharacterId = 1, Command = command, Action = action });
    }

    public Task<IReadOnlyList<AliasEntity>> GetByCharacterAsync(int characterId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AliasEntity>>(_items.Where(a => a.CharacterId == characterId).Select(Clone).ToList());

    public Task<int> AddAsync(AliasEntity alias, CancellationToken ct = default)
    {
        if (_items.Any(a => a.CharacterId == alias.CharacterId && a.Command == alias.Command)) throw new DuplicateEntityException("Alias", alias.Command);
        var copy = Clone(alias);
        copy.Id = _nextId++;
        _items.Add(copy);
        return Task.FromResult(copy.Id);
    }

    public Task UpdateAsync(AliasEntity alias, CancellationToken ct = default)
    {
        if (_items.Any(a => a.Id != alias.Id && a.CharacterId == alias.CharacterId && a.Command == alias.Command)) throw new DuplicateEntityException("Alias", alias.Command);
        _items[_items.FindIndex(a => a.Id == alias.Id)] = Clone(alias);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int id, CancellationToken ct = default)
    {
        _items.RemoveAll(a => a.Id == id);
        return Task.CompletedTask;
    }

    public Task DeleteByCommandAsync(int characterId, string command, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> RemoveByCommandAsync(int characterId, string command, CancellationToken ct = default) => throw new NotSupportedException();

    private static AliasEntity Clone(AliasEntity a) => new() { Id = a.Id, CharacterId = a.CharacterId, Command = a.Command, Action = a.Action, Enabled = a.Enabled };
}

internal sealed class MemoryTriggerRepository : ITriggerRepository
{
    private readonly List<TriggerEntity> _items = [];

    public MemoryTriggerRepository(params string[] names)
    {
        foreach (var name in names)
            _items.Add(new TriggerEntity { Id = "id-" + name, CharacterId = 1, Name = name, Pattern = "pattern " + name, Action = "do " + name, SortOrder = _items.Count + 1 });
    }

    public List<TriggerEntity> Items => _items;

    public Task<IReadOnlyList<TriggerEntity>> GetByCharacterAsync(int characterId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TriggerEntity>>(_items.Where(t => t.CharacterId == characterId).OrderBy(t => t.SortOrder).Select(Clone).ToList());

    public Task<TriggerEntity?> GetByIdAsync(string id, CancellationToken ct = default) => Task.FromResult(_items.Find(t => t.Id == id));

    public Task AddAsync(TriggerEntity trigger, CancellationToken ct = default)
    {
        if (_items.Any(t => t.Id == trigger.Id)) throw new DuplicateEntityException("Trigger", trigger.Id);
        var copy = Clone(trigger);
        if (copy.SortOrder <= 0) copy.SortOrder = trigger.SortOrder = _items.Count == 0 ? 1 : _items.Max(t => t.SortOrder) + 1;
        _items.Add(copy);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(TriggerEntity trigger, CancellationToken ct = default)
    {
        _items[_items.FindIndex(t => t.Id == trigger.Id)] = Clone(trigger);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken ct = default)
    {
        _items.RemoveAll(t => t.Id == id);
        return Task.CompletedTask;
    }

    public Task SetEnabledAsync(string id, bool enabled, CancellationToken ct = default)
    {
        _items.Find(t => t.Id == id)!.Enabled = enabled;
        return Task.CompletedTask;
    }

    public Task ReorderAsync(int characterId, IReadOnlyList<string> triggerIdsInOrder, CancellationToken ct = default)
    {
        for (var i = 0; i < triggerIdsInOrder.Count; i++)
            _items.Find(t => t.Id == triggerIdsInOrder[i])!.SortOrder = i + 1;
        return Task.CompletedTask;
    }

    private static TriggerEntity Clone(TriggerEntity t) => new()
    {
        Id = t.Id, CharacterId = t.CharacterId, Name = t.Name, Pattern = t.Pattern, PatternType = t.PatternType, Action = t.Action,
        ActionType = t.ActionType, Sound = t.Sound, Enabled = t.Enabled, CaseSensitive = t.CaseSensitive, Priority = t.Priority,
        Multiline = t.Multiline, GagLine = t.GagLine, SortOrder = t.SortOrder, CreatedAt = t.CreatedAt, UpdatedAt = t.UpdatedAt,
    };
}

internal sealed class MemoryPathRepository : IPathRepository
{
    private readonly List<PathEntity> _items = [];
    private int _nextId = 1;

    public MemoryPathRepository(params (string Name, string Path)[] seed)
    {
        foreach (var (name, path) in seed)
            _items.Add(new PathEntity { Id = _nextId++, CharacterId = 1, Name = name, Path = path });
    }

    public Task<IReadOnlyList<PathEntity>> GetByCharacterAsync(int characterId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PathEntity>>(_items.Where(p => p.CharacterId == characterId).Select(Clone).ToList());

    public Task<int> AddAsync(PathEntity path, CancellationToken ct = default)
    {
        if (_items.Any(p => p.CharacterId == path.CharacterId && p.Name == path.Name)) throw new DuplicateEntityException("Path", path.Name);
        var copy = Clone(path);
        copy.Id = _nextId++;
        _items.Add(copy);
        return Task.FromResult(copy.Id);
    }

    public Task UpdateAsync(PathEntity path, CancellationToken ct = default)
    {
        if (_items.Any(p => p.Id != path.Id && p.CharacterId == path.CharacterId && p.Name == path.Name)) throw new DuplicateEntityException("Path", path.Name);
        _items[_items.FindIndex(p => p.Id == path.Id)] = Clone(path);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int id, CancellationToken ct = default)
    {
        _items.RemoveAll(p => p.Id == id);
        return Task.CompletedTask;
    }

    private static PathEntity Clone(PathEntity p) => new() { Id = p.Id, CharacterId = p.CharacterId, Name = p.Name, Path = p.Path };
}

/// <summary>A real SQLite database in a temporary file, migrated, with two MUDs and three characters.</summary>
internal sealed class ListsDatabase : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "omnimud-t-" + Guid.NewGuid().ToString("N"));

    public ListsDatabase()
    {
        Directory.CreateDirectory(_dir);
        Factory = new SqliteConnectionFactory(Path.Combine(_dir, "omnimud.db"));
        using (var connection = Factory.Create())
            MigrationRunner.RunAsync(connection).GetAwaiter().GetResult();

        Muds = new SqliteMudRepository(Factory);
        Characters = new SqliteCharacterRepository(Factory);
        Aliases = new SqliteAliasRepository(Factory);
        Triggers = new SqliteTriggerRepository(Factory);
        Paths = new SqlitePathRepository(Factory);
        Directions = new SqliteDirectionRepository(Factory);
        Options = new SqliteOptionRepository(Factory);
        Exchange = new ExchangeService(Factory);

        var now = DateTime.UtcNow;
        MudId = Muds.AddAsync(new MudEntity { Name = "Reinos", Host = "rl.example.org", Port = 23, CreatedAt = now, UpdatedAt = now }).GetAwaiter().GetResult();
        OtherMudId = Muds.AddAsync(new MudEntity { Name = "Otro", Host = "otro.example.org", Port = 4000, CreatedAt = now, UpdatedAt = now }).GetAwaiter().GetResult();
        CharacterId = AddCharacter(MudId, "Gandalf");
        SecondCharacterId = AddCharacter(MudId, "Frodo");
        ThirdCharacterId = AddCharacter(OtherMudId, "Aragorn");
    }

    private int AddCharacter(int mudId, string name) =>
        Characters.AddAsync(new CharacterEntity { MudId = mudId, Name = name, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }).GetAwaiter().GetResult();

    public IDbConnectionFactory Factory { get; }
    public IMudRepository Muds { get; }
    public ICharacterRepository Characters { get; }
    public IAliasRepository Aliases { get; }
    public ITriggerRepository Triggers { get; }
    public IPathRepository Paths { get; }
    public IDirectionRepository Directions { get; }
    public IOptionRepository Options { get; }
    public IExchangeService Exchange { get; }
    public int MudId { get; }
    public int OtherMudId { get; }
    public int CharacterId { get; }
    public int SecondCharacterId { get; }
    public int ThirdCharacterId { get; }

    public string FilePath(string name) => Path.Combine(_dir, name);

    public TriggerEntity NewTrigger(int characterId, string name, string pattern = "hola", string action = "saludar") => new()
    {
        Id = Guid.NewGuid().ToString(), CharacterId = characterId, Name = name, Pattern = pattern, Action = action,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>
/// Creating a control installs a WinForms synchronization context on the STA thread, so awaited
/// continuations are posted to a message loop that a test does not run. This runs it until the task ends.
/// </summary>
internal static class StaPump
{
    public static void Wait(Task task, int timeoutMs = 30_000)
    {
        var limit = Environment.TickCount64 + timeoutMs;
        while (!task.IsCompleted)
        {
            if (Environment.TickCount64 > limit) throw new TimeoutException("The UI task did not finish.");
            Application.DoEvents();
            Thread.Sleep(5);
        }
        task.GetAwaiter().GetResult();
    }
}
