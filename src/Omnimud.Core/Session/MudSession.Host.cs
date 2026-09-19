using Omnimud.Core.Commands;
using Omnimud.Core.Resources;
using Omnimud.Core.Scripting;
using Omnimud.Core.Telnet;
using Omnimud.Core.Text;
using Omnimud.Core.Triggers;

namespace Omnimud.Core.Session;

// What scripts (IScriptHost, any thread) and the input processor (IInputHost, inside the queue) can do.
public sealed partial class MudSession
{
    // ── IScriptHost: called from script threads, so everything goes through the queue ──

    public void Send(string command)
        => _queue.Post(() => _input.ExecuteAsync(command ?? string.Empty), ReportInternalError);

    public void SendRaw(string command)
        => _queue.Post(async () =>
        {
            if (_state == SessionState.Connected)
                await SendLineCoreAsync(command ?? string.Empty, log: true).ConfigureAwait(false);
        }, ReportInternalError);

    public void Display(string text)
        => _queue.Post(() => ProcessInjectedAsync(text ?? string.Empty), ReportInternalError);

    public void Echo(string text)
        => _queue.Post(() =>
        {
            EchoCore(text ?? string.Empty);
            return Task.CompletedTask;
        }, ReportInternalError);

    public void Say(string text, bool interrupt)
        => _queue.Post(() =>
        {
            // Asked for explicitly by a script: spoken whatever the window or silent mode say.
            RaiseAnnounce(text ?? string.Empty, interrupt ? AnnouncePriority.Interrupt : AnnouncePriority.Queue);
            return Task.CompletedTask;
        });

    public void AddMessage(string text)
        => _queue.Post(() =>
        {
            if (!string.IsNullOrEmpty(text))
                AddMessageCore(AnsiParser.Strip(text), null, null, alreadyAnnounced: false);
            return Task.CompletedTask;
        });

    public void PlaySound(string name, int loop, int volume, int priority)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        Guard(() => _sound.PlayTriggerSound(name, loop, volume, priority));
    }

    public bool StopSound(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        try
        {
            return _sound.StopTriggerSound(name);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void SetVariable(string name, string value) => _variables[name] = value ?? string.Empty;

    public string? GetVariable(string name) => _variables.TryGetValue(name, out var value) ? value : null;

    public bool RemoveVariable(string name) => _variables.TryRemove(name, out _);

    public bool IsVariableSet(string name) => _variables.ContainsKey(name);

    public void SetStatus(string text) => Raise(StatusChanged, text ?? string.Empty);

    public void Log(string text)
        => _queue.Post(() =>
        {
            _log.WriteLine(AnsiParser.Strip(text ?? string.Empty));
            return Task.CompletedTask;
        });

    public void Gag() => ScriptRun.Current.Value?.Gag();

    public double SecondsSinceLastActivity
        => _time.GetElapsedTime(Interlocked.Read(ref _lastActivityTimestamp)).TotalSeconds;

    public void ReportScriptError(string scriptName, string error)
    {
        if (ScriptRun.Current.Value is { } run)
            run.ErrorReported = true;

        _queue.Post(() =>
        {
            WriteSystem(string.Format(Strings.Session_ScriptError, scriptName, error), AnnouncePriority.Queue);
            return Task.CompletedTask;
        });
    }

    // ── IInputHost: called by the input processor, already inside the queue ──

    int? IInputHost.CharacterId => Profile.CharacterId;

    ISessionStore IInputHost.Store => _store;

    bool IInputHost.TriggersEnabled
    {
        get => _triggers.IsEnabled;
        set
        {
            if (value) _triggers.EnableAll();
            else _triggers.DisableAll();
        }
    }

    TriggerDefinition? IInputHost.FindTrigger(string name) => _triggers.FindByName(name);

    async Task<bool> IInputHost.RunCommandTriggersAsync(string word, IReadOnlyList<string> args, string fullCommand, int depth)
    {
        var matches = _triggers.MatchCommand(word);
        if (matches.Count == 0)
            return false;

        foreach (var trigger in matches)
            await FireTriggerAsync(trigger, args, fullCommand, () => fullCommand, fullCommand, depth, canGag: false).ConfigureAwait(false);
        return true;
    }

    Task IInputHost.SendLineAsync(string line, bool log) => SendLineCoreAsync(line, log);

    void IInputHost.WriteSystem(string text) => WriteSystem(text, AnnouncePriority.Queue);

    void IInputHost.RequestWindow(SessionWindow window, string? payload)
    {
        var handler = WindowRequested;
        if (handler is null) return;
        foreach (var target in handler.GetInvocationList())
        {
            try { ((Action<SessionWindow, string?>)target)(window, payload); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }
    }

    void IInputHost.RequestClear() => Raise(ClearRequested);

    void IInputHost.PlayUiSound(string name)
    {
        // The view plays client effects; without a view the session's own sound does.
        if (UiSoundRequested is { } handler)
            Raise(handler, name);
        else
            Guard(() => _sound.PlayUiSound(name));
    }

    bool IInputHost.Confirm(string question)
    {
        var handler = ConfirmRequested;
        if (handler is null) return true;

        try
        {
            return handler(question);
        }
        catch (Exception)
        {
            return true;
        }
    }

    // ── Sending ────────────────────────────────────────────────────────────

    /// <summary>One line to the MUD; the connection encodes it (MUD encoding, LF terminator, IAC doubled). Never throws.</summary>
    private async Task SendLineCoreAsync(string line, bool log)
    {
        try
        {
            await _connection.SendAsync(line, _cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            WriteSystem(string.Format(Strings.Session_SendError, ex.Message), AnnouncePriority.Queue);
            return;
        }

        Interlocked.Exchange(ref _lastActivityTimestamp, _time.GetTimestamp());
        if (log && !_passwordMode)
            _log.WriteLine(line);
    }

    private async Task<bool> SendBytesAsync(byte[] bytes)
    {
        try
        {
            await _connection.SendRawAsync(bytes, _cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            WriteSystem(string.Format(Strings.Session_SendError, ex.Message), AnnouncePriority.Queue);
            return false;
        }
    }
}
