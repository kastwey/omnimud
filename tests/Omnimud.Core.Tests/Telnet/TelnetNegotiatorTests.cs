using FluentAssertions;
using Omnimud.Core.Telnet;

namespace Omnimud.Core.Tests.Telnet;

public class TelnetNegotiatorTests
{
    private readonly TelnetNegotiator _sut = new();

    [Fact]
    public void Process_PlainText_ReturnsUnmodified()
    {
        var data = "Hello World"u8.ToArray();

        var result = _sut.Process(data);

        result.CleanData.ToArray().Should().BeEquivalentTo(data);
        result.Negotiations.Should().BeEmpty();
    }

    [Fact]
    public void Process_IacWillEcho_ExtractsNegotiation()
    {
        byte[] data = [255, 251, 1]; // IAC WILL ECHO

        var result = _sut.Process(data);

        result.CleanData.Length.Should().Be(0);
        result.Negotiations.Should().HaveCount(1);
        result.Negotiations[0].Verb.Should().Be(TelnetVerb.Will);
        result.Negotiations[0].Command.Should().Be(TelnetCommand.Echo);
    }

    [Fact]
    public void Process_IacDontMsp_ExtractsNegotiation()
    {
        byte[] data = [255, 254, 90]; // IAC DONT MSP

        var result = _sut.Process(data);

        result.CleanData.Length.Should().Be(0);
        result.Negotiations.Should().HaveCount(1);
        result.Negotiations[0].Verb.Should().Be(TelnetVerb.Dont);
        result.Negotiations[0].Command.Should().Be(TelnetCommand.Msp);
    }

    [Fact]
    public void Process_TextWithEmbeddedIac_SeparatesCorrectly()
    {
        // "Hi" + IAC WILL ECHO + " there"
        byte[] data = [72, 105, 255, 251, 1, 32, 116, 104, 101, 114, 101];

        var result = _sut.Process(data);

        var cleanText = System.Text.Encoding.ASCII.GetString(result.CleanData.ToArray());
        cleanText.Should().Be("Hi there");
        result.Negotiations.Should().HaveCount(1);
    }

    [Fact]
    public void Process_EscapedIac_ProducesLiteral255()
    {
        byte[] data = [65, 255, 255, 66]; // A, IAC IAC, B → A, 0xFF, B

        var result = _sut.Process(data);

        result.CleanData.ToArray().Should().BeEquivalentTo(new byte[] { 65, 255, 66 });
        result.Negotiations.Should().BeEmpty();
    }

    [Fact]
    public void Process_Subnegotiation_IsSkipped()
    {
        // IAC SB (250) TERMINAL_TYPE (24) ... IAC SE (240)
        byte[] data = [255, 250, 24, 0, 1, 2, 3, 255, 240, 65];

        var result = _sut.Process(data);

        result.CleanData.ToArray().Should().BeEquivalentTo(new byte[] { 65 });
    }

    [Fact]
    public void Process_MultipleNegotiations_ExtractsAll()
    {
        // IAC WILL ECHO + IAC DO MSP
        byte[] data = [255, 251, 1, 255, 253, 90];

        var result = _sut.Process(data);

        result.Negotiations.Should().HaveCount(2);
        result.Negotiations[0].Should().Be(new TelnetNegotiation(TelnetVerb.Will, TelnetCommand.Echo));
        result.Negotiations[1].Should().Be(new TelnetNegotiation(TelnetVerb.Do, TelnetCommand.Msp));
    }

    [Fact]
    public void BuildResponse_CreatesCorrectIacSequence()
    {
        var response = _sut.BuildResponse(TelnetCommand.Echo, TelnetVerb.Do);

        response.Should().BeEquivalentTo(new byte[] { 255, 253, 1 });
    }

    [Fact]
    public void Process_EmptyInput_ReturnsEmpty()
    {
        var result = _sut.Process(ReadOnlySpan<byte>.Empty);

        result.CleanData.Length.Should().Be(0);
        result.Negotiations.Should().BeEmpty();
    }
}
