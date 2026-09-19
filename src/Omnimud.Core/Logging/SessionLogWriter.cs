using System.Globalization;
using System.Text;
using Omnimud.Core.Options;
using Omnimud.Core.Resources;

namespace Omnimud.Core.Logging;

/// <summary>
/// The text log of one session: what was received and what was sent, without ANSI, in UTF-8,
/// always appending. Files live in &lt;base&gt;\&lt;mud&gt;\&lt;character&gt;\ and are named
/// yyyy-MM-dd.log (one per day) or yyyy-MM-dd HH-mm-ss.log (one per session).
/// A log failure never reaches the session: the writer just stops and reports it once.
/// Thread-safe.
/// </summary>
public sealed class SessionLogWriter : IDisposable
{
    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private readonly string _defaultBaseDirectory;

    private StreamWriter? _writer;
    private LogMode _mode = LogMode.None;
    private string? _directory;
    private DateOnly _fileDate;

    public SessionLogWriter(TimeProvider time, string defaultBaseDirectory)
    {
        _time = time;
        _defaultBaseDirectory = defaultBaseDirectory;
    }

    public bool IsOpen
    {
        get { lock (_gate) return _writer is not null; }
    }

    /// <summary>Full path of the file being written, or null.</summary>
    public string? CurrentPath { get; private set; }

    /// <summary>Raised (once per failure) with the error text when the file cannot be opened or written.</summary>
    public event Action<string>? Failed;

    /// <summary>
    /// Opens (or creates) the file for this session. With <see cref="LogMode.None"/> it only closes
    /// whatever was open. Returns false if the file could not be opened.
    /// </summary>
    public bool Open(LogMode mode, string? customBaseDirectory, string mudName, string? characterName)
    {
        string? error = null;
        lock (_gate)
        {
            CloseCore(writeFooter: false);
            _mode = mode;
            if (mode == LogMode.None)
                return true;

            var baseDirectory = string.IsNullOrWhiteSpace(customBaseDirectory) ? _defaultBaseDirectory : customBaseDirectory;
            _directory = Path.Combine(baseDirectory, SanitizeName(mudName));
            if (!string.IsNullOrWhiteSpace(characterName))
                _directory = Path.Combine(_directory, SanitizeName(characterName));

            var now = _time.GetLocalNow().DateTime;
            var fileName = mode == LogMode.PerDay
                ? now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log"
                : now.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture) + ".log";

            error = OpenFile(Path.Combine(_directory, fileName), now);
        }

        if (error is null) return true;
        Failed?.Invoke(error);
        return false;
    }

    /// <summary>Writes text (one or more lines) followed by a line break.</summary>
    public void WriteLine(string text)
    {
        string? error = null;
        lock (_gate)
        {
            if (_writer is null) return;

            try
            {
                RollOverIfNewDay();
                if (_writer is null) return;
                _writer.WriteLine(NormalizeLineBreaks(text));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                error = ex.Message;
                CloseCore(writeFooter: false);
            }
        }

        if (error is not null) Failed?.Invoke(error);
    }

    /// <summary>Closes the file, by default with the "session ended" line.</summary>
    public void Close(bool writeFooter = true)
    {
        lock (_gate)
            CloseCore(writeFooter);
    }

    public void Dispose() => Close(writeFooter: false);

    /// <summary>MUD and character names become folder names.</summary>
    public static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name.Trim())
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);

        var result = sb.ToString().TrimEnd('.', ' ');
        return result.Length == 0 ? "_" : result;
    }

    private string? OpenFile(string path, DateTime now)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
            _fileDate = DateOnly.FromDateTime(now);
            CurrentPath = path;
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _writer = null;
            CurrentPath = null;
            return ex.Message;
        }
    }

    private void RollOverIfNewDay()
    {
        if (_mode != LogMode.PerDay || _directory is null) return;

        var now = _time.GetLocalNow().DateTime;
        if (DateOnly.FromDateTime(now) == _fileDate) return;

        _writer?.Dispose();
        _writer = null;
        var error = OpenFile(Path.Combine(_directory, now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log"), now);
        if (error is not null) throw new IOException(error);
    }

    private void CloseCore(bool writeFooter)
    {
        if (_writer is null) return;

        try
        {
            if (writeFooter)
                _writer.WriteLine(string.Format(CultureInfo.InvariantCulture, Strings.Log_SessionEnded, _time.GetLocalNow().DateTime));
            _writer.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
        }

        _writer = null;
        CurrentPath = null;
    }

    private static string NormalizeLineBreaks(string text)
        => text.Contains('\n') ? text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine) : text;
}
