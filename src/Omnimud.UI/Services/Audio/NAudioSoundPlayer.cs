using System.Diagnostics;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Omnimud.Core.Sound;
using Omnimud.Core.Text;

namespace Omnimud.UI.Services.Audio;

/// <summary>
/// Real <see cref="ISoundPlayer"/>: ONE output device (the Windows default, through the wave
/// mapper, so it follows the user when the default device changes) fed by a software mixer.
/// A device per sound was discarded: a fight in a MUD fires dozens of overlapping sounds and each
/// device costs a thread and a driver handle, while a mixer input costs a few bytes; and with one
/// device there is one single place where "no sound card" and "device lost" are handled.
/// The device is opened by the first sound and released after a while without anything playing,
/// so an idle client does not keep the audio endpoint (and the computer) awake.
/// Nothing here throws to the caller: what cannot be played returns handle 0 and is logged.
/// </summary>
public sealed class NAudioSoundPlayer : ISoundPlayer
{
    private static readonly WaveFormat s_mixFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
    private static readonly TimeSpan s_idleBeforeRelease = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan s_retryAfterFailure = TimeSpan.FromSeconds(5);

    private readonly object _gate = new();
    private readonly Dictionary<long, PlaybackSource> _live = [];
    private readonly Func<IWavePlayer> _outputFactory;
    private readonly Action<string> _log;
    private readonly AudioMixer _mixer;
    private readonly System.Threading.Timer _idleTimer;
    private IWavePlayer? _output;
    private long _nextHandle;
    private long _lastActivity = Environment.TickCount64;
    private long _lastOpenFailure = long.MinValue;
    private volatile float _masterGain = 1f;
    private int _masterVolume = 100;
    private bool _disposed;

