using Omnimud.Core.Sound;
using Omnimud.Core.Text;

namespace Omnimud.Core.Tests.Sound;

/// <summary>In-memory player: remembers what it was asked and lets the test end a playback.</summary>
internal sealed class FakeSoundPlayer : ISoundPlayer
{
    private readonly object _gate = new();
    private readonly Dictionary<long, SoundPlayRequest> _live = [];
    private long _next;

    public List<(long Handle, SoundPlayRequest Request)> Played { get; } = [];
    public List<long> Stopped { get; } = [];
    public Dictionary<long, int> Volumes { get; } = [];
    public bool FailPlay { get; set; }
    public int MasterVolume { get; set; } = 100;

    public event EventHandler<PlaybackEndedEventArgs>? PlaybackEnded;

    public IReadOnlyList<string> LiveFiles
    {
        get { lock (_gate) return _live.Values.Select(r => Path.GetFileName(r.FilePath)).OrderBy(n => n).ToArray(); }
    }

    public SoundPlayRequest LastRequest => Played[^1].Request;
    public long LastHandle => Played[^1].Handle;

    public long Play(SoundPlayRequest request)
    {
        if (FailPlay)
            return 0;

        lock (_gate)
        {
            var handle = ++_next;
            _live[handle] = request;
            Played.Add((handle, request));
            Volumes[handle] = request.Volume;
            return handle;
        }
    }

    /// <summary>The sound reached its end by itself.</summary>
    public void Finish(long handle) => End(handle, PlaybackEndReason.Completed);

    public void Stop(long handle)
    {
        if (End(handle, PlaybackEndReason.Stopped))
            Stopped.Add(handle);
    }

    public void Stop(string fileName)
    {
        foreach (var handle in Handles(r => r.FilePath.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
            Stop(handle);
    }

    public void StopAll()
    {
        foreach (var handle in Handles(_ => true))
            Stop(handle);
    }

    public void StopByType(SoundType type)
    {
        foreach (var handle in Handles(r => r.Type == type))
            Stop(handle);
    }

    public void SetVolume(long handle, int volume)
    {
        lock (_gate)
        {
            if (_live.ContainsKey(handle))
                Volumes[handle] = volume;
        }
    }

    public bool IsPlaying(long handle)
    {
        lock (_gate)
            return _live.ContainsKey(handle);
    }

    public void Dispose() => StopAll();

    private long[] Handles(Func<SoundPlayRequest, bool> predicate)
    {
        lock (_gate)
            return _live.Where(p => predicate(p.Value)).Select(p => p.Key).ToArray();
    }

    private bool End(long handle, PlaybackEndReason reason)
    {
        SoundPlayRequest? request;
        lock (_gate)
        {
            if (!_live.Remove(handle, out request))
                return false;
        }

        PlaybackEnded?.Invoke(this, new PlaybackEndedEventArgs(handle, request.FilePath, request.Type, reason));
        return true;
    }
}

/// <summary>Downloader driven by the test: by default it "downloads" a few bytes.</summary>
internal sealed class FakeSoundDownloader : ISoundDownloader
{
    private readonly object _gate = new();

    public List<(string Url, string LocalPath, SoundDownloadOptions Options)> Calls { get; } = [];

    /// <summary>What to do for a URL; writing the file is the behaviour's job. Null = write 4 bytes.</summary>
    public Func<string, string, Task>? Behaviour { get; set; }

    public Task DownloadAsync(string url, string localPath, CancellationToken ct = default) =>
        DownloadAsync(url, localPath, SoundDownloadOptions.Default, ct);

    public async Task DownloadAsync(string url, string localPath, SoundDownloadOptions options, CancellationToken ct = default)
    {
        lock (_gate)
            Calls.Add((url, localPath, options));

        if (Behaviour is not null)
        {
            await Behaviour(url, localPath);
            return;
        }

        WriteFile(localPath);
    }

    public static void WriteFile(string localPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        File.WriteAllBytes(localPath, [1, 2, 3, 4]);
    }
}

/// <summary>Random that always answers the same index.</summary>
internal sealed class FixedRandom(int value) : Random
{
    public override int Next(int maxValue) => Math.Min(value, maxValue - 1);
}
