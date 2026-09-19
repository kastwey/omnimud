using System.Text.RegularExpressions;
using Omnimud.Core.Resources;
using Omnimud.Core.Text;

namespace Omnimud.Core.Session;

// om.get: send a command and capture the reply instead of showing it. FIFO, one at a time, so
// two scripts asking at once never get each other's lines.
public sealed partial class MudSession
{
    private readonly Queue<GetRequest> _getQueue = new();
    private GetRequest? _activeGet;

    public Task<string?> GetAsync(string command, string? expectedPattern, TimeSpan timeout, bool keepColors, CancellationToken ct)
    {
        // A script about to block on the session can no longer hide its line in time.
        ScriptRun.Current.Value?.ReleaseLine();

        Regex? pattern = null;
        if (!string.IsNullOrEmpty(expectedPattern))
        {
            try
            {
                pattern = new Regex(expectedPattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            }
            catch (ArgumentException)
            {
                ReportScriptError("om.get", string.Format(Strings.Session_InvalidGetPattern, expectedPattern));
                return Task.FromResult<string?>(null);
            }
        }

        if (ct.IsCancellationRequested)
            return Task.FromCanceled<string?>(ct);

        var request = new GetRequest(command ?? string.Empty, pattern, timeout, keepColors);
        if (ct.CanBeCanceled)
        {
            request.Cancellation = ct.Register(() => _queue.Post(async () =>
            {
                request.Completion.TrySetCanceled(ct);
                if (ReferenceEquals(_activeGet, request))
                    await FinishGetAsync(request, null).ConfigureAwait(false);
            }));
        }

        _queue.Post(async () =>
        {
            _getQueue.Enqueue(request);
            if (_activeGet is null)
                await StartNextGetAsync().ConfigureAwait(false);
        }, ex => request.Completion.TrySetResult(null));

        return request.Completion.Task;
    }

    private async Task StartNextGetAsync()
    {
        while (_activeGet is null && _getQueue.TryDequeue(out var request))
        {
            if (request.Completion.Task.IsCompleted)
            {
                request.Dispose();
                continue;
            }

            if (_state != SessionState.Connected)
            {
                request.Completion.TrySetResult(null);
                request.Dispose();
                continue;
            }

            // What was already on its way belongs to the screen, not to this request.
            await FlushPendingTextAsync().ConfigureAwait(false);

            _activeGet = request;
            await SendLineCoreAsync(request.Command, log: false).ConfigureAwait(false);

            if (request.Timeout > TimeSpan.Zero && request.Timeout != Timeout.InfiniteTimeSpan)
            {
                request.TimeoutTimer = _time.CreateTimer(_ => _queue.Post(async () =>
                {
                    if (ReferenceEquals(_activeGet, request))
                        await FinishGetAsync(request, null).ConfigureAwait(false);
                }), null, request.Timeout, Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <summary>True if the line belongs to the request and must not be shown.</summary>
    private async Task<bool> TryCaptureAsync(GetRequest request, string raw, SessionLineKind kind)
    {
        var plain = AnsiParser.Strip(raw).Replace(SpeakMarker, string.Empty);
        var matches = request.Pattern is null || SafeIsMatch(request.Pattern, plain);

        if (kind == SessionLineKind.Prompt)
        {
            if (request.Pattern is null)
            {
                // The prompt closes the reply to a command the user never saw: swallow it.
                await FinishGetAsync(request, request.Result()).ConfigureAwait(false);
                return true;
            }

            if (matches)
            {
                request.Add(request.KeepColors ? raw : plain);
                await FinishGetAsync(request, request.Result()).ConfigureAwait(false);
                return true;
            }

            if (request.HasCaptured)
                await FinishGetAsync(request, request.Result()).ConfigureAwait(false);
            return false;
        }

        if (!matches)
            return false;

        request.Add(request.KeepColors ? raw : plain);
        return true;
    }

    /// <summary>After each packet: if the request already has something and the MUD goes quiet, it is done.</summary>
    private void ArmGetQuietTimer()
    {
        if (_activeGet is not { HasCaptured: true } request)
            return;

        var delay = TimeSpan.FromMilliseconds(Math.Max(1, _options.PromptFlushMilliseconds));
        request.LastDataTimestamp = _time.GetTimestamp();
        request.QuietDelay = delay;

        if (request.QuietTimer is null)
        {
            request.QuietTimer = _time.CreateTimer(_ => _queue.Post(async () =>
            {
                if (!ReferenceEquals(_activeGet, request)) return;
                // Re-armed while this callback was queued: not quiet yet.
                if (_time.GetElapsedTime(request.LastDataTimestamp) < request.QuietDelay / 2) return;
                // The prompt that closes a hidden reply is part of it.
                if (request.Pattern is null) _lineBuffer.TakePending();
                await FinishGetAsync(request, request.Result()).ConfigureAwait(false);
            }), null, delay, Timeout.InfiniteTimeSpan);
        }
        else
        {
            request.QuietTimer.Change(delay, Timeout.InfiniteTimeSpan);
        }
    }

    private async Task FinishGetAsync(GetRequest request, string? result)
    {
        if (ReferenceEquals(_activeGet, request))
            _activeGet = null;

        request.Dispose();
        request.Completion.TrySetResult(result);
        await StartNextGetAsync().ConfigureAwait(false);
    }

    /// <summary>Disconnection or shutdown: nobody is going to answer.</summary>
    private void FailPendingGets()
    {
        if (_activeGet is { } active)
        {
            _activeGet = null;
            active.Dispose();
            active.Completion.TrySetResult(null);
        }

        while (_getQueue.TryDequeue(out var request))
        {
            request.Dispose();
            request.Completion.TrySetResult(null);
        }
    }

    private static bool SafeIsMatch(Regex regex, string text)
    {
        try
        {
            return regex.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private sealed class GetRequest(string command, Regex? pattern, TimeSpan timeout, bool keepColors) : IDisposable
    {
        private readonly List<string> _lines = [];

        public string Command { get; } = command;
        public Regex? Pattern { get; } = pattern;
        public TimeSpan Timeout { get; } = timeout;
        public bool KeepColors { get; } = keepColors;
        public TaskCompletionSource<string?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenRegistration Cancellation { get; set; }
        public ITimer? TimeoutTimer { get; set; }
        public ITimer? QuietTimer { get; set; }
        public long LastDataTimestamp { get; set; }
        public TimeSpan QuietDelay { get; set; }
        public bool HasCaptured => _lines.Count > 0;

        public void Add(string line) => _lines.Add(line);

        public string Result() => string.Join('\n', _lines);

        public void Dispose()
        {
            TimeoutTimer?.Dispose();
            QuietTimer?.Dispose();
            Cancellation.Dispose();
        }
    }
}
