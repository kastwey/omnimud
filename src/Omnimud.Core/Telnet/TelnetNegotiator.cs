using System.Text;

namespace Omnimud.Core.Telnet;

/// <summary>
/// Stateful telnet parser: one instance per connection. It only parses and builds packets;
/// which options to accept is the session's decision.
/// </summary>
public sealed class TelnetNegotiator : ITelnetNegotiator
{
    private const byte Iac = 255;
    private const byte Will = 251;
    private const byte Wont = 252;
    private const byte Do = 253;
    private const byte Dont = 254;
    private const byte Sb = 250;
    private const byte Se = 240;
    private const byte GoAhead = 249;
    private const byte EndOfRecord = 239;
    private const byte GmcpOption = 201;

    /// <summary>A subnegotiation that never ends must not eat memory forever.</summary>
    private const int MaxSubnegotiationBytes = 64 * 1024;

    private enum State
    {
        Text,
        Iac,
        Verb,
        SubOption,
        SubData,
        SubIac
    }

    private State _state = State.Text;
    private byte _pendingVerb;
    private byte _subnegOption;
    private readonly List<byte> _subnegBuffer = [];

    public TelnetParseResult Process(ReadOnlySpan<byte> data)
    {
        var cleanBytes = new List<byte>(data.Length);
        List<TelnetNegotiation>? negotiations = null;
        List<GmcpMessage>? gmcpMessages = null;
        var promptMarkAt = -1;

        foreach (var b in data)
        {
            switch (_state)
            {
                case State.Text:
                    if (b == Iac) _state = State.Iac;
                    else cleanBytes.Add(b);
                    break;

                case State.Iac:
                    switch (b)
                    {
                        case Iac:
                            cleanBytes.Add(Iac);
                            _state = State.Text;
                            break;
                        case Will or Wont or Do or Dont:
                            _pendingVerb = b;
                            _state = State.Verb;
                            break;
                        case Sb:
                            _subnegBuffer.Clear();
                            _state = State.SubOption;
                            break;
                        case GoAhead or EndOfRecord:
                            promptMarkAt = cleanBytes.Count;
                            _state = State.Text;
                            break;
                        default:
                            // NOP, DM, BRK, IP, AO, AYT, EC, EL, stray SE...: two-byte commands, ignored.
                            _state = State.Text;
                            break;
                    }
                    break;

                case State.Verb:
                    (negotiations ??= []).Add(new TelnetNegotiation((TelnetVerb)_pendingVerb, (TelnetCommand)b));
                    _state = State.Text;
                    break;

                case State.SubOption:
                    _subnegOption = b;
                    _state = State.SubData;
                    break;

                case State.SubData:
                    if (b == Iac) _state = State.SubIac;
                    else AppendSubnegotiation(b);
                    break;

                case State.SubIac:
                    if (b == Se)
                    {
                        if (_subnegOption == GmcpOption && _subnegBuffer.Count > 0)
                            (gmcpMessages ??= []).Add(ParseGmcp(_subnegBuffer));
                        _subnegBuffer.Clear();
                        _state = State.Text;
                    }
                    else if (b == Iac)
                    {
                        AppendSubnegotiation(Iac);
                        _state = State.SubData;
                    }
                    else
                    {
                        // Malformed: IAC + something inside SB. Drop the pair and keep collecting.
                        _state = State.SubData;
                    }
                    break;
            }
        }

        return new TelnetParseResult
        {
            CleanData = cleanBytes.ToArray(),
            Negotiations = (IReadOnlyList<TelnetNegotiation>?)negotiations ?? [],
            GmcpMessages = (IReadOnlyList<GmcpMessage>?)gmcpMessages ?? [],
            EndsWithPromptMark = promptMarkAt >= 0 && promptMarkAt == cleanBytes.Count
        };
    }

    public void Reset()
    {
        _state = State.Text;
        _pendingVerb = 0;
        _subnegOption = 0;
        _subnegBuffer.Clear();
    }

    public byte[] BuildResponse(TelnetCommand command, TelnetVerb verb)
    {
        return [Iac, (byte)verb, (byte)command];
    }

    /// <summary>
    /// Builds a GMCP subnegotiation packet: IAC SB GMCP package payload IAC SE
    /// </summary>
    public static byte[] BuildGmcpPacket(string package, string? payload = null)
    {
        var content = payload is null ? package : $"{package} {payload}";
        var contentBytes = EscapeIac(Encoding.UTF8.GetBytes(content));
        var packet = new byte[contentBytes.Length + 5];
        packet[0] = Iac;
        packet[1] = Sb;
        packet[2] = GmcpOption;
        contentBytes.CopyTo(packet, 3);
        packet[^2] = Iac;
        packet[^1] = Se;
        return packet;
    }

    /// <summary>Doubles every 0xFF so text bytes are never taken for a telnet command.</summary>
    public static byte[] EscapeIac(byte[] data)
    {
        var count = 0;
        foreach (var b in data)
            if (b == Iac) count++;
        if (count == 0) return data;

        var result = new byte[data.Length + count];
        var j = 0;
        foreach (var b in data)
        {
            result[j++] = b;
            if (b == Iac) result[j++] = Iac;
        }
        return result;
    }

    private void AppendSubnegotiation(byte b)
    {
        if (_subnegBuffer.Count < MaxSubnegotiationBytes)
            _subnegBuffer.Add(b);
    }

    private static GmcpMessage ParseGmcp(List<byte> buffer)
    {
        var raw = Encoding.UTF8.GetString(buffer.ToArray());
        var spaceIndex = raw.IndexOf(' ');
        if (spaceIndex < 0)
            return new GmcpMessage(raw.Trim(), "{}");

        var package = raw[..spaceIndex].Trim();
        var payload = raw[(spaceIndex + 1)..].Trim();
        return new GmcpMessage(package, payload);
    }
}
