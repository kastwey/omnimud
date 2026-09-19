namespace Omnimud.Core.Telnet;

public readonly struct TelnetParseResult
{
    /// <summary>Clean text bytes with IAC sequences removed.</summary>
    public ReadOnlyMemory<byte> CleanData { get; init; }

    /// <summary>IAC commands found during parsing.</summary>
    public IReadOnlyList<TelnetNegotiation> Negotiations { get; init; }

    /// <summary>GMCP messages received in this data chunk.</summary>
    public IReadOnlyList<GmcpMessage> GmcpMessages { get; init; }

    /// <summary>True when the chunk ends with IAC GA or IAC EOR: whatever text is pending without a
    /// newline is a complete prompt.</summary>
    public bool EndsWithPromptMark { get; init; }
}

public readonly record struct TelnetNegotiation(TelnetVerb Verb, TelnetCommand Command);

/// <summary>
/// A parsed GMCP message with package name and JSON payload.
/// </summary>
public sealed record GmcpMessage(string Package, string Payload);
