using System.Text;
using FluentAssertions;
using Omnimud.Core.Telnet;

namespace Omnimud.Core.Tests.Telnet;

public class TelnetNegotiatorStateTests
{
    private const byte Iac = 255, Will = 251, Do = 253, Sb = 250, Se = 240, Ga = 249, Eor = 239, Gmcp = 201;

    private readonly TelnetNegotiator _sut = new();

    private static string Text(TelnetParseResult r) => Encoding.ASCII.GetString(r.CleanData.Span);

    [Fact]
    public void Process_IacAtEndOfBuffer_CompletesWithNextRead()
    {
        var first = _sut.Process([(byte)'a', Iac]);
        var second = _sut.Process([Will, 1, (byte)'b']);

        Text(first).Should().Be("a");
        first.Negotiations.Should().BeEmpty();
        Text(second).Should().Be("b");
        second.Negotiations.Should().ContainSingle().Which.Should().Be(new TelnetNegotiation(TelnetVerb.Will, TelnetCommand.Echo));
    }

    [Fact]
    public void Process_VerbAtEndOfBuffer_OptionArrivesLater()
    {
        var first = _sut.Process([Iac, Do]);
        var second = _sut.Process([31, (byte)'x']);

        first.Negotiations.Should().BeEmpty();
        second.Negotiations.Should().ContainSingle().Which.Should().Be(new TelnetNegotiation(TelnetVerb.Do, TelnetCommand.WindowSize));
        Text(second).Should().Be("x");
    }

    [Fact]
    public void Process_EscapedIacSplitAcrossReads_ProducesOneLiteral()
    {
        var first = _sut.Process([(byte)'a', Iac]);
        var second = _sut.Process([Iac, (byte)'b']);

        first.CleanData.ToArray().Should().Equal((byte)'a');
        second.CleanData.ToArray().Should().Equal(Iac, (byte)'b');
    }

    [Fact]
    public void Process_SubnegotiationWithoutSe_SwallowsUntilSeArrives()
    {
        var first = _sut.Process([(byte)'a', Iac, Sb, 24, 1, 2, 3]);
        var second = _sut.Process([4, 5]);
        var third = _sut.Process([Iac, Se, (byte)'z']);

        Text(first).Should().Be("a");
        Text(second).Should().BeEmpty();
        Text(third).Should().Be("z");
    }

    [Fact]
    public void Process_IacSeSplitAcrossReads_EndsSubnegotiation()
    {
        _sut.Process([Iac, Sb, 24, 1, Iac]);
        var result = _sut.Process([Se, (byte)'k']);

        Text(result).Should().Be("k");
    }

    [Fact]
    public void Process_GmcpSplitInThreeReads_IsParsedOnce()
    {
        var payload = Encoding.UTF8.GetBytes("Comm.Channel.Text {\"channel\":\"chat\"}");
        var packet = new List<byte> { Iac, Sb, Gmcp };
        packet.AddRange(payload);
        packet.AddRange([Iac, Se]);
        var bytes = packet.ToArray();

        var r1 = _sut.Process(bytes.AsSpan(0, 2));
        var r2 = _sut.Process(bytes.AsSpan(2, 10));
        var r3 = _sut.Process(bytes.AsSpan(12));

        r1.GmcpMessages.Should().BeEmpty();
        r2.GmcpMessages.Should().BeEmpty();
        r3.GmcpMessages.Should().ContainSingle();
        r3.GmcpMessages[0].Package.Should().Be("Comm.Channel.Text");
        r3.GmcpMessages[0].Payload.Should().Be("{\"channel\":\"chat\"}");
    }

    [Fact]
    public void Process_EscapedIacInsideSubnegotiation_IsData()
    {
        var result = _sut.Process([Iac, Sb, Gmcp, (byte)'a', Iac, Iac, (byte)'b', Iac, Se]);

        result.GmcpMessages.Should().ContainSingle();
        result.CleanData.Length.Should().Be(0);
    }

