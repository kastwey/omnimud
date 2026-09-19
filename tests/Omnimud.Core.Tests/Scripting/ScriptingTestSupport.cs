using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Omnimud.Core.Scripting;

namespace Omnimud.Core.Tests.Scripting;

/// <summary>
/// FakeTimeProvider that tells the test when the code under test has started waiting
/// (om.sleep, om.countdown and om.timer each create exactly one timer), so that tests can
/// advance the clock without real sleeps and without races.
/// </summary>
internal sealed class SignalingTimeProvider : FakeTimeProvider
{
    private readonly SemaphoreSlim _created = new(0);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        _created.Release();
        return timer;
    }

    public async Task WaitForTimerAsync()
    {
        bool created = await _created.WaitAsync(TimeSpan.FromSeconds(10));
        created.Should().BeTrue("the script should have started waiting");
    }

    /// <summary>Waits until the script is waiting, then moves the clock.</summary>
    public async Task WaitAndAdvanceAsync(TimeSpan delta)
    {
        await WaitForTimerAsync();
        Advance(delta);
    }
}

/// <summary>Hand-written host with real variable storage, for the acceptance tests.</summary>
internal sealed class RecordingHost : IScriptHost
{
    private readonly object _lock = new();
    private readonly Dictionary<string, string> _variables = new();

    public List<string> Sent { get; } = [];
    public List<string> SentRaw { get; } = [];
    public List<string> Displayed { get; } = [];
    public List<string> Spoken { get; } = [];
    public List<string> Messages { get; } = [];
    public List<string> Sounds { get; } = [];
    public List<string> StoppedSounds { get; } = [];
    public List<string> GetCommands { get; } = [];
    public List<(string Name, string Error)> Errors { get; } = [];
    public Queue<string?> GetReplies { get; } = new();

    public void Send(string command) { lock (_lock) Sent.Add(command); }
    public void SendRaw(string command) { lock (_lock) SentRaw.Add(command); }

    public Task<string?> GetAsync(string command, string? expectedPattern, TimeSpan timeout, bool keepColors, CancellationToken ct)
    {
        lock (_lock)
        {
            GetCommands.Add(command);
            return Task.FromResult(GetReplies.Count > 0 ? GetReplies.Dequeue() : null);
        }
    }

    public void Display(string text) { lock (_lock) Displayed.Add(text); }
    public void Echo(string text) { lock (_lock) Displayed.Add(text); }
    public void Say(string text, bool interrupt) { lock (_lock) Spoken.Add(text); }
    public void AddMessage(string text) { lock (_lock) Messages.Add(text); }
    public void PlaySound(string name, int loop, int volume, int priority) { lock (_lock) Sounds.Add($"{name}:{loop}"); }
    public bool StopSound(string name) { lock (_lock) StoppedSounds.Add(name); return true; }
    public void SetVariable(string name, string value) { lock (_lock) _variables[name] = value; }
    public string? GetVariable(string name) { lock (_lock) return _variables.GetValueOrDefault(name); }
    public bool RemoveVariable(string name) { lock (_lock) return _variables.Remove(name); }
    public bool IsVariableSet(string name) { lock (_lock) return _variables.ContainsKey(name); }
    public void SetStatus(string text) { }
    public void Log(string text) { }
    public void Gag() { }
    public double SecondsSinceLastActivity => 0;
    public void ReportScriptError(string scriptName, string error) { lock (_lock) Errors.Add((scriptName, error)); }
}
