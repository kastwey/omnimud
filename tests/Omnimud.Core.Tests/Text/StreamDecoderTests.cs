using System.Text;
using FluentAssertions;
using Omnimud.Core.Text;

namespace Omnimud.Core.Tests.Text;

public class StreamDecoderTests
{
    [Fact]
    public void Decode_Utf8CharacterSplitInTwoReads_IsDecodedOnce()
    {
        var sut = new StreamDecoder("utf-8");
        var bytes = Encoding.UTF8.GetBytes("camión");
        var cut = Array.IndexOf(bytes, (byte)0xC3) + 1; // in the middle of "ó"

        var first = sut.Decode(bytes.AsSpan(0, cut));
        var second = sut.Decode(bytes.AsSpan(cut));

        first.Should().Be("cami");
        second.Should().Be("ón");
    }

    [Fact]
    public void Decode_FourByteCharacterOneByteAtATime_ProducesOneSurrogatePair()
    {
        var sut = new StreamDecoder("utf-8");
        var bytes = Encoding.UTF8.GetBytes("a😀b");

        var sb = new StringBuilder();
        foreach (var b in bytes)
            sb.Append(sut.Decode([b]));

        sb.ToString().Should().Be("a😀b");
    }

    [Fact]
    public void Decode_Latin1_MapsHighBytes()
    {
        var sut = new StreamDecoder("iso-8859-1");

        sut.Decode([0xF1, 0xE1]).Should().Be("ñá");
    }

    [Fact]
    public void Decode_Windows1252_IsAvailable()
    {
        var sut = new StreamDecoder("windows-1252");

        sut.Decode([0x80]).Should().Be("€");
    }

    [Fact]
    public void Decode_Empty_ReturnsEmpty()
    {
        new StreamDecoder("utf-8").Decode([]).Should().BeEmpty();
    }

    [Fact]
    public void Reset_DropsPartialCharacter()
    {
        var sut = new StreamDecoder("utf-8");
        sut.Decode([0xC3]);

        sut.Reset();

        sut.Decode([(byte)'a']).Should().Be("a");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-such-encoding")]
    public void ResolveEncoding_UnknownOrEmpty_FallsBackToUtf8(string? name)
    {
        StreamDecoder.ResolveEncoding(name).WebName.Should().Be("utf-8");
    }

    [Fact]
    public void ResolveEncoding_Utf8_HasNoByteOrderMark()
    {
        StreamDecoder.ResolveEncoding("utf-8").GetPreamble().Should().BeEmpty();
    }
}