    [Fact]
    public void Process_GmcpWithoutPayload_GetsEmptyObject()
    {
        var bytes = new List<byte> { Iac, Sb, Gmcp };
        bytes.AddRange(Encoding.ASCII.GetBytes("Core.Ping"));
        bytes.AddRange([Iac, Se]);

        var result = _sut.Process(bytes.ToArray());

        result.GmcpMessages.Should().ContainSingle().Which.Should().Be(new GmcpMessage("Core.Ping", "{}"));
    }

    [Theory]
    [InlineData(Ga)]
    [InlineData(Eor)]
    public void Process_GoAheadOrEorAtEnd_MarksPromptAndLeavesNoGarbage(byte mark)
    {
        var result = _sut.Process([(byte)'>', (byte)' ', Iac, mark]);

        Text(result).Should().Be("> ");
        result.EndsWithPromptMark.Should().BeTrue();
    }

    [Fact]
    public void Process_GoAheadFollowedByText_IsNotAPromptMark()
    {
        var result = _sut.Process([(byte)'>', Iac, Ga, (byte)'x']);

        Text(result).Should().Be(">x");
        result.EndsWithPromptMark.Should().BeFalse();
    }

    [Fact]
    public void Process_UnknownTwoByteCommand_IsDropped()
    {
        var result = _sut.Process([(byte)'a', Iac, 241, (byte)'b']); // NOP

        Text(result).Should().Be("ab");
    }

    [Fact]
    public void Process_ByteByByte_GivesSameResultAsWhole()
    {
        byte[] stream = [(byte)'h', Iac, Will, 1, (byte)'i', Iac, Iac, Iac, Sb, 24, 9, Iac, Se, (byte)'!', Iac, Do, 3];
        var whole = new TelnetNegotiator().Process(stream);

        var clean = new List<byte>();
        var negotiations = new List<TelnetNegotiation>();
        foreach (var b in stream)
        {
            var r = _sut.Process([b]);
            clean.AddRange(r.CleanData.ToArray());
            negotiations.AddRange(r.Negotiations);
        }

        clean.Should().Equal(whole.CleanData.ToArray());
        negotiations.Should().Equal(whole.Negotiations);
    }

    [Fact]
    public void Reset_ForgetsPartialSequence()
    {
        _sut.Process([Iac, Sb, 24, 1, 2]);

        _sut.Reset();
        var result = _sut.Process([(byte)'o', (byte)'k']);

        Text(result).Should().Be("ok");
    }

    [Fact]
    public void Process_EndlessSubnegotiation_DoesNotGrowWithoutLimit()
    {
        _sut.Process([Iac, Sb, Gmcp]);
        var chunk = new byte[100_000];
        Array.Fill(chunk, (byte)'x');

        var act = () => _sut.Process(chunk);

        act.Should().NotThrow();
        _sut.Process([Iac, Se]).GmcpMessages.Should().ContainSingle()
            .Which.Package.Length.Should().BeLessThanOrEqualTo(64 * 1024);
    }

    [Fact]
    public void BuildGmcpPacket_WrapsPackageAndPayload()
    {
        var packet = TelnetNegotiator.BuildGmcpPacket("Core.Supports.Set", "[\"Comm.Channel 1\"]");

        packet[..3].Should().Equal(Iac, Sb, Gmcp);
        packet[^2..].Should().Equal(Iac, Se);
        Encoding.UTF8.GetString(packet[3..^2]).Should().Be("Core.Supports.Set [\"Comm.Channel 1\"]");
    }

    [Fact]
    public void EscapeIac_DoublesEvery255()
    {
        TelnetNegotiator.EscapeIac([1, 255, 2, 255]).Should().Equal(1, 255, 255, 2, 255, 255);
        var untouched = new byte[] { 1, 2, 3 };
        TelnetNegotiator.EscapeIac(untouched).Should().BeSameAs(untouched);
    }
}
