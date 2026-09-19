namespace Omnimud.Core.Scripting;

/// <summary>
/// Host used by the overload that runs without a session: records what the script asked for so
/// that it can be returned in the <see cref="ScriptResult"/>. Variables live in the context.
/// om.get has no MUD to talk to, so it records the command and answers nil.
/// </summary>
internal sealed class CollectingScriptHost(ScriptContext context) : IScriptHost
{
    private readonly object _lock = new();
    private readonly List<string> _commands = [];
    private readonly List<string> _display = [];
    private readonly List<string> _notifications = [];
    private readonly List<string> _messages = [];
    private readonly List<string> _sounds = [];
    private bool _gagged;

    public ScriptResult ToResult()
    {
        lock (_lock)
        {
            return new ScriptResult
            {
                Success = true,
                CommandsToSend = _commands.ToArray(),
                DisplayMessages = _display.ToArray(),
                Notifications = _notifications.ToArray(),
                Messages = _messages.ToArray(),
                SoundsToPlay = _sounds.ToArray(),
                Gagged = _gagged
            };
        }
    }

    private void Add(List<string> list, string text)
    {
        lock (_lock) list.Add(text);
    }

    public void Send(string command) => Add(_commands, command);
    public void SendRaw(string command) => Add(_commands, command);

    public Task<string?> GetAsync(string command, string? expectedPattern, TimeSpan timeout, bool keepColors, CancellationToken ct)
    {
        Add(_commands, command);
        return Task.FromResult<string?>(null);
    }

    public void Display(string text) => Add(_display, text);
    public void Echo(string text) => Add(_display, text);
    public void Say(string text, bool interrupt) => Add(_notifications, text);
    public void AddMessage(string text) => Add(_messages, text);
    public void PlaySound(string name, int loop, int volume, int priority) => Add(_sounds, name);
    public bool StopSound(string name) => false;

    public void SetVariable(string name, string value)
    {
        lock (_lock) context.Variables[name] = value;
    }

    public string? GetVariable(string name)
    {
        lock (_lock) return context.Variables.TryGetValue(name, out var value) ? value : null;
    }

    public bool RemoveVariable(string name)
    {
        lock (_lock) return context.Variables.Remove(name);
    }

    public bool IsVariableSet(string name)
    {
        lock (_lock) return context.Variables.ContainsKey(name);
    }

    public void SetStatus(string text) { }
    public void Log(string text) => Add(_display, "[LOG] " + text);

    public void Gag()
    {
        lock (_lock) _gagged = true;
    }

    public double SecondsSinceLastActivity => 0;
    public void ReportScriptError(string scriptName, string error) { }
}
