using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Omnimud.Core.Sound;
using Omnimud.Core.Text;

namespace Omnimud.UI.Services.Audio;

/// <summary>An open audio file: the reader that owns the file handle and its samples as floats.</summary>
internal sealed class DecodedFile(WaveStream reader, ISampleProvider samples) : IDisposable
{
    public ISampleProvider Samples { get; } = samples;

    public void Dispose()
    {
        try
        {
            reader.Dispose();
        }
        catch (Exception)
        {
        }
    }
}

/// <summary>Opens wav, mp3 and ogg files and brings them to the mixer's format.</summary>
internal static class AudioFileDecoder
{
    /// <summary>
    /// Opens the file with the decoder of its extension and, if that fails (sound packs are full of
    /// mp3 files called .wav, ADPCM wavs and odd bit depths), lets Media Foundation sniff the content.
    /// Throws if nothing can read it.
    /// </summary>
    public static DecodedFile Open(string path)
    {
        WaveStream? reader = null;
        try
        {
            reader = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".ogg" => new VorbisWaveReader(path),
                ".mp3" => new Mp3FileReaderBase(path, format => new AcmMp3FrameDecompressor(format)),
                _ => OpenWav(path)
            };
            return new DecodedFile(reader, reader.ToSampleProvider());
        }
        catch (Exception first)
        {
            reader?.Dispose();
            reader = null;
            try
            {
                reader = new MediaFoundationReader(path);
                return new DecodedFile(reader, reader.ToSampleProvider());
            }
            catch (Exception)
            {
                reader?.Dispose();
                throw new InvalidDataException($"'{path}' is not a playable audio file: {first.Message}", first);
            }
        }
    }

    /// <summary>Brings <paramref name="source"/> to the mixer's channel count and sample rate.</summary>
    public static ISampleProvider ToMixFormat(ISampleProvider source, WaveFormat mixFormat)
    {
        if (source.WaveFormat.Channels == 1 && mixFormat.Channels == 2)
        {
            source = new MonoToStereoSampleProvider(source);
        }
        else if (source.WaveFormat.Channels != mixFormat.Channels)
        {
            // Surround files: keep the front pair.
            var multiplexer = new MultiplexingSampleProvider([source], mixFormat.Channels);
            for (var channel = 0; channel < mixFormat.Channels; channel++)
                multiplexer.ConnectInputToOutput(Math.Min(channel, source.WaveFormat.Channels - 1), channel);
            source = multiplexer;
        }

        if (source.WaveFormat.SampleRate != mixFormat.SampleRate)
            source = new WdlResamplingSampleProvider(source, mixFormat.SampleRate);

        return source;
    }

    private static WaveStream OpenWav(string path)
    {
        var reader = new WaveFileReader(path);
        try
        {
            if (reader.WaveFormat.Encoding is WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat or WaveFormatEncoding.Extensible)
                return reader;

            // ADPCM, µ-law, GSM...: through the ACM codecs that come with Windows.
            return WaveFormatConversionStream.CreatePcmStream(reader);
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }
}

/// <summary>
/// Plays a file 1 time, N times or for ever (-1). Each pass REOPENS the file instead of seeking
/// back: seeking to the start is unreliable in the decoders (NVorbis does not always land on the
/// first sample; NAudio 3.1's mp3 reader returns garbage lengths after the second seek), while
/// reading a file from the beginning is the one path every decoder gets right.
/// Looping happens before resampling, so the resampler sees one continuous stream.
/// </summary>
internal sealed class LoopingSampleProvider : ISampleProvider, IDisposable
{
    private readonly string _path;
    private DecodedFile? _current;
    private int _remaining;
    private bool _producedInThisPass;

    public LoopingSampleProvider(string path, int loop)
    {
        _path = path;
        _remaining = loop < 0 ? -1 : Math.Max(loop, 1);
        _current = AudioFileDecoder.Open(path);
        WaveFormat = _current.Samples.WaveFormat;
    }

    public WaveFormat WaveFormat { get; }

    public int Read(Span<float> buffer)
    {
        var total = 0;
        while (total < buffer.Length && _current is not null)
        {
            var read = _current.Samples.Read(buffer[total..]);
            if (read > 0)
            {
                total += read;
                _producedInThisPass = true;
                continue;
            }

            // End of a pass. A file without a single sample must not spin here for ever.
            var again = _producedInThisPass && (_remaining < 0 || --_remaining > 0);
            _producedInThisPass = false;
            _current.Dispose();
            _current = null;

            if (again)
            {
                var next = AudioFileDecoder.Open(_path);
                if (next.Samples.WaveFormat.Equals(WaveFormat))
                    _current = next;
                else
                    next.Dispose(); // the file was replaced by a different one while looping
            }
        }

        return total;
    }

