namespace Omnimud.Core.Telnet;

public interface ITelnetNegotiator
{
    /// <summary>
    /// Processes raw bytes, stripping telnet sequences and returning clean text bytes.
    /// Any IAC commands found are returned separately for handling. The parser keeps its state
    /// between calls, so sequences split across reads are handled.
    /// </summary>
    TelnetParseResult Process(ReadOnlySpan<byte> data);

    /// <summary>
    /// Builds a response for a telnet negotiation command.
    /// </summary>
    byte[] BuildResponse(TelnetCommand command, TelnetVerb verb);

    /// <summary>Forgets any partially received sequence (new connection).</summary>
    void Reset();
}
