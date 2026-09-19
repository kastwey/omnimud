using Microsoft.Data.Sqlite;
using Omnimud.Core.Security;
using Omnimud.Core.Session;
using Omnimud.Data;
using Omnimud.Data.Entities;
using Omnimud.Data.Exchange;
using Omnimud.Data.Migrations;
using Omnimud.Data.Repositories;
using Omnimud.UI.Presenters;
using Omnimud.UI.Services;

namespace Omnimud.UI.Tests.Presenters;

/// <summary>Answers the system dialogs from a script and records everything that was shown.</summary>
internal sealed class ScriptedPrompts : IUserPrompts
{
    public Queue<bool> ConfirmAnswers { get; } = new();
    /// <summary>Answer when <see cref="ConfirmAnswers"/> is empty.</summary>
    public bool DefaultConfirm { get; set; } = true;
    public string? OpenFile { get; set; }
    public string? SaveFile { get; set; }
    public string? Folder { get; set; }

    public List<string> Confirms { get; } = [];
    public List<string> Infos { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> Errors { get; } = [];
    public string? SuggestedFileName { get; private set; }

    public bool Confirm(string message, string? title = null)
    {
        Confirms.Add(message);
        return ConfirmAnswers.Count > 0 ? ConfirmAnswers.Dequeue() : DefaultConfirm;
    }

    public void Info(string message, string? title = null) => Infos.Add(message);
    public void Warn(string message, string? title = null) => Warnings.Add(message);
    public void Error(string message, string? title = null) => Errors.Add(message);
    public string? PickOpenFile(string title, string filter) => OpenFile;

    public string? PickSaveFile(string title, string filter, string? defaultFileName = null)
    {
        SuggestedFileName = defaultFileName;
        return SaveFile;
    }

    public string? PickFolder(string title, string? initialPath = null) => Folder;
}

/// <summary>Resolves import conflicts from a script: every conflict gets the same decision, or the import is cancelled.</summary>
internal sealed class ScriptedConflicts(ImportDecision? decision) : IImportConflictResolver
{
    public List<IReadOnlyList<ImportItem>> Asked { get; } = [];

    public IReadOnlyDictionary<string, ImportDecision>? Resolve(IReadOnlyList<ImportItem> items)
    {
        Asked.Add(items);
        if (decision is null) return null;
        var presenter = new ImportConflictsPresenter(items);
        presenter.SetAll(decision == ImportDecision.Overwrite);
        return presenter.Decisions;
    }
}

/// <summary>Launcher dialogs answered by code: the delegate fills the model as a user would and says OK or Cancel.</summary>
internal sealed class ScriptedLauncherDialogs : ILauncherDialogs
{
    public Func<MudEditorModel, bool> OnEditMud { get; set; } = _ => false;
    public Func<CharacterEditorModel, bool> OnEditCharacter { get; set; } = _ => false;
    public SessionProfile? QuickConnectProfile { get; set; }

    /// <summary>Like the real dialog: accepting means the model saved without problems.</summary>
    public bool EditMud(MudEditorModel model) =>
        OnEditMud(model) && model.SaveAsync().GetAwaiter().GetResult() is null;

    public bool EditCharacter(CharacterEditorModel model) =>
        OnEditCharacter(model) && model.SaveAsync().GetAwaiter().GetResult() is null;

    public SessionProfile? QuickConnect() => QuickConnectProfile;
}

/// <summary>Reversible fake: "protects" by prefixing, so tests can tell a protected blob from plaintext.</summary>
internal sealed class FakeProtector : IPasswordProtector
{
    public byte[] Protect(string plainPassword) => System.Text.Encoding.UTF8.GetBytes("enc:" + plainPassword);

    public string Unprotect(byte[] protectedData)
    {
        var text = System.Text.Encoding.UTF8.GetString(protectedData);
        return text.StartsWith("enc:", StringComparison.Ordinal) ? text[4..] : throw new InvalidOperationException("Not protected.");
    }
}

/// <summary>A real SQLite database in a temporary file, migrated, with every repository on top.</summary>
internal sealed class TempDatabase : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "omnimud-g-" + Guid.NewGuid().ToString("N"));

    public TempDatabase()
    {
        Directory.CreateDirectory(_dir);
        Factory = new SqliteConnectionFactory(Path.Combine(_dir, "omnimud.db"));
        using (var connection = Factory.Create())
            MigrationRunner.RunAsync(connection).GetAwaiter().GetResult();

        Muds = new SqliteMudRepository(Factory);
        Characters = new SqliteCharacterRepository(Factory);
        Rules = new SqliteMessageRuleRepository(Factory);
        Aliases = new SqliteAliasRepository(Factory);
        Triggers = new SqliteTriggerRepository(Factory);
        Paths = new SqlitePathRepository(Factory);
        Exchange = new ExchangeService(Factory);
    }

    public IDbConnectionFactory Factory { get; }
    public IMudRepository Muds { get; }
    public ICharacterRepository Characters { get; }
    public IMessageRuleRepository Rules { get; }
    public IAliasRepository Aliases { get; }
    public ITriggerRepository Triggers { get; }
    public IPathRepository Paths { get; }
    public IExchangeService Exchange { get; }

    public string FilePath(string name) => Path.Combine(_dir, name);

    public async Task<MudEntity> AddMudAsync(string name, string host = "mud.example.org", int port = 4000)
    {
        var mud = new MudEntity { Name = name, Host = host, Port = port, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        mud.Id = await Muds.AddAsync(mud);
        return mud;
    }

    public async Task<CharacterEntity> AddCharacterAsync(int mudId, string name, byte[]? password = null)
    {
        var character = new CharacterEntity { MudId = mudId, Name = name, EncryptedPassword = password, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        character.Id = await Characters.AddAsync(character);
        return character;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>
/// Creating a WinForms control installs a synchronization context on the STA thread: awaited
/// continuations are posted to a message loop that a test does not run. This pumps it.
/// </summary>
internal static class UiPump
{
    public static void Wait(Task task, int timeoutMs = 30_000)
    {
        var limit = Environment.TickCount64 + timeoutMs;
        while (!task.IsCompleted)
        {
            if (Environment.TickCount64 > limit) throw new TimeoutException("The UI task did not finish.");
            Application.DoEvents();
            Thread.Sleep(1);
        }
        task.GetAwaiter().GetResult();
    }

    public static T Wait<T>(Task<T> task, int timeoutMs = 30_000)
    {
        Wait((Task)task, timeoutMs);
        return task.Result;
    }
}