    /// <param name="outputFactory">Creates the output device; null = the default Windows device. For tests and future back ends.</param>
    /// <param name="log">Where problems are reported; null = <see cref="Trace"/>.</param>
    public NAudioSoundPlayer(Func<IWavePlayer>? outputFactory = null, Action<string>? log = null)
    {
        _outputFactory = outputFactory ?? CreateDefaultOutput;
        _log = log ?? (message => Trace.WriteLine("[sound] " + message));
        _mixer = new AudioMixer(s_mixFormat, OnSourcesEnded);
        _idleTimer = new System.Threading.Timer(_ => ReleaseOutputIfIdle(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public event EventHandler<PlaybackEndedEventArgs>? PlaybackEnded;

    public int MasterVolume
    {
        get => Volatile.Read(ref _masterVolume);
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            Volatile.Write(ref _masterVolume, clamped);
            _masterGain = clamped / 100f;
        }
    }

    /// <summary>True while the output device is open. Diagnostic.</summary>
    public bool IsOutputOpen
    {
        get { lock (_gate) return _output is not null; }
    }

    public long Play(SoundPlayRequest request)
    {
        PlaybackSource? source = null;
        try
        {
            if (request is null || string.IsNullOrWhiteSpace(request.FilePath))
                return 0;

            if (!File.Exists(request.FilePath))
            {
                _log($"File not found: {request.FilePath}");
                return 0;
            }

            // Device first: without a sound card there is no point in decoding anything.
            lock (_gate)
            {
                if (_disposed || !EnsureOutput())
                    return 0;
            }

            source = PlaybackSource.Open(request, s_mixFormat, () => _masterGain);

            lock (_gate)
            {
                if (_disposed || _output is null)
                {
                    source.Dispose();
                    return 0;
                }

                source.Handle = ++_nextHandle;
                _live[source.Handle] = source;
                _lastActivity = Environment.TickCount64;
                _mixer.Add(source);
                return source.Handle;
            }
        }
        catch (Exception ex)
        {
            source?.Dispose();
            _log($"Cannot play '{request?.FilePath}': {ex.Message}");
            return 0;
        }
    }

    public void Stop(long handle)
    {
        PlaybackSource? source;
        lock (_gate)
            _live.TryGetValue(handle, out source);

        if (source is not null)
            End([source], PlaybackEndReason.Stopped);
    }

    public void Stop(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return;

        var byNameOnly = Path.GetFileName(fileName) == fileName;
        string? fullPath = null;
        if (!byNameOnly)
        {
            try { fullPath = Path.GetFullPath(fileName); }
            catch (Exception) { return; }
        }

        StopWhere(s => byNameOnly
            ? Path.GetFileName(s.FullPath).Equals(fileName, StringComparison.OrdinalIgnoreCase)
            : s.FullPath.Equals(fullPath, StringComparison.OrdinalIgnoreCase));
    }

    public void StopAll() => StopWhere(_ => true);

    public void StopByType(SoundType type) => StopWhere(s => s.Type == type);

    public void SetVolume(long handle, int volume)
    {
        lock (_gate)
        {
            if (_live.TryGetValue(handle, out var source))
                source.SetVolume(volume);
        }
    }

    public bool IsPlaying(long handle)
    {
        lock (_gate)
            return _live.ContainsKey(handle);
    }

    public void Dispose()
    {
        IWavePlayer? output;
        PlaybackSource[] sources;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            output = DetachOutput();
            sources = [.. _live.Values];
        }

        _idleTimer.Dispose();
        End(sources, PlaybackEndReason.Stopped);
        CloseOutput(output);
    }

    // ---- Ending playbacks --------------------------------------------------------------------

    private void StopWhere(Func<PlaybackSource, bool> predicate)
    {
        PlaybackSource[] sources;
        lock (_gate)
            sources = [.. _live.Values.Where(predicate)];

        End(sources, PlaybackEndReason.Stopped);
    }

    /// <summary>Audio thread: these sources have just left the mixer. Get off this thread at once.</summary>
    private void OnSourcesEnded(IReadOnlyList<PlaybackSource> sources) =>
        ThreadPool.QueueUserWorkItem(_ => End(sources, PlaybackEndReason.Completed));

    private void End(IReadOnlyList<PlaybackSource> sources, PlaybackEndReason reason)
    {
        var ended = new List<(PlaybackSource Source, PlaybackEndReason Reason)>();

        foreach (var source in sources)
        {
            // A sound can finish by itself at the very moment somebody stops it: report it once.
            if (!source.TryMarkFinished())
                continue;

            source.RequestStop();
            lock (_gate)
            {
                _live.Remove(source.Handle);
                _lastActivity = Environment.TickCount64;
            }

            _mixer.Remove(source);
            source.Dispose();

            if (source.Failure is not null)
                _log($"'{source.FullPath}' stopped half way: {source.Failure.Message}");
            ended.Add((source, source.Failure is not null ? PlaybackEndReason.Failed : reason));
        }

        if (ended.Count == 0 || PlaybackEnded is null)
            return;

        // Always from the pool and without locks: listeners call back into the player, and two of
        // them stopping each other's neighbours from inside their own locks would deadlock otherwise.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            foreach (var (source, endReason) in ended)
            {
                try
                {
                    PlaybackEnded?.Invoke(this, new PlaybackEndedEventArgs(source.Handle, source.FullPath, source.Type, endReason));
                }
                catch (Exception ex)
                {
                    _log($"A PlaybackEnded listener failed: {ex.Message}");
                }
            }
        });
    }

    // ---- Output device -----------------------------------------------------------------------

    /// <summary>Call with <see cref="_gate"/> held.</summary>
    private bool EnsureOutput()
    {
        if (_output is not null)
            return true;

        // A machine without sound card would otherwise try to open the device for every single sound.
        var now = Environment.TickCount64;
        if (_lastOpenFailure != long.MinValue && now - _lastOpenFailure < s_retryAfterFailure.TotalMilliseconds)
            return false;

        IWavePlayer? output = null;
        try
        {
            output = _outputFactory();
            output.PlaybackStopped += OnOutputStopped;
            // 16 bit out: every driver takes it, and the conversion clips the sum of loud sounds cleanly.
            output.Init(new SampleToWaveProvider16(_mixer));
            output.Play();
            _output = output;
            _lastActivity = now;
            return true;
        }
        catch (Exception ex)
        {
            _lastOpenFailure = now;
            _log($"No audio output available: {ex.Message}");
            if (output is not null)
            {
                output.PlaybackStopped -= OnOutputStopped;
                try { output.Dispose(); } catch (Exception) { }
            }
            return false;
        }
    }

    /// <summary>The mixer never ends, so the device only stops by itself when it breaks (unplugged, driver reset).</summary>
    private void OnOutputStopped(object? sender, StoppedEventArgs e)
    {
        IWavePlayer? output;
        PlaybackSource[] sources;
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _output))
                return;
            output = DetachOutput();
            sources = [.. _live.Values];
        }

        _log($"Audio output lost: {e.Exception?.Message ?? "stopped"}");

        // Off the device's own thread: closing a device from its callback can hang.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            End(sources, PlaybackEndReason.Failed);
            CloseOutput(output);
        });
    }

    private void ReleaseOutputIfIdle()
    {
        IWavePlayer? output;
        lock (_gate)
        {
            if (_output is null || _live.Count > 0 || !_mixer.IsEmpty ||
                Environment.TickCount64 - _lastActivity < s_idleBeforeRelease.TotalMilliseconds)
                return;
            output = DetachOutput();
        }

        CloseOutput(output);
    }

    /// <summary>Call with <see cref="_gate"/> held; close the result outside the lock.</summary>
    private IWavePlayer? DetachOutput()
    {
        var output = _output;
        _output = null;
        if (output is not null)
            output.PlaybackStopped -= OnOutputStopped;
        return output;
    }

    private void CloseOutput(IWavePlayer? output)
    {
        if (output is null)
            return;

        try
        {
            output.Stop();
        }
        catch (Exception)
        {
        }

        try
        {
            output.Dispose();
        }
        catch (Exception ex)
        {
            _log($"Error closing the audio output: {ex.Message}");
        }
    }

    private static IWavePlayer CreateDefaultOutput()
    {
        if (WaveOut.DeviceCount <= 0)
            throw new InvalidOperationException("There is no audio output device.");

        // DeviceNumber -1 = wave mapper = "whatever the default device is", also when it changes.
        // 3 x 40 ms: short enough for the click of the history to feel immediate, long enough not to stutter.
        return new WaveOut { DeviceNumber = -1, BufferMilliseconds = 40, NumberOfBuffers = 3 };
    }
}
