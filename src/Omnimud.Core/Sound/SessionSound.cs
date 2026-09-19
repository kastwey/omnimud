using System.Collections.Concurrent;
using Omnimud.Core.Text;

namespace Omnimud.Core.Sound;

/// <summary>
/// Sound of one session on top of a player shared with the other sessions. It only ever touches
/// the playbacks it started (by handle), so two windows never stop each other's sounds, and it
/// applies its own master volume instead of the player's.
/// </summary>
public sealed class SessionSound : ISessionSound
{
    private enum Table { Sound, Music, Trigger, Ui }

    private sealed record Live(long Handle, Table Table, string FilePath, string Name, int Priority, int Volume);

    // Shared by every session: two windows on the same MUD write to the same folder.
    private static readonly ConcurrentDictionary<string, Lazy<Task<bool>>> s_downloads = new(StringComparer.OrdinalIgnoreCase);

    private readonly ISoundPlayer _player;
    private readonly ISoundDownloader _downloader;
    private readonly Random _random;
    private readonly object _gate = new();
    private readonly Dictionary<long, Live> _live = [];
    private readonly HashSet<string> _myDownloads = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Task> _pending = [];
    private CancellationTokenSource _downloadCts = new();
    private SoundSettings? _settings;
    private string? _defaultUrl;
    private volatile bool _windowActive = true;
    private bool _disposed;