    public void Dispose()
    {
        _current?.Dispose();
        _current = null;
    }
}

/// <summary>One playback inside the mixer: decoding chain + volume + stop flag. Owns its file.</summary>
internal sealed class PlaybackSource : ISampleProvider, IDisposable
{
    private readonly LoopingSampleProvider _file;
    private readonly ISampleProvider _chain;
    private readonly Func<float> _masterVolume;
    private volatile float _volume;
    private volatile bool _stopRequested;
    private int _finished;
    private int _disposed;

    private PlaybackSource(SoundPlayRequest request, string fullPath, LoopingSampleProvider file, ISampleProvider chain, Func<float> masterVolume)
    {
        Request = request;
        FullPath = fullPath;
        _file = file;
        _chain = chain;
        _masterVolume = masterVolume;
        _volume = ToGain(request.Volume);
        WaveFormat = chain.WaveFormat;
    }

    public long Handle { get; set; }
    public SoundPlayRequest Request { get; }
    public string FullPath { get; }
    public SoundType Type => Request.Type;
    public WaveFormat WaveFormat { get; }

    /// <summary>Set by the audio thread when decoding blew up half way.</summary>
    public Exception? Failure { get; private set; }

    public static PlaybackSource Open(SoundPlayRequest request, WaveFormat mixFormat, Func<float> masterVolume)
    {
        var fullPath = Path.GetFullPath(request.FilePath);
        var file = new LoopingSampleProvider(fullPath, request.Loop);
        try
        {
            return new PlaybackSource(request, fullPath, file, AudioFileDecoder.ToMixFormat(file, mixFormat), masterVolume);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    public static float ToGain(int volume) => Math.Clamp(volume, 0, 100) / 100f;

    public void SetVolume(int volume) => _volume = ToGain(volume);

    public void RequestStop() => _stopRequested = true;

    /// <summary>True for the first caller only: whoever wins reports the end of this playback.</summary>
    public bool TryMarkFinished() => Interlocked.Exchange(ref _finished, 1) == 0;

    /// <summary>Returning less than asked tells the mixer that this playback is over.</summary>
    public int Read(Span<float> buffer)
    {
        if (_stopRequested || Volatile.Read(ref _disposed) != 0)
            return 0;

        var total = 0;
        try
        {
            while (total < buffer.Length)
            {
                var read = _chain.Read(buffer[total..]);
                if (read <= 0)
                    break;
                total += read;
            }
        }
        catch (Exception ex)
        {
            // Truncated or corrupt data in the middle of the file: end this sound, keep the rest playing.
            Failure = ex;
        }

        var gain = _volume * _masterVolume();
        if (gain != 1f)
        {
            var filled = buffer[..total];
            for (var i = 0; i < filled.Length; i++)
                filled[i] *= gain;
        }

        return total;
    }

    /// <summary>Only call once the mixer no longer reads this source.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _file.Dispose();
    }
}

/// <summary>
/// Adds up any number of playbacks. Never ends: with nothing to play it produces silence, so the
/// output device keeps running and the next sound starts without reopening it.
/// </summary>
internal sealed class AudioMixer : ISampleProvider
{
    private readonly object _gate = new();
    private readonly List<PlaybackSource> _sources = [];
    private readonly Action<IReadOnlyList<PlaybackSource>> _onEnded;
    private float[] _scratch = [];

    public AudioMixer(WaveFormat format, Action<IReadOnlyList<PlaybackSource>> onEnded)
    {
        WaveFormat = format;
        _onEnded = onEnded;
    }

    public WaveFormat WaveFormat { get; }

    public bool IsEmpty
    {
        get { lock (_gate) return _sources.Count == 0; }
    }

    public void Add(PlaybackSource source)
    {
        lock (_gate)
            _sources.Add(source);
    }

    /// <summary>When this returns the audio thread is not reading the source and never will again.</summary>
    public void Remove(PlaybackSource source)
    {
        lock (_gate)
            _sources.Remove(source);
    }

    public int Read(Span<float> buffer)
    {
        buffer.Clear();
        List<PlaybackSource>? ended = null;

        lock (_gate)
        {
            if (_sources.Count > 0 && _scratch.Length < buffer.Length)
                _scratch = new float[buffer.Length];

            for (var i = _sources.Count - 1; i >= 0; i--)
            {
                var source = _sources[i];
                var scratch = _scratch.AsSpan(0, buffer.Length);
                var read = source.Read(scratch);

                for (var n = 0; n < read; n++)
                    buffer[n] += scratch[n];

                if (read < buffer.Length)
                {
                    _sources.RemoveAt(i);
                    (ended ??= []).Add(source);
                }
            }
        }

        if (ended is not null)
            _onEnded(ended);

        return buffer.Length;
    }
}
