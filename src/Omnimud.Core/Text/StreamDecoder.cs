using System.Text;

namespace Omnimud.Core.Text;

/// <summary>
/// Bytes → text with the MUD's encoding, keeping the decoder state between reads so a
/// multibyte character split across two packets is decoded correctly.
/// </summary>
public sealed class StreamDecoder
{
    private readonly Decoder _decoder;

    static StreamDecoder()
    {
        // windows-1252 and friends are not built into .NET.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public StreamDecoder(Encoding encoding)
    {
        Encoding = encoding;
        _decoder = encoding.GetDecoder();
    }

    public StreamDecoder(string encodingName) : this(ResolveEncoding(encodingName))
    {
    }

    public Encoding Encoding { get; }

    public string Decode(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return string.Empty;

        var buffer = new char[Encoding.GetMaxCharCount(data.Length)];
        var count = _decoder.GetChars(data, buffer, flush: false);
        return count == 0 ? string.Empty : new string(buffer, 0, count);
    }

    /// <summary>Drops any partial character (new connection).</summary>
    public void Reset() => _decoder.Reset();

    /// <summary>Encoding by name; unknown or empty names fall back to UTF-8.</summary>
    public static Encoding ResolveEncoding(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        try
        {
            var encoding = Encoding.GetEncoding(name.Trim());
            return encoding is UTF8Encoding ? new UTF8Encoding(false) : encoding;
        }
        catch (ArgumentException)
        {
            return new UTF8Encoding(false);
        }
    }
}