    public SessionSound(ISoundPlayer player, ISoundDownloader downloader, Random? random = null)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _random = random ?? Random.Shared;
        _player.PlaybackEnded += OnPlaybackEnded;
    }

    public bool IsWindowActive
    {
        get => _windowActive;
        set => _windowActive = value;
    }

    public void Configure(SoundSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_gate)
        {
            if (_disposed)
                return;

            _settings = settings;

            foreach (var live in _live.Values.ToArray())
            {
                if (!IsEnabled(settings, live.Table))
                    StopLive(live);
                else
                    Guard(() => _player.SetVolume(live.Handle, EffectiveVolume(settings, live.Volume)));
            }
        }
    }

    /// <summary>
    /// Never throws and never waits for a download: the returned task is complete as soon as the
    /// command has been played, discarded or handed to a background download, so the receive loop
    /// is not held up. See <see cref="WhenDownloadsCompleteAsync"/>.
    /// </summary>
    public Task HandleMspAsync(SoundCommand command, CancellationToken ct = default)
    {
        try
        {
            if (command is null || ct.IsCancellationRequested)
                return Task.CompletedTask;

            var table = command.Type switch
            {
                SoundType.Music => Table.Music,
                SoundType.Trigger => Table.Trigger,
                _ => Table.Sound
            };

            if (command.IsStop)
            {
                lock (_gate)
                {
                    if (!string.IsNullOrWhiteSpace(command.Url))
                        _defaultUrl = command.Url;
                    StopTable(table);
                }
                return Task.CompletedTask;
            }

            HandleMspPlay(command, table, ct);
        }
        catch (Exception)
        {
            // Sound is never a reason to break the session.
        }

        return Task.CompletedTask;
    }

    /// <summary>Completes when the downloads started so far have finished and their sounds have been played.</summary>
    public Task WhenDownloadsCompleteAsync()
    {
        lock (_gate)
            return Task.WhenAll(_pending.ToArray());
    }

    public bool PlayTriggerSound(string name, int loop = 1, int volume = 100, int priority = 50)
    {
        try
        {
            SoundSettings? settings;
            lock (_gate)
            {
                if (_disposed || _settings is null)
                    return false;
                settings = _settings;
            }

            var file = ResolveTriggerSound(settings, name);
            if (file is null)
                return false;

            Play(Table.Trigger, file, name, volume, loop, priority, cont: false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool StopTriggerSound(string name)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            lock (_gate)
            {
                if (_disposed)
                    return false;

                Prune();
                var candidates = _settings is null ? [] : TriggerCandidates(_settings, name).SelectMany(SoundPathResolver.FindMatches).ToArray();
                var targets = _live.Values
                    .Where(l => l.Table == Table.Trigger &&
                                (l.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                                 candidates.Contains(l.FilePath, StringComparer.OrdinalIgnoreCase)))
                    .ToArray();

                foreach (var live in targets)
                    StopLive(live);

                return targets.Length > 0;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void PlayUiSound(string name)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name) || name.AsSpan().IndexOfAny("/\\:*?") >= 0)
                return;

            SoundSettings? settings;
            lock (_gate)
                settings = _settings;
            if (settings is null || !settings.EnableSounds)
                return;

            var names = SoundPathResolver.IsPlayableExtension(Path.GetExtension(name))
                ? [name]
                : SoundPathResolver.PlayableExtensions.Select(e => name + e);

            foreach (var candidate in names)
            {
                var path = SoundPathResolver.Combine(settings.AppSoundDirectory, null, candidate);
                if (path is not null && File.Exists(path))
                {
                    Play(Table.Ui, path, name, 100, 1, 50, cont: false);
                    return;
                }
            }
        }
        catch (Exception)
        {
        }
    }

    public void StopAll()
    {
        lock (_gate)
        {
            CancelDownloads(renew: !_disposed);
            foreach (var live in _live.Values.ToArray())
                StopLive(live);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        _player.PlaybackEnded -= OnPlaybackEnded;
        StopAll();
    }

    // ---- MSP -------------------------------------------------------------------------------

    private void HandleMspPlay(SoundCommand command, Table table, CancellationToken ct)
    {
        SoundSettings? settings;
        string? defaultUrl;
        lock (_gate)
        {
            if (_disposed || _settings is null || !IsEnabled(_settings, table))
                return;
            settings = _settings;
            defaultUrl = _defaultUrl;
        }

        var pattern = SoundPathResolver.Combine(settings.MudSoundDirectory, command.SoundCategory, command.FileName);
        if (pattern is null)
            return;

        var matches = SoundPathResolver.FindMatches(pattern);
        if (matches.Count > 0)
        {
            var file = matches.Count == 1 ? matches[0] : matches[NextRandom(matches.Count)];
            Play(table, file, command.FileName, command.Volume, command.Loop, command.Priority, command.Continue);
            return;
        }

        var baseUrl = string.IsNullOrWhiteSpace(command.Url) ? defaultUrl : command.Url;
        if (!settings.DownloadSounds || baseUrl is null || SoundPathResolver.HasWildcards(pattern))
            return;

        var segments = SoundPathResolver.NormalizeSegments(command.SoundCategory, command.FileName);
        var urls = BuildDownloadUrls(baseUrl, segments!, settings.AllowHttpDownloads);
        if (urls.Count == 0)
            return;

        CancellationTokenSource linked;
        lock (_gate)
        {
            // The original could start the same download several times; here a file is requested once.
            if (_disposed || !_myDownloads.Add(pattern))
                return;
            linked = CancellationTokenSource.CreateLinkedTokenSource(_downloadCts.Token, ct);
        }

        var options = new SoundDownloadOptions
        {
            AllowHttp = settings.AllowHttpDownloads,
            Proxy = settings.DownloadProxy,
            ProxySettings = settings.DownloadProxySettings
        };
        var task = DownloadAndPlayAsync(command, table, pattern, urls, options, linked);
        lock (_gate)
        {
            _pending.RemoveAll(t => t.IsCompleted);
            if (!task.IsCompleted)
                _pending.Add(task);
        }
    }

    private async Task DownloadAndPlayAsync(SoundCommand command, Table table, string target, IReadOnlyList<string> urls,
        SoundDownloadOptions options, CancellationTokenSource linked)
    {
        try
        {
            var token = linked.Token;
            var shared = s_downloads.GetOrAdd(target,
                key => new Lazy<Task<bool>>(() => DownloadFirstAvailableAsync(_downloader, key, urls, options, token)));

            var downloaded = await shared.Value.ConfigureAwait(false);

            // Settings may have changed meanwhile: Play checks the switches again.
            if (downloaded && !token.IsCancellationRequested)
                Play(table, target, command.FileName, command.Volume, command.Loop, command.Priority, command.Continue);
        }
        catch (Exception)
        {
        }
        finally
        {
            lock (_gate)
                _myDownloads.Remove(target);
            linked.Dispose();
        }
    }

    private static async Task<bool> DownloadFirstAvailableAsync(ISoundDownloader downloader, string target,
        IReadOnlyList<string> urls, SoundDownloadOptions options, CancellationToken ct)
    {
        // Leave the caller right away, even with a downloader that works synchronously.
        await Task.Yield();

        try
        {
            foreach (var url in urls)
            {
                if (ct.IsCancellationRequested)
                    return false;

                try
                {
                    await downloader.DownloadAsync(url, target, options, ct).ConfigureAwait(false);

                    var info = new FileInfo(target);
                    if (info.Exists && info.Length > 0)
                        return true;
                    if (info.Exists)
                        info.Delete();
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return false;
                }
                catch (Exception)
                {
                    // Next candidate (https failed, http may still work if the user allows it).
                }
            }

            return false;
        }
        finally
        {
            s_downloads.TryRemove(target, out _);
        }
    }

    /// <summary>U (without trailing slash) + "/" + [T/]name. HTTPS always goes first; plain HTTP only if allowed.</summary>
    internal static IReadOnlyList<string> BuildDownloadUrls(string baseUrl, string[] segments, bool allowHttp)
    {
        var relative = string.Join('/', segments.Select(Uri.EscapeDataString));
        var trimmed = baseUrl.Trim().TrimEnd('/');

        // Some MUDs send the complete URL of the file in U= instead of the folder.
        var full = trimmed.EndsWith("/" + segments[^1], StringComparison.OrdinalIgnoreCase) ||
                   trimmed.EndsWith("/" + Uri.EscapeDataString(segments[^1]), StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + "/" + relative;

        if (!Uri.TryCreate(full, UriKind.Absolute, out var uri))
            return [];

        if (uri.Scheme == Uri.UriSchemeHttps)
            return [uri.AbsoluteUri];

        if (uri.Scheme != Uri.UriSchemeHttp)
            return [];

        var secure = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = uri.IsDefaultPort ? -1 : uri.Port };
        return allowHttp ? [secure.Uri.AbsoluteUri, uri.AbsoluteUri] : [secure.Uri.AbsoluteUri];
    }

    // ---- Trigger sounds --------------------------------------------------------------------

    private string? ResolveTriggerSound(SoundSettings settings, string name)
    {
        foreach (var candidate in TriggerCandidates(settings, name))
        {
            var matches = SoundPathResolver.FindMatches(candidate);
            if (matches.Count > 0)
                return matches.Count == 1 ? matches[0] : matches[NextRandom(matches.Count)];
        }

        return null;
    }

    /// <summary>The path as given (only if it is a full local path), then the MUD's folder, then the application's.</summary>
    private static IEnumerable<string> TriggerCandidates(SoundSettings settings, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            yield break;

        name = name.Trim();

        if (Path.IsPathFullyQualified(name))
        {
            // A script may come from someone else: a UNC path would hand the user's credentials to a remote server.
            if (name.StartsWith(@"\\", StringComparison.Ordinal) || name.StartsWith("//", StringComparison.Ordinal))
                yield break;

            string? full = null;
            try
            {
                var withExtension = SoundPathResolver.IsPlayableExtension(Path.GetExtension(name)) ? name : name + ".wav";
                full = Path.GetFullPath(withExtension);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
            }

            if (full is not null && !SoundPathResolver.HasWildcards(Path.GetDirectoryName(full) ?? string.Empty))
                yield return full;
            yield break;
        }

        if (SoundPathResolver.Combine(settings.MudSoundDirectory, null, name) is { } inMud)
            yield return inMud;
        if (SoundPathResolver.Combine(settings.AppSoundDirectory, null, name) is { } inApp)
            yield return inApp;
    }

    // ---- Playback and priorities -----------------------------------------------------------

    private void Play(Table table, string filePath, string name, int volume, int loop, int priority, bool cont)
    {
        lock (_gate)
        {
            if (_disposed || _settings is null || !IsEnabled(_settings, table))
                return;

            Prune();

            if (table != Table.Ui)
            {
                var sameTable = _live.Values.Where(l => l.Table == table).ToArray();
                var sameFile = sameTable.Where(l => l.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)).ToArray();

                if (cont && sameFile.Length > 0)
                    return;

                if (sameTable.Any(l => l.Priority > priority))
                    return;

                // Lower priority gives way; the same file restarts; music never overlaps music.
                foreach (var live in sameTable)
                {
                    if (live.Priority < priority || table == Table.Music || sameFile.Contains(live))
                        StopLive(live);
                }
            }

            volume = Math.Clamp(volume, 0, 100);
            var request = new SoundPlayRequest
            {
                FilePath = filePath,
                Type = table switch
                {
                    Table.Music => SoundType.Music,
                    Table.Trigger => SoundType.Trigger,
                    _ => SoundType.Sound
                },
                Volume = EffectiveVolume(_settings, volume),
                Loop = loop == 0 ? 1 : Math.Max(loop, -1),
                Priority = priority,
                Continue = cont
            };

            long handle = 0;
            Guard(() => handle = _player.Play(request));
            if (handle != 0)
                _live[handle] = new Live(handle, table, filePath, name, priority, volume);
        }
    }

    private bool IsEnabled(SoundSettings settings, Table table) => table switch
    {
        Table.Music => settings.EnableMusic && (_windowActive || settings.PlayMusicInBackground),
        Table.Ui => settings.EnableSounds,
        _ => settings.EnableSounds && (_windowActive || settings.PlaySoundsInBackground)
    };

    private static int EffectiveVolume(SoundSettings settings, int volume) =>
        (int)Math.Round(Math.Clamp(volume, 0, 100) * Math.Clamp(settings.Volume, 0, 100) / 100.0, MidpointRounding.AwayFromZero);

    private void StopTable(Table table)
    {
        foreach (var live in _live.Values.Where(l => l.Table == table).ToArray())
            StopLive(live);
    }

    private void StopLive(Live live)
    {
        _live.Remove(live.Handle);
        Guard(() => _player.Stop(live.Handle));
    }

    /// <summary>Safety net in case an end notification was lost: a dead entry would block lower priorities for ever.</summary>
    private void Prune()
    {
        foreach (var handle in _live.Keys.ToArray())
        {
            var playing = true;
            Guard(() => playing = _player.IsPlaying(handle));
            if (!playing)
                _live.Remove(handle);
        }
    }

    private void OnPlaybackEnded(object? sender, PlaybackEndedEventArgs e)
    {
        lock (_gate)
            _live.Remove(e.Handle);
    }

    private void CancelDownloads(bool renew)
    {
        var old = _downloadCts;
        if (renew)
            _downloadCts = new CancellationTokenSource();
        try
        {
            old.Cancel();
        }
        catch (AggregateException)
        {
        }
        if (renew)
            old.Dispose();
    }

    private int NextRandom(int count)
    {
        lock (_gate)
            return Math.Clamp(_random.Next(count), 0, count - 1);
    }

    private static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception)
        {
            // A broken audio backend must not take the session down.
        }
    }
}
